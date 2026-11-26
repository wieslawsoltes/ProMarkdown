using System.Text.Json;

namespace ProMarkdown.Plugin.Mermaid;

internal static class MermaidFlowchartMetadata
{
    private static readonly (string Shape, string Open, string Close)[] ClassicShapes =
    [
        ("dbl-circ", "(((", ")))"),
        ("circle", "((", "))"),
        ("stadium", "([", "])"),
        ("fr-rect", "[[", "]]"),
        ("cyl", "[(", ")]"),
        ("trap-b", "[/", "\\]"),
        ("lean-r", "[/", "/]"),
        ("trap-t", "[\\", "/]"),
        ("lean-l", "[\\", "\\]"),
        ("hex", "{{", "}}"),
        ("rounded", "(", ")"),
        ("diam", "{", "}"),
        ("odd", ">", "]"),
        ("rect", "[", "]")
    ];

    private static bool TryReadProperty(ref ReadOnlySpan<char> properties, out ReadOnlySpan<char> name,
        out ReadOnlySpan<char> value, out MermaidYamlBlockScalar? blockScalar)
    {
        blockScalar = null;
        while (!properties.IsEmpty)
        {
            var untrimmed = properties;
            properties = properties.TrimStart(" \t,\r\n");
            if (properties.StartsWith('#'))
            {
                var newline = properties.IndexOfAny('\r', '\n');
                properties = newline < 0 ? [] : properties[(newline + 1)..];
                continue;
            }
            var separator = properties.IndexOfAny(":,\r\n");
            if (separator < 0)
                break;
            if (properties[separator] != ':')
            {
                properties = properties[(separator + 1)..];
                continue;
            }

            name = properties[..separator].Trim();
            var body = properties[(separator + 1)..].TrimStart(" \t");
            if (!body.IsEmpty && body[0] is '|' or '>' &&
                MermaidYamlBlockScalar.TryRead(body, GetLineIndent(untrimmed, untrimmed.Length - properties.Length), out var scalar))
            {
                blockScalar = scalar;
                value = body[..scalar.End];
                properties = body[scalar.End..];
                return true;
            }
            var delimiter = FindDelimiter(body, block: false, out var commentStart);
            var valueEnd = commentStart >= 0 ? commentStart : delimiter < 0 ? body.Length : delimiter;
            value = body[..valueEnd].Trim();
            properties = delimiter < 0 ? [] : body[(delimiter + 1)..];
            return true;
        }

        name = value = [];
        return false;
    }

    public static int FindBlockEnd(ReadOnlySpan<char> body) => FindDelimiter(body, block: true, out _);

    private static int FindDelimiter(ReadOnlySpan<char> body, bool block, out int commentStart)
    {
        var nesting = 0;
        var startsScalar = true;
        commentStart = -1;
        for (var index = 0; index < body.Length; index++)
        {
            var current = body[index];
            if (startsScalar && current is '|' or '>' &&
                MermaidYamlBlockScalar.TryRead(body[index..], GetLineIndent(body, index), out var scalar))
            {
                index += scalar.End - 1;
                startsScalar = true;
                continue;
            }
            if (startsScalar && current is '\'' or '"')
            {
                var length = FindQuotedValueEnd(body[index..]);
                if (length == 0)
                    return -1;
                index += length - 1;
                startsScalar = false;
                continue;
            }
            if (current == '#' && (index == 0 || char.IsWhiteSpace(body[index - 1])))
            {
                if (!block && nesting == 0)
                    commentStart = index;
                var newline = body[index..].IndexOfAny('\r', '\n');
                if (newline < 0)
                    return -1;
                index += newline;
                current = body[index];
            }
            if (nesting == 0 && (block ? current == '}' : current is ',' or '\r' or '\n'))
                return index;
            if (current is '[' or '{')
                nesting++;
            else if (nesting > 0 && current is ']' or '}')
                nesting--;
            if (!char.IsWhiteSpace(current) || current is '\r' or '\n')
                startsScalar = current is ':' or ',' or '[' or '{' or '\r' or '\n';
        }
        return -1;
    }

