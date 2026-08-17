using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.CustomContainers;

internal sealed record MarkdownCustomContainerDiagnostic(string Message, MarkdownSourceSpan Span);

internal sealed record MarkdownCustomContainerDraft(string Info, string Arguments, string BodyMarkdown);

internal sealed class MarkdownCustomContainerDocument(
    string sourceMarkdown,
    string info,
    string arguments,
    string bodyMarkdown,
    int fenceLength,
    IReadOnlyList<MarkdownCustomContainerDiagnostic> diagnostics)
{
    public string SourceMarkdown { get; } = sourceMarkdown;

    public string Info { get; } = info;

    public string Arguments { get; } = arguments;

    public string BodyMarkdown { get; } = bodyMarkdown;

    public int FenceLength { get; } = fenceLength;

    public IReadOnlyList<MarkdownCustomContainerDiagnostic> Diagnostics { get; } = diagnostics;
}
