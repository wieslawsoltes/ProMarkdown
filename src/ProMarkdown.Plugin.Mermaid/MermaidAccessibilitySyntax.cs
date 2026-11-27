using System.Text;
using System.Text.RegularExpressions;

namespace ProMarkdown.Plugin.Mermaid;

internal static partial class MermaidAccessibilitySyntax
{
    private const string PercentMarkerPrefix = "__promarkdown_desc_percent__";

    [GeneratedRegex(@"(?m)^[^\S\r\n]*%%", RegexOptions.CultureInvariant)]
    private static partial Regex CommentLine();

    public static bool TryReadDirective(ReadOnlySpan<char> source, ref bool insideDescription, out int length, out int bracedContentStart)
    {
        length = 0;
        bracedContentStart = 0;
        if (insideDescription)
        {
            length = ReadDescription(source, ref insideDescription);
            return true;
        }

        const string description = "accDescr";
        const string title = "accTitle";
        var content = TrimLeadingWhitespaceOnLine(source);
        var isDescription = content.StartsWith(description, StringComparison.Ordinal);
        if (!isDescription && !content.StartsWith(title, StringComparison.Ordinal))
            return false;

        var suffix = TrimLeadingWhitespaceOnLine(content[(isDescription ? description.Length : title.Length)..]);
        var suffixStart = source.Length - suffix.Length;
        if (isDescription && suffix.StartsWith('{'))
        {
            bracedContentStart = suffixStart + 1;
            length = bracedContentStart + ReadDescription(source[bracedContentStart..], ref insideDescription);
            return true;
        }

        if (!suffix.StartsWith(':'))
            return false;
        var lineEnd = suffix.IndexOfAny('\r', '\n');
        var line = lineEnd < 0 ? suffix : suffix[..lineEnd];
        if (line[1..].Trim().IsEmpty)
            return false;

        length = suffixStart + line.Length;
        return true;
    }

    internal static string? GetPercentMarker(ReadOnlySpan<char> content, string source, ref string? sharedMarker)
    {
        if (!CommentLine().IsMatch(content))
            return null;

        return sharedMarker ??= source.Contains(PercentMarkerPrefix, StringComparison.Ordinal)
            ? PercentMarkerPrefix + Guid.NewGuid().ToString("N")
            : PercentMarkerPrefix;
    }

    internal static string FormatBracedDescription(ReadOnlySpan<char> content, string? percentMarker)
    {
        content = content.Trim();
        if (percentMarker is null)
            return string.Concat("accDescr {\n", content, "\n}");

        // Mermaider strips comment lines before reading accessibility directives.
        var formatted = new StringBuilder(content.Length).Append("accDescr {\n");
        var offset = 0;
        foreach (var match in CommentLine().EnumerateMatches(content))
        {
            var percentStart = match.Index + match.Length - 2;
            formatted.Append(content[offset..percentStart]).Append(percentMarker);
            offset = percentStart + 2;
        }
        return formatted.Append(content[offset..]).Append("\n}").ToString();
    }

    internal static string RestoreDescription(string svg, string? percentMarker)
    {
        if (percentMarker is null)
            return svg;

        var description = svg.IndexOf("<desc ", StringComparison.Ordinal);
        if (description < 0)
            description = svg.IndexOf("<desc>", StringComparison.Ordinal);
        if (description < 0)
            return svg;
        var openingEnd = svg.IndexOf('>', description);
        if (openingEnd < 0)
            return svg;
        var contentStart = openingEnd + 1;
        var contentEnd = svg.IndexOf("</desc>", contentStart, StringComparison.Ordinal);
        if (contentEnd < 0 || !svg.AsSpan(contentStart, contentEnd - contentStart).Contains(percentMarker, StringComparison.Ordinal))
            return svg;

        var content = svg[contentStart..contentEnd].Replace(percentMarker, "%%", StringComparison.Ordinal);
        return string.Concat(svg.AsSpan(0, contentStart), content, svg.AsSpan(contentEnd));
    }

    internal static ReadOnlySpan<char> TrimLeadingWhitespaceOnLine(ReadOnlySpan<char> source)
    {
        var index = 0;
        while (index < source.Length && source[index] is not ('\r' or '\n') && char.IsWhiteSpace(source[index]))
            index++;
        return source[index..];
    }

    private static int ReadDescription(ReadOnlySpan<char> source, ref bool insideDescription)
    {
        var closing = source.IndexOf('}');
        insideDescription = closing < 0;
        return insideDescription ? source.Length : closing + 1;
    }
}
