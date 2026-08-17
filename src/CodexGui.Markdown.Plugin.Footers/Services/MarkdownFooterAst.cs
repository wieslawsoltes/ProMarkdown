using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.Footers;

internal sealed record MarkdownFooterDiagnostic(string Message, MarkdownSourceSpan Span);

internal sealed record MarkdownFooterDraft(string BodyMarkdown);

internal sealed class MarkdownFooterDocument(
    string sourceMarkdown,
    string bodyMarkdown,
    IReadOnlyList<MarkdownFooterDiagnostic> diagnostics)
{
    public string SourceMarkdown { get; } = sourceMarkdown;

    public string BodyMarkdown { get; } = bodyMarkdown;

    public IReadOnlyList<MarkdownFooterDiagnostic> Diagnostics { get; } = diagnostics;
}
