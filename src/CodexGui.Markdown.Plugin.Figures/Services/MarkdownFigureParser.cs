using System.Text;
using CodexGui.Markdown.Services;
using Markdig;
using Markdig.Extensions.Figures;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Figures;

internal static class MarkdownFigureParser
{
    private static readonly Lazy<MarkdownPipeline> Pipeline = new(() => new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build());

    public static MarkdownFigureDocument Parse(Figure figure, MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(parseResult);

        List<MarkdownFigureDiagnostic> diagnostics = [];
        var sourceSpan = MarkdownSourceSpan.FromMarkdig(figure.Span);
        var sourceText = sourceSpan.Slice(parseResult.OriginalMarkdown);
        if (string.IsNullOrWhiteSpace(sourceText) && !parseResult.UsesOriginalSourceSpans)
        {
            sourceText = sourceSpan.Slice(parseResult.ParsedMarkdown);
        }

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            sourceText = BuildFallbackSource(figure, parseResult, out diagnostics);
            sourceSpan = string.IsNullOrWhiteSpace(sourceText) ? MarkdownSourceSpan.Empty : new MarkdownSourceSpan(0, sourceText.Length);
        }

        return CreateDocument(
            figure,
            string.IsNullOrWhiteSpace(sourceText) ? string.Empty : sourceText,
            sourceSpan.IsEmpty ? new MarkdownSourceSpan(0, sourceText.Length) : sourceSpan,
            diagnostics);
    }

    public static MarkdownFigureDocument Parse(string? markdown)
    {
        var source = MarkdownFigureSyntax.NormalizeBlockText(markdown);
        List<MarkdownFigureDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(new MarkdownFigureDiagnostic("Figure content is empty.", MarkdownSourceSpan.Empty));
            return new MarkdownFigureDocument(
                string.Empty,
                MarkdownSourceSpan.Empty,
                string.Empty,
                MarkdownSourceSpan.Empty,
                null,
                null,
                fenceLength: 3,
                diagnostics);
        }

        var document = Markdig.Markdown.Parse(source, Pipeline.Value);
        Figure? figure = null;
        foreach (var block in document)
        {
            switch (block)
            {
                case Figure parsedFigure when figure is null:
                    figure = parsedFigure;
                    break;
                case BlankLineBlock:
                    break;
                default:
                    diagnostics.Add(new MarkdownFigureDiagnostic(
                        $"Unexpected block '{block.GetType().Name}' inside figure source.",
                        MarkdownSourceSpan.FromMarkdig(block.Span)));
                    break;
            }
        }

        if (figure is null)
        {
            diagnostics.Add(new MarkdownFigureDiagnostic(
                "Markdig did not produce a figure block from the provided markdown.",
                MarkdownSourceSpan.Empty));

            return new MarkdownFigureDocument(
                source,
                new MarkdownSourceSpan(0, source.Length),
                string.Empty,
                MarkdownSourceSpan.Empty,
                null,
                null,
                MarkdownFigureSyntax.ReadFenceLength(source),
                diagnostics);
        }

        return CreateDocument(figure, source, new MarkdownSourceSpan(0, source.Length), diagnostics);
    }

    private static MarkdownFigureDocument CreateDocument(
        Figure figure,
        string sourceText,
        MarkdownSourceSpan containerSpan,
        List<MarkdownFigureDiagnostic> diagnostics)
    {
        var source = MarkdownFigureSyntax.NormalizeBlockText(containerSpan.IsEmpty ? sourceText : containerSpan.Slice(sourceText));
        if (string.IsNullOrWhiteSpace(source))
        {
            source = MarkdownFigureSyntax.NormalizeBlockText(sourceText);
        }

        var effectiveContainerSpan = new MarkdownSourceSpan(0, source.Length);
        var absoluteContainerSpan = containerSpan.IsEmpty ? new MarkdownSourceSpan(0, sourceText.Length) : containerSpan;
        var fenceLength = figure.OpeningCharacterCount > 0 ? figure.OpeningCharacterCount : MarkdownFigureSyntax.ReadFenceLength(source);

        MarkdownFigureCaption? leadingCaption = null;
        MarkdownFigureCaption? trailingCaption = null;
        List<Block> bodyBlocks = [];
        var encounteredBody = false;

        foreach (var child in figure)
        {
            switch (child)
            {
                case FigureCaption caption when !encounteredBody && leadingCaption is null:
                    leadingCaption = ParseCaption(caption, sourceText, absoluteContainerSpan, MarkdownFigureCaptionPlacement.OpeningFence);
                    break;
                case FigureCaption caption when trailingCaption is null:
                    trailingCaption = ParseCaption(caption, sourceText, absoluteContainerSpan, MarkdownFigureCaptionPlacement.ClosingFence);
                    break;
                case FigureCaption caption:
                    diagnostics.Add(new MarkdownFigureDiagnostic(
                        "Figure contains more than two fence captions.",
                        ToRelativeSpan(MarkdownSourceSpan.FromMarkdig(caption.Span), absoluteContainerSpan)));
                    break;
                case BlankLineBlock:
                    break;
                default:
                    if (trailingCaption is not null)
                    {
                        diagnostics.Add(new MarkdownFigureDiagnostic(
                            "Figure body content appears after a closing fence caption.",
                            ToRelativeSpan(MarkdownSourceSpan.FromMarkdig(child.Span), absoluteContainerSpan)));
                    }

                    encounteredBody = true;
                    bodyBlocks.Add(child);
                    break;
            }
        }

        var bodySpan = ToRelativeSpan(ResolveCombinedSpan(bodyBlocks), absoluteContainerSpan);
        var bodyMarkdown = bodySpan.Slice(source).Trim();
        if (string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            diagnostics.Add(new MarkdownFigureDiagnostic("Figure body is empty.", bodySpan));
        }

        return new MarkdownFigureDocument(
            source,
            effectiveContainerSpan,
            bodyMarkdown,
            bodySpan,
            leadingCaption,
            trailingCaption,
            fenceLength,
            diagnostics);
    }

    private static MarkdownFigureCaption ParseCaption(
        FigureCaption caption,
        string sourceText,
        MarkdownSourceSpan containerSpan,
        MarkdownFigureCaptionPlacement placement)
    {
        var absoluteSpan = MarkdownSourceSpan.FromMarkdig(caption.Span);
        var relativeSpan = ToRelativeSpan(absoluteSpan, containerSpan);
        var markdown = relativeSpan.Slice(containerSpan.Slice(sourceText));
        if (string.IsNullOrWhiteSpace(markdown))
        {
            markdown = caption.Lines.ToString();
        }

        return new MarkdownFigureCaption(MarkdownFigureSyntax.NormalizeInlineText(markdown), relativeSpan, placement);
    }

    private static string BuildFallbackSource(Figure figure, MarkdownParseResult parseResult, out List<MarkdownFigureDiagnostic> diagnostics)
    {
        diagnostics = [];
        MarkdownFigureCaption? leadingCaption = null;
        MarkdownFigureCaption? trailingCaption = null;
        List<Block> bodyBlocks = [];

        foreach (var child in figure)
        {
            switch (child)
            {
                case FigureCaption caption when leadingCaption is null && bodyBlocks.Count == 0:
                    leadingCaption = new MarkdownFigureCaption(
                        MarkdownFigureSyntax.NormalizeInlineText(caption.Lines.ToString()),
                        MarkdownSourceSpan.Empty,
                        MarkdownFigureCaptionPlacement.OpeningFence);
                    break;
                case FigureCaption caption when trailingCaption is null:
                    trailingCaption = new MarkdownFigureCaption(
                        MarkdownFigureSyntax.NormalizeInlineText(caption.Lines.ToString()),
                        MarkdownSourceSpan.Empty,
                        MarkdownFigureCaptionPlacement.ClosingFence);
                    break;
                case BlankLineBlock:
                    break;
                default:
                    bodyBlocks.Add(child);
                    break;
            }
        }

        var sourceText = parseResult.UsesOriginalSourceSpans ? parseResult.OriginalMarkdown : parseResult.ParsedMarkdown;
        var bodySpan = ResolveCombinedSpan(bodyBlocks);
        var bodyMarkdown = bodySpan.Slice(sourceText).Trim();
        if (string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            bodyMarkdown = string.Empty;
            diagnostics.Add(new MarkdownFigureDiagnostic("Figure source fallback could not recover body markdown.", MarkdownSourceSpan.Empty));
        }

        return MarkdownFigureSyntax.BuildFigure(
            leadingCaption?.Markdown,
            bodyMarkdown,
            trailingCaption?.Markdown,
            preferredFenceLength: figure.OpeningCharacterCount);
    }

    private static MarkdownSourceSpan ResolveCombinedSpan(IReadOnlyList<Block> blocks)
    {
        if (blocks.Count == 0)
        {
            return MarkdownSourceSpan.Empty;
        }

        var first = MarkdownSourceSpan.FromMarkdig(blocks[0].Span);
        var start = first.Start;
        var endExclusive = first.EndExclusive;
        for (var index = 1; index < blocks.Count; index++)
        {
            var span = MarkdownSourceSpan.FromMarkdig(blocks[index].Span);
            if (span.IsEmpty)
            {
                continue;
            }

            start = Math.Min(start, span.Start);
            endExclusive = Math.Max(endExclusive, span.EndExclusive);
        }

        return endExclusive <= start ? MarkdownSourceSpan.Empty : new MarkdownSourceSpan(start, endExclusive - start);
    }

    private static MarkdownSourceSpan ToRelativeSpan(MarkdownSourceSpan absoluteSpan, MarkdownSourceSpan containerSpan)
    {
        if (absoluteSpan.IsEmpty || containerSpan.IsEmpty)
        {
            return MarkdownSourceSpan.Empty;
        }

        var relativeStart = Math.Max(absoluteSpan.Start - containerSpan.Start, 0);
        var relativeEnd = Math.Min(absoluteSpan.EndExclusive - containerSpan.Start, containerSpan.Length);
        return relativeEnd <= relativeStart ? MarkdownSourceSpan.Empty : new MarkdownSourceSpan(relativeStart, relativeEnd - relativeStart);
    }
}

