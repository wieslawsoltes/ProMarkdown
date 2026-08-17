using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.Figures;

internal sealed record MarkdownFigureDiagnostic(string Message, MarkdownSourceSpan Span);

internal enum MarkdownFigureCaptionPlacement
{
    OpeningFence,
    ClosingFence
}

internal sealed record MarkdownFigureCaption(string Markdown, MarkdownSourceSpan Span, MarkdownFigureCaptionPlacement Placement);

internal sealed record MarkdownFigureDraft(string LeadingCaptionMarkdown, string BodyMarkdown, string TrailingCaptionMarkdown);

internal sealed class MarkdownFigureDocument(
    string sourceMarkdown,
    MarkdownSourceSpan sourceSpan,
    string bodyMarkdown,
    MarkdownSourceSpan bodySpan,
    MarkdownFigureCaption? leadingCaption,
    MarkdownFigureCaption? trailingCaption,
    int fenceLength,
    IReadOnlyList<MarkdownFigureDiagnostic> diagnostics)
{
    public string SourceMarkdown { get; } = sourceMarkdown;

    public MarkdownSourceSpan SourceSpan { get; } = sourceSpan;

    public string BodyMarkdown { get; } = bodyMarkdown;

    public MarkdownSourceSpan BodySpan { get; } = bodySpan;

    public MarkdownFigureCaption? LeadingCaption { get; } = leadingCaption;

    public MarkdownFigureCaption? TrailingCaption { get; } = trailingCaption;

    public int FenceLength { get; } = fenceLength;

    public IReadOnlyList<MarkdownFigureDiagnostic> Diagnostics { get; } = diagnostics;

    public bool IsEmpty => string.IsNullOrWhiteSpace(BodyMarkdown) &&
                           string.IsNullOrWhiteSpace(LeadingCaption?.Markdown) &&
                           string.IsNullOrWhiteSpace(TrailingCaption?.Markdown);
}
