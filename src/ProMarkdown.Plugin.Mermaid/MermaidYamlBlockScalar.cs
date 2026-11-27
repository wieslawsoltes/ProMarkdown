using System.Text;

namespace ProMarkdown.Plugin.Mermaid;

internal readonly record struct MermaidYamlBlockScalar(int ContentStart, int End, int Indent, bool Fold, char Chomp)
{
    public static bool TryRead(ReadOnlySpan<char> source, int propertyIndent, out MermaidYamlBlockScalar scalar)
    {
        scalar = default;
        if (source.IsEmpty || source[0] is not ('|' or '>'))
            return false;
        var headerEnd = source.IndexOfAny('\r', '\n');
        if (headerEnd < 0)
            return false;

        var header = source[1..headerEnd];
        var comment = header.IndexOf('#');
        if (comment > 0 && char.IsWhiteSpace(header[comment - 1]))
            header = header[..comment];
        var chomp = '\0';
        var indent = -1;
        foreach (var modifier in header.TrimEnd())
        {
            if (modifier is '+' or '-' && chomp == '\0')
                chomp = modifier;
            else if (modifier is >= '1' and <= '9' && indent < 0)
                indent = propertyIndent + (modifier - '0');
            else
                return false;
        }

        var contentStart = AfterLineBreak(source, headerEnd);
        var offset = contentStart;
        while (offset < source.Length)
        {
            var remaining = source[offset..];
            var lineBreak = remaining.IndexOfAny('\r', '\n');
            var lineEnd = lineBreak < 0 ? source.Length : offset + lineBreak;
            var line = source[offset..lineEnd];
            if (!line.Trim().IsEmpty)
            {
                var lineIndent = line.Length - line.TrimStart(' ').Length;
                if (lineIndent <= propertyIndent || (indent >= 0 && lineIndent < indent))
                    break;
                if (indent < 0)
                    indent = lineIndent;
            }
            offset = AfterLineBreak(source, lineEnd);
        }

        scalar = new(contentStart, offset, Math.Max(0, indent), source[0] == '>', chomp);
        return true;
    }

    public string ReadValue(ReadOnlySpan<char> source)
    {
        var label = new StringBuilder();
        var pendingBreaks = 0;
        var hasText = false;
        var previousMoreIndented = false;
        var content = source[ContentStart..End];
        while (!content.IsEmpty)
        {
            var lineBreak = content.IndexOfAny('\r', '\n');
            var lineEnd = lineBreak < 0 ? content.Length : lineBreak;
            var line = content[Math.Min(Indent, lineEnd)..lineEnd];
            if (!line.IsEmpty)
            {
                var moreIndented = line.StartsWith(' ');
                if (Fold && hasText && !previousMoreIndented && !moreIndented && pendingBreaks == 1)
                    label.Append(' ');
                else
                    label.Append('\n', Fold && hasText && !previousMoreIndented && !moreIndented
                        ? Math.Max(0, pendingBreaks - 1)
                        : pendingBreaks);
                label.Append(line);
                hasText = true;
                previousMoreIndented = moreIndented;
                pendingBreaks = 0;
            }
            if (lineBreak >= 0)
                pendingBreaks++;
            content = content[AfterLineBreak(content, lineEnd)..];
        }
        if (Chomp == '+')
            label.Append('\n', pendingBreaks);
        else if (Chomp != '-' && hasText && pendingBreaks > 0)
            label.Append('\n');
        return label.ToString();
    }

    private static int AfterLineBreak(ReadOnlySpan<char> source, int end)
    {
        if (end == source.Length)
            return end;
        return source[end] == '\r' && end + 1 < source.Length && source[end + 1] == '\n' ? end + 2 : end + 1;
    }
}