    private static int GetLineIndent(ReadOnlySpan<char> source, int position)
    {
        var lineStart = source[..position].LastIndexOfAny('\r', '\n') + 1;
        var before = source[lineStart..position];
        return before.Length - before.TrimStart(' ').Length;
    }

    private static int FindQuotedValueEnd(ReadOnlySpan<char> body)
    {
        var quote = body[0];
        for (var index = 1; index < body.Length; index++)
        {
            if (quote == '"' && body[index] == '\\')
            {
                index++;
                continue;
            }
            if (body[index] != quote)
                continue;
            if (quote != '\'' || index + 1 >= body.Length || body[index + 1] != '\'')
                return index + 1;
            index++;
        }
        return 0;
    }

    public static NodeAppearance ReadNodeAppearance(string properties)
    {
        var appearance = default(NodeAppearance);
        var fields = properties.AsSpan();
        while (TryReadProperty(ref fields, out var name, out var value, out var blockScalar))
        {
            if (blockScalar is null && value is "null" or "Null" or "NULL" or "~")
                continue;
            switch (name)
            {
                case "label":
                    var label = blockScalar is { } scalar ? scalar.ReadValue(value) : ReadLabel(value);
                    if (label.Length > 0)
                        appearance = appearance with { Label = label };
                    break;
                case "shape":
                    var shape = value.Trim("\"'");
                    if (!shape.IsEmpty)
                        appearance = appearance with { Shape = shape.ToString() };
                    break;
            }
        }
        return appearance;
    }

    public static string FormatNodeDeclaration(string nodeId, string authoredId, NodeAppearance appearance)
    {
        if (appearance.Label is null && string.IsNullOrEmpty(appearance.Shape) && nodeId == authoredId)
            return nodeId;

        var shape = NormalizeShape(appearance.Shape);
        var display = (appearance.Label ?? authoredId).Replace("\"", "#quot;", StringComparison.Ordinal).ReplaceLineEndings("<br/>");
        foreach (var (name, open, close) in ClassicShapes)
        {
            if (name == shape)
                return $"{nodeId}{open}\"{display}\"{close}";
        }
        return $"{nodeId}[\"{display}\"]";
    }

    public static NodeAppearance ReadClassicAppearance(ReadOnlySpan<char> suffix)
    {
        foreach (var (shape, open, close) in ClassicShapes)
        {
            if (suffix.Length < open.Length + close.Length || !suffix.StartsWith(open) || !suffix.EndsWith(close))
                continue;
            var label = suffix[open.Length..^close.Length];
            var trimmed = label.TrimEnd();
            if (trimmed is ['"', .., '"'])
                label = trimmed[1..^1];
            return new NodeAppearance(label.ToString(), shape);
        }
        return default;
    }

    private static string? NormalizeShape(string? shape) => shape switch
    {
        "event" => "rounded",
        "pill" or "terminal" => "stadium",
        "framed-rectangle" or "subproc" or "subprocess" or "subroutine" => "fr-rect",
        "cylinder" or "database" or "db" => "cyl",
        "decision" or "diamond" or "question" => "diam",
        "hexagon" or "prepare" => "hex",
        "circ" => "circle",
        "double-circle" => "dbl-circ",
        "in-out" or "lean-right" => "lean-r",
        "lean-left" or "out-in" => "lean-l",
        "priority" or "trapezoid" or "trapezoid-bottom" => "trap-b",
        "inv-trapezoid" or "manual" or "trapezoid-top" => "trap-t",
        _ => shape
    };

    private static string ReadLabel(ReadOnlySpan<char> value)
    {
        if (value is ['\'', .., '\''])
            return value[1..^1].ToString().Replace("''", "'", StringComparison.Ordinal);
        if (value is not ['"', .., '"'])
            return value.ToString();
        if (value.Contains('\\'))
        {
            try
            {
                using var json = JsonDocument.Parse(value.ToString());
                return json.RootElement.GetString()!;
            }
            catch (JsonException)
            {
                // YAML accepts escapes beyond the JSON subset. Keep their authored text.
            }
        }
        return value[1..^1].ToString();
    }

    internal readonly record struct NodeAppearance(string? Label, string? Shape);
}
