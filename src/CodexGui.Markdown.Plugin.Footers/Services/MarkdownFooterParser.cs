using System.Text;
using CodexGui.Markdown.Services;
using Markdig;
using Markdig.Extensions.Footers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Footers;

internal static class MarkdownFooterParser
{
    private static readonly Lazy<MarkdownPipeline> Pipeline = new(() => new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build());

    public static MarkdownFooterDocument Parse(FooterBlock footerBlock, MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(footerBlock);
        ArgumentNullException.ThrowIfNull(parseResult);

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(footerBlock.Span);
        var source = sourceSpan.Slice(parseResult.OriginalMarkdown);
        if (string.IsNullOrWhiteSpace(source) && !parseResult.UsesOriginalSourceSpans)
        {
            source = sourceSpan.Slice(parseResult.ParsedMarkdown);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            var fallbackBody = ResolveCombinedSpan(footerBlock)
                .Slice(parseResult.UsesOriginalSourceSpans ? parseResult.OriginalMarkdown : parseResult.ParsedMarkdown);
            source = MarkdownFooterSyntax.BuildFooter(fallbackBody);
        }

        return Parse(source);
    }

    public static MarkdownFooterDocument Parse(string? markdown)
    {
        var source = MarkdownFooterSyntax.NormalizeBlockText(markdown);
        List<MarkdownFooterDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(new MarkdownFooterDiagnostic("Footer content is empty.", MarkdownSourceSpan.Empty));
            return new MarkdownFooterDocument(string.Empty, string.Empty, diagnostics);
        }

        var document = Markdig.Markdown.Parse(source, Pipeline.Value);
        FooterBlock? footerBlock = null;
        foreach (var block in document)
        {
            switch (block)
            {
                case FooterBlock parsedFooter when footerBlock is null:
                    footerBlock = parsedFooter;
                    break;
                case BlankLineBlock:
                    break;
                default:
                    diagnostics.Add(new MarkdownFooterDiagnostic(
                        $"Unexpected block '{block.GetType().Name}' inside footer source.",
                        MarkdownSourceSpan.FromMarkdig(block.Span)));
                    break;
            }
        }

        if (footerBlock is null)
        {
            diagnostics.Add(new MarkdownFooterDiagnostic(
                "Markdig did not produce a footer block from the provided markdown.",
                MarkdownSourceSpan.Empty));
            return new MarkdownFooterDocument(source, string.Empty, diagnostics);
        }

        var bodyMarkdown = MarkdownFooterSyntax.ExtractBodyMarkdown(source, diagnostics);
        if (string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            diagnostics.Add(new MarkdownFooterDiagnostic("Footer body is empty.", new MarkdownSourceSpan(0, source.Length)));
        }

        return new MarkdownFooterDocument(source, bodyMarkdown, diagnostics);
    }

    private static MarkdownSourceSpan ResolveCombinedSpan(FooterBlock footerBlock)
    {
        var blocks = footerBlock
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

internal static class MarkdownFooterSyntax
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

    public static string ExtractBodyMarkdown(string source, List<MarkdownFooterDiagnostic> diagnostics)
    {
        var lines = NormalizeLineEndings(source).Split('\n', StringSplitOptions.None);
        var builder = new StringBuilder();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("^^", StringComparison.Ordinal))
            {
                diagnostics.Add(new MarkdownFooterDiagnostic(
                    "Footer lines should start with the `^^` prefix.",
                    MarkdownSourceSpan.Empty));
                continue;
            }

            var remainder = trimmed.Length > 2 ? trimmed[2..] : string.Empty;
            if (remainder.StartsWith(' ') || remainder.StartsWith('\t'))
            {
                remainder = remainder[1..];
            }

            builder.Append(remainder);
            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return NormalizeBlockText(builder.ToString());
    }

    public static string BuildFooter(string? bodyMarkdown)
    {
        var normalizedBody = NormalizeBlockText(bodyMarkdown);
        if (normalizedBody.Length == 0)
        {
            return "^^ Footer text.";
        }

        var lines = NormalizeLineEndings(normalizedBody).Split('\n', StringSplitOptions.None);
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            builder.Append("^^");
            if (line.Length > 0)
            {
                builder.Append(' ').Append(line);
            }

            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }
}
