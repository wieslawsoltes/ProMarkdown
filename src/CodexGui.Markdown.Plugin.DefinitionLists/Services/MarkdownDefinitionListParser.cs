using System.Text;
using CodexGui.Markdown.Services;
using Markdig;
using Markdig.Extensions.DefinitionLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CodexGui.Markdown.Plugin.DefinitionLists;

internal static class MarkdownDefinitionListParser
{
    private static readonly Lazy<MarkdownPipeline> Pipeline = new(() => new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build());

    public static MarkdownDefinitionListDocument Parse(DefinitionList definitionList, MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(definitionList);
        ArgumentNullException.ThrowIfNull(parseResult);

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(definitionList.Span);
        var source = sourceSpan.Slice(parseResult.OriginalMarkdown);
        if (string.IsNullOrWhiteSpace(source))
        {
            source = MarkdownDefinitionListSyntax.NormalizeBlockText(definitionList.ToString());
        }

        return Parse(source);
    }

    public static MarkdownDefinitionListDocument Parse(string? markdown)
    {
        var source = MarkdownDefinitionListSyntax.NormalizeBlockText(markdown);
        var diagnostics = new List<MarkdownDefinitionListDiagnostic>();
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(new MarkdownDefinitionListDiagnostic("Definition list content is empty.", MarkdownSourceSpan.Empty));
            return new MarkdownDefinitionListDocument(source, Array.Empty<MarkdownDefinitionEntry>(), diagnostics);
        }

        var document = Markdig.Markdown.Parse(source, Pipeline.Value);
        var entries = new List<MarkdownDefinitionEntry>();

        foreach (var block in document)
        {
            switch (block)
            {
                case DefinitionList definitionList:
                    entries.AddRange(ParseDefinitionList(definitionList, source, diagnostics));
                    break;
                case BlankLineBlock:
                    break;
                default:
                    diagnostics.Add(new MarkdownDefinitionListDiagnostic(
                        $"Unexpected block '{block.GetType().Name}' inside definition-list source.",
                        MarkdownSourceSpan.FromMarkdig(block.Span)));
                    break;
            }
        }

        if (entries.Count == 0)
        {
            diagnostics.Add(new MarkdownDefinitionListDiagnostic(
                "Markdig did not produce any definition-list entries from the provided markdown.",
                MarkdownSourceSpan.Empty));
        }

