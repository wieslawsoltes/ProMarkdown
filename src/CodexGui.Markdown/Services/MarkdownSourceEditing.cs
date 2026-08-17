using System.Text.RegularExpressions;

namespace CodexGui.Markdown.Services;

internal static partial class MarkdownSourceEditing
{
    public static string Replace(string markdown, MarkdownSourceSpan span, string replacement)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(replacement);

        if (span.IsEmpty)
        {
            return markdown;
        }

        var start = Math.Clamp(span.Start, 0, markdown.Length);
        var end = Math.Clamp(span.EndExclusive, start, markdown.Length);
        return string.Concat(markdown.AsSpan(0, start), replacement, markdown.AsSpan(end));
    }

    public static string Remove(string markdown, MarkdownSourceSpan span)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Replace(markdown, span, string.Empty);
    }

    public static string NormalizeLineEndings(string? text)
    {
        return string.IsNullOrEmpty(text)
            ? string.Empty
            : text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
    }

    public static string NormalizeBlockText(string? text)
    {
        return NormalizeLineEndings(text).TrimEnd('\n');
    }

    public static string NormalizeInlineText(string? text)
    {
        return NormalizeBlockText(text).Replace('\n', ' ');
    }

    public static string NormalizeInlineMarkdown(string? markdown)
    {
        return NormalizeLineEndings(markdown).Replace('\n', ' ').Trim();
    }

    public static string StripListMarker(string? text)
    {
        var normalized = NormalizeBlockText(text);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var lines = normalized.Split('\n', StringSplitOptions.None);
        lines[0] = ListMarkerRegex().Replace(lines[0], string.Empty, count: 1);
        for (var index = 1; index < lines.Length; index++)
        {
            lines[index] = lines[index].TrimStart();
        }

        return string.Join('\n', lines).Trim();
    }

    public static string ExtractHeadingText(string sourceText, int level)
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

        if (lines[0].TrimStart().StartsWith('#'))
        {
            var content = Regex.Replace(lines[0], @"^\s*#{1,6}\s*", string.Empty);
            content = Regex.Replace(content, @"\s+#+\s*$", string.Empty);
            return content.Trim();
        }

        if (lines.Length > 1 && UnderlineHeadingRegex().IsMatch(lines[^1]))
        {
            return string.Join('\n', lines[..^1]).Trim();
        }

        return normalized.Trim();
    }

    public static string BuildHeadingMarkdown(int level, string text)
    {
        return $"{new string('#', Math.Clamp(level, 1, 6))} {NormalizeInlineMarkdown(text)}".TrimEnd();
    }

    public static string BuildParagraphMarkdown(string? text)
    {
        return NormalizeInlineMarkdown(text);
    }

    public static string NormalizeLanguageHint(string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(languageHint))
        {
            return string.Empty;
        }

        var trimmed = languageHint.Trim();
        var separatorIndex = trimmed.IndexOfAny([' ', '\t', ',', ';', '{', '(']);
        var normalized = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        return normalized.Trim().Trim('.').ToLowerInvariant();
    }

    public static string ResolveCodeFenceDelimiter(string code)
    {
        var normalized = NormalizeLineEndings(code);
        return normalized.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
    }

    public static string BuildCodeFence(string languageHint, string code)
    {
        var normalizedCode = NormalizeBlockText(code);
        var normalizedLanguage = NormalizeLanguageHint(languageHint);
        var fence = ResolveCodeFenceDelimiter(normalizedCode);
        return string.IsNullOrWhiteSpace(normalizedLanguage)
            ? $"{fence}\n{normalizedCode}\n{fence}"
            : $"{fence}{normalizedLanguage}\n{normalizedCode}\n{fence}";
    }

    public static string BuildBlockInsertionReplacement(
        string currentBlockMarkdown,
        string insertedBlockMarkdown,
        bool insertBefore,
        out int revealStart,
        out int revealLength)
    {
        var normalizedCurrentBlock = NormalizeBlockText(currentBlockMarkdown);
        var normalizedInsertedBlock = NormalizeBlockText(insertedBlockMarkdown);

        if (normalizedInsertedBlock.Length == 0)
        {
            revealStart = 0;
            revealLength = 0;
            return normalizedCurrentBlock;
        }

        if (normalizedCurrentBlock.Length == 0)
        {
            revealStart = 0;
            revealLength = normalizedInsertedBlock.Length;
            return normalizedInsertedBlock;
        }

        const string separator = "\n\n";
        if (insertBefore)
        {
            revealStart = 0;
            revealLength = normalizedInsertedBlock.Length;
            return string.Concat(normalizedInsertedBlock, separator, normalizedCurrentBlock);
        }

        revealStart = normalizedCurrentBlock.Length + separator.Length;
        revealLength = normalizedInsertedBlock.Length;
        return string.Concat(normalizedCurrentBlock, separator, normalizedInsertedBlock);
    }

    public static string SanitizeTableCell(string? text)
    {
        return NormalizeBlockText(text)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace('\n', ' ');
    }

    [GeneratedRegex(@"^\s*(?:[-+*]|\d+[.)])\s+(?:\[(?: |x|X)\]\s+)?", RegexOptions.CultureInvariant)]
    private static partial Regex ListMarkerRegex();

    [GeneratedRegex(@"^\s*[=-]{2,}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex UnderlineHeadingRegex();
}
