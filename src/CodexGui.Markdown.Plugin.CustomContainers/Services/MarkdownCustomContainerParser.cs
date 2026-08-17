using System.Text;
using CodexGui.Markdown.Services;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.CustomContainers;

internal static class MarkdownCustomContainerParser
{
    private static readonly Lazy<MarkdownPipeline> Pipeline = new(() => new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build());

    public static MarkdownCustomContainerDocument Parse(CustomContainer customContainer, MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(customContainer);
        ArgumentNullException.ThrowIfNull(parseResult);

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(customContainer.Span);
        var source = sourceSpan.Slice(parseResult.OriginalMarkdown);
        if (string.IsNullOrWhiteSpace(source) && !parseResult.UsesOriginalSourceSpans)
        {
            source = sourceSpan.Slice(parseResult.ParsedMarkdown);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            var fallbackBody = ResolveCombinedSpan(customContainer)
                .Slice(parseResult.UsesOriginalSourceSpans ? parseResult.OriginalMarkdown : parseResult.ParsedMarkdown);
            source = MarkdownCustomContainerSyntax.BuildCustomContainer(
                customContainer.Info,
                customContainer.Arguments,
                fallbackBody,
                customContainer.OpeningFencedCharCount);
        }

        return Parse(source);
    }

    public static MarkdownCustomContainerDocument Parse(string? markdown)
    {
        var source = MarkdownCustomContainerSyntax.NormalizeBlockText(markdown);
        List<MarkdownCustomContainerDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(new MarkdownCustomContainerDiagnostic("Custom container content is empty.", MarkdownSourceSpan.Empty));
            return new MarkdownCustomContainerDocument(string.Empty, string.Empty, string.Empty, string.Empty, 3, diagnostics);
        }

        var document = Markdig.Markdown.Parse(source, Pipeline.Value);
        CustomContainer? customContainer = null;

        foreach (var block in document)
        {
            switch (block)
            {
                case CustomContainer parsedContainer when customContainer is null:
                    customContainer = parsedContainer;
                    break;
                case BlankLineBlock:
                    break;
                default:
                    diagnostics.Add(new MarkdownCustomContainerDiagnostic(
                        $"Unexpected block '{block.GetType().Name}' inside custom-container source.",
                        MarkdownSourceSpan.FromMarkdig(block.Span)));
                    break;
            }
        }

        if (customContainer is null)
        {
            diagnostics.Add(new MarkdownCustomContainerDiagnostic(
                "Markdig did not produce a custom container from the provided markdown.",
                MarkdownSourceSpan.Empty));
            return new MarkdownCustomContainerDocument(
                source,
                string.Empty,
                string.Empty,
                string.Empty,
                MarkdownCustomContainerSyntax.ReadFenceLength(source),
                diagnostics);
        }

        var bodyMarkdown = MarkdownCustomContainerSyntax.ExtractBodyMarkdown(source);
        if (string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            diagnostics.Add(new MarkdownCustomContainerDiagnostic("Custom container body is empty.", new MarkdownSourceSpan(0, source.Length)));
        }

        return new MarkdownCustomContainerDocument(
            source,
            MarkdownCustomContainerSyntax.NormalizeInfo(customContainer.Info),
            MarkdownCustomContainerSyntax.NormalizeArguments(customContainer.Arguments),
            bodyMarkdown,
            Math.Max(customContainer.OpeningFencedCharCount, MarkdownCustomContainerSyntax.ReadFenceLength(source)),
            diagnostics);
    }

    private static MarkdownSourceSpan ResolveCombinedSpan(CustomContainer customContainer)
    {
        var blocks = customContainer
            .OfType<Block>()
            .Where(static block => block is not BlankLineBlock)
            .ToArray();
        if (blocks.Length == 0)
        {
            return MarkdownSourceSpan.Empty;
        }

        var first = MarkdownSourceSpan.FromMarkdig(blocks[0].Span);
        var start = first.Start;
        var endExclusive = first.EndExclusive;
        for (var index = 1; index < blocks.Length; index++)
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
}

internal static class MarkdownCustomContainerSyntax
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

    public static string NormalizeInfo(string? info)
    {
        return NormalizeInlineText(info).ToLowerInvariant();
    }

    public static string NormalizeArguments(string? arguments)
    {
        return NormalizeInlineText(arguments);
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
        while (count < firstLine.Length && firstLine[count] == ':')
        {
            count++;
        }

        return Math.Max(count, 3);
    }

    public static string ExtractBodyMarkdown(string source)
    {
        var normalized = NormalizeLineEndings(source);
        var firstNewLine = normalized.IndexOf('\n');
        var lastNewLine = normalized.LastIndexOf('\n');
        if (firstNewLine < 0 || lastNewLine <= firstNewLine)
        {
            return string.Empty;
        }

        var body = normalized.Substring(firstNewLine + 1, lastNewLine - firstNewLine - 1);
        return NormalizeBlockText(body);
    }

    public static string BuildCustomContainer(
        string? info,
        string? arguments,
        string? bodyMarkdown,
        int preferredFenceLength = 3)
    {
        var normalizedInfo = NormalizeInfo(info);
        var normalizedArguments = normalizedInfo.Length == 0 ? string.Empty : NormalizeArguments(arguments);
        var normalizedBody = NormalizeBlockText(bodyMarkdown);
        var fenceLength = ResolveFenceLength(normalizedBody, preferredFenceLength);
        var fence = new string(':', fenceLength);

        var builder = new StringBuilder();
        builder.Append(fence);
        if (normalizedInfo.Length > 0)
        {
            builder.Append(normalizedInfo);
            if (normalizedArguments.Length > 0)
            {
                builder.Append(' ').Append(normalizedArguments);
            }
        }
        builder.Append('\n');

        if (normalizedBody.Length > 0)
        {
            builder.Append(normalizedBody);
            builder.Append('\n');
        }

        builder.Append(fence);
        return builder.ToString();
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
            var colonCount = 0;
            while (colonCount < trimmed.Length && trimmed[colonCount] == ':')
            {
                colonCount++;
            }

            if (colonCount >= requiredFenceLength)
            {
                requiredFenceLength = colonCount + 1;
            }
        }

        return requiredFenceLength;
    }
}