internal static class MarkdownFigureSyntax
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
        return NormalizeLineEndings(source).Replace('\n', ' ').Trim();
    }

    public static int ReadFenceLength(string? source)
    {
        var normalized = NormalizeLineEndings(source);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 3;
        }

        var firstLine = normalized.Split('\n', 2, StringSplitOptions.None)[0].TrimStart();
        var count = 0;
        while (count < firstLine.Length && firstLine[count] == '^')
        {
            count++;
        }

        return Math.Max(count, 3);
    }

    public static string BuildFigure(
        string? leadingCaption,
        string? bodyMarkdown,
        string? trailingCaption,
        int preferredFenceLength = 3)
    {
        var normalizedBody = NormalizeBlockText(bodyMarkdown);
        var normalizedLeadingCaption = NormalizeInlineText(leadingCaption);
        var normalizedTrailingCaption = NormalizeInlineText(trailingCaption);
        var fenceLength = ResolveFenceLength(normalizedBody, preferredFenceLength);
        var fence = new string('^', fenceLength);

        var builder = new StringBuilder();
        AppendFenceLine(builder, fence, normalizedLeadingCaption);
        builder.Append('\n');

        if (normalizedBody.Length > 0)
        {
            builder.Append(normalizedBody);
            builder.Append('\n');
        }

        AppendFenceLine(builder, fence, normalizedTrailingCaption);
        return builder.ToString();
    }

    private static void AppendFenceLine(StringBuilder builder, string fence, string caption)
    {
        builder.Append(fence);
        if (caption.Length > 0)
        {
            builder.Append(' ').Append(caption);
        }
    }

    private static int ResolveFenceLength(string normalizedBody, int preferredFenceLength)
    {
        var requiredFenceLength = Math.Max(preferredFenceLength, 3);
        if (normalizedBody.Length == 0)
        {
            return requiredFenceLength;
        }

        foreach (var line in NormalizeLineEndings(normalizedBody).Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.TrimStart();
            var caretCount = 0;
            while (caretCount < trimmed.Length && trimmed[caretCount] == '^')
            {
                caretCount++;
            }

            if (caretCount >= requiredFenceLength)
            {
                requiredFenceLength = caretCount + 1;
            }
        }

        return requiredFenceLength;
    }
}
