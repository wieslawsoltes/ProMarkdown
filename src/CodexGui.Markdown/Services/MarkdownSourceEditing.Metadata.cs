using System.Text;

namespace CodexGui.Markdown.Services;

internal static partial class MarkdownSourceEditing
{
    public static string BuildYamlFrontMatter(string? text)
    {
        var normalized = NormalizeBlockText(text);
        return normalized.Length == 0
            ? "---\n---"
            : $"---\n{normalized}\n---";
    }

    public static string ExtractYamlFrontMatterBody(string sourceText)
    {
        var normalized = NormalizeBlockText(sourceText);
        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length >= 2 &&
            string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal) &&
            string.Equals(lines[^1].Trim(), "---", StringComparison.Ordinal))
        {
            return string.Join('\n', lines[1..^1]);
        }

        return normalized;
    }

    public static string BuildAbbreviationMarkdown(string? label, string? text)
    {
        var normalizedLabel = SanitizeReferenceLabel(label, fallback: "TERM");
        var normalizedText = NormalizeInlineText(text);
        if (normalizedText.Length == 0)
        {
            normalizedText = "Meaning";
        }

        return $"*[{normalizedLabel}]: {normalizedText}";
    }

    public static string BuildLinkReferenceDefinition(string? label, string? url, string? title)
    {
        var normalizedLabel = SanitizeReferenceLabel(label, fallback: "reference");
        var normalizedUrl = NormalizeInlineText(url);
        if (normalizedUrl.Length == 0)
        {
            normalizedUrl = "https://example.com";
        }

        var normalizedTitle = NormalizeInlineText(title);
        return normalizedTitle.Length == 0
            ? $"[{normalizedLabel}]: {normalizedUrl}"
            : $"[{normalizedLabel}]: {normalizedUrl} \"{EscapeQuotedText(normalizedTitle)}\"";
    }

    public static string BuildFootnoteMarkdown(string? label, string? text)
    {
        var normalizedLabel = SanitizeReferenceLabel(label, fallback: "note");
        var normalizedText = NormalizeBlockText(text);
        var lines = normalizedText.Split('\n', StringSplitOptions.None);

        var builder = new StringBuilder();
        builder.Append("[^").Append(normalizedLabel).Append("]:");
        if (lines.Length == 0 || (lines.Length == 1 && lines[0].Length == 0))
        {
            builder.Append(" Footnote text.");
            return builder.ToString();
        }

        if (lines[0].Length > 0)
        {
            builder.Append(' ').Append(lines[0]);
        }

        for (var index = 1; index < lines.Length; index++)
        {
            builder.Append('\n');
            if (lines[index].Length > 0)
            {
                builder.Append("    ").Append(lines[index]);
            }
        }

        return builder.ToString();
    }

    public static string ExtractFootnoteBody(string sourceText)
    {
        var normalized = NormalizeBlockText(sourceText);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        var firstLine = lines[0];
        var markerSeparatorIndex = firstLine.IndexOf("]:", StringComparison.Ordinal);
        var outputLines = new List<string>();
        if (markerSeparatorIndex >= 0)
        {
            var firstContent = firstLine[(markerSeparatorIndex + 2)..].TrimStart();
            if (firstContent.Length > 0)
            {
                outputLines.Add(firstContent);
            }
        }
        else
        {
            outputLines.Add(firstLine);
        }

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                outputLines.Add(string.Empty);
                continue;
            }

            if (line.StartsWith("    ", StringComparison.Ordinal))
            {
                outputLines.Add(line[4..]);
                continue;
            }

            if (line.StartsWith('\t'))
            {
                outputLines.Add(line[1..]);
                continue;
            }

            outputLines.Add(line.TrimStart());
        }

        return string.Join('\n', outputLines).TrimEnd();
    }

    private static string SanitizeReferenceLabel(string? label, string fallback)
    {
        var normalized = NormalizeInlineText(label)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Replace("^", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Trim();

        return normalized.Length == 0 ? fallback : normalized;
    }

    private static string EscapeQuotedText(string text)
    {
        return text.Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