        return new MarkdownDefinitionListDocument(source, entries, diagnostics);
    }

    private static IEnumerable<MarkdownDefinitionEntry> ParseDefinitionList(
        DefinitionList definitionList,
        string source,
        List<MarkdownDefinitionListDiagnostic> diagnostics)
    {
        foreach (var definitionItem in definitionList.OfType<DefinitionItem>())
        {
            if (ParseDefinitionItem(definitionItem, source, diagnostics) is { } entry)
            {
                yield return entry;
            }
        }
    }

    private static MarkdownDefinitionEntry? ParseDefinitionItem(
        DefinitionItem definitionItem,
        string source,
        List<MarkdownDefinitionListDiagnostic> diagnostics)
    {
        var terms = new List<MarkdownDefinitionTerm>();
        var contentBlocks = new List<Block>();

        foreach (var child in definitionItem)
        {
            switch (child)
            {
                case DefinitionTerm definitionTerm:
                    if (ParseDefinitionTerm(definitionTerm, source) is { } term)
                    {
                        terms.Add(term);
                    }
                    break;
                case BlankLineBlock:
                    break;
                default:
                    contentBlocks.Add(child);
                    break;
            }
        }

        var itemSpan = MarkdownSourceSpan.FromMarkdig(definitionItem.Span);
        if (terms.Count == 0)
        {
            diagnostics.Add(new MarkdownDefinitionListDiagnostic("Definition-list entry is missing a term.", itemSpan));
        }

        var definitionSpan = ResolveCombinedSpan(contentBlocks);
        var definitionMarkdown = definitionSpan.Slice(source).TrimEnd();
        if (string.IsNullOrWhiteSpace(definitionMarkdown))
        {
            diagnostics.Add(new MarkdownDefinitionListDiagnostic("Definition-list entry is missing definition content.", itemSpan));
        }

        if (terms.Count == 0 && string.IsNullOrWhiteSpace(definitionMarkdown))
        {
            return null;
        }

        return new MarkdownDefinitionEntry(
            terms,
            MarkdownDefinitionListSyntax.NormalizeBlockText(definitionMarkdown),
            itemSpan);
    }

    private static MarkdownDefinitionTerm? ParseDefinitionTerm(DefinitionTerm definitionTerm, string source)
    {
        var span = MarkdownSourceSpan.FromMarkdig(definitionTerm.Span);
        var markdown = span.Slice(source).Trim();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            markdown = MarkdownDefinitionListSyntax.NormalizeInlineText(definitionTerm.Lines.ToString());
        }

        var plainText = ExtractInlineText(definitionTerm.Inline);
        if (string.IsNullOrWhiteSpace(plainText))
        {
            plainText = markdown;
        }

        return string.IsNullOrWhiteSpace(markdown)
            ? null
            : new MarkdownDefinitionTerm(markdown, plainText.Trim(), span);
    }

    private static MarkdownSourceSpan ResolveCombinedSpan(IReadOnlyList<Block> blocks)
    {
        if (blocks.Count == 0)
        {
            return MarkdownSourceSpan.Empty;
        }

        var firstSpan = MarkdownSourceSpan.FromMarkdig(blocks[0].Span);
        var lastSpan = MarkdownSourceSpan.FromMarkdig(blocks[^1].Span);
        if (firstSpan.IsEmpty || lastSpan.IsEmpty)
        {
            return MarkdownSourceSpan.Empty;
        }

        return new MarkdownSourceSpan(firstSpan.Start, lastSpan.EndExclusive - firstSpan.Start);
    }

    private static string ExtractInlineText(ContainerInline? containerInline)
    {
        if (containerInline is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var child = containerInline.FirstChild;
        while (child is not null)
        {
            AppendInlineText(builder, child);
            child = child.NextSibling;
        }

        return builder.ToString();
    }

    private static void AppendInlineText(StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;
            case CodeInline codeInline:
                builder.Append(codeInline.Content);
                break;
            case HtmlEntityInline htmlEntityInline:
                builder.Append(htmlEntityInline.Transcoded.ToString());
                break;
            case HtmlInline htmlInline:
                builder.Append(htmlInline.Tag);
                break;
            case LinkInline linkInline:
                if (linkInline.FirstChild is not null)
                {
                    var child = linkInline.FirstChild;
                    while (child is not null)
                    {
                        AppendInlineText(builder, child);
                        child = child.NextSibling;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(linkInline.Url))
                {
                    builder.Append(linkInline.Url);
                }
                break;
            case LineBreakInline:
                builder.Append(' ');
                break;
            case ContainerInline nested:
                var childInline = nested.FirstChild;
                while (childInline is not null)
                {
                    AppendInlineText(builder, childInline);
                    childInline = childInline.NextSibling;
                }
                break;
            default:
                builder.Append(inline.ToString());
                break;
        }
    }
}

internal static class MarkdownDefinitionListSyntax
{
    public static string NormalizeLineEndings(string? source)
    {
        return (source ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string NormalizeBlockText(string? source)
    {
        return NormalizeLineEndings(source).TrimEnd('\n');
    }

    public static string NormalizeInlineText(string? source)
    {
        return NormalizeBlockText(source).Replace('\n', ' ').Trim();
    }

    public static string BuildDefinitionList(IReadOnlyList<MarkdownDefinitionDraftEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        for (var index = 0; index < entries.Count; index++)
        {
            if (index > 0)
            {
                builder.AppendLine();
            }

            builder.Append(BuildDefinitionEntry(entries[index]));
        }

        return NormalizeBlockText(builder.ToString());
    }

    public static string BuildDefinitionEntry(MarkdownDefinitionDraftEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var terms = entry.TermsText
            .Split('\n', StringSplitOptions.None)
            .Select(NormalizeInlineText)
            .Where(static term => !string.IsNullOrWhiteSpace(term))
            .ToArray();
        if (terms.Length == 0)
        {
            terms = ["Term"];
        }

        var definitionLines = NormalizeDefinitionLines(entry.DefinitionMarkdown);
        if (definitionLines.Count == 0)
        {
            definitionLines.Add("Definition text.");
        }

        var builder = new StringBuilder();
        foreach (var term in terms)
        {
            builder.AppendLine(term);
        }

        builder.Append(":   ");
        builder.AppendLine(definitionLines[0]);

        for (var lineIndex = 1; lineIndex < definitionLines.Count; lineIndex++)
        {
            var line = definitionLines[lineIndex];
            if (line.Length == 0)
            {
                builder.AppendLine();
            }
            else
            {
                builder.Append("    ");
                builder.AppendLine(line);
            }
        }

        return NormalizeBlockText(builder.ToString());
    }

    private static List<string> NormalizeDefinitionLines(string? definitionMarkdown)
    {
        var lines = NormalizeLineEndings(definitionMarkdown)
            .Split('\n', StringSplitOptions.None)
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}
