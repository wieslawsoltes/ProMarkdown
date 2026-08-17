using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.Alerts;

internal sealed record MarkdownAlertDiagnostic(string Message, MarkdownSourceSpan Span);

internal sealed record MarkdownAlertDraft(string Kind, string BodyMarkdown);

internal sealed class MarkdownAlertDocument(
    string sourceMarkdown,
    string kind,
    MarkdownSourceSpan sourceSpan,
    string bodyMarkdown,
    MarkdownSourceSpan bodySpan,
    IReadOnlyList<MarkdownAlertDiagnostic> diagnostics)
{
    public string SourceMarkdown { get; } = sourceMarkdown;

    public string Kind { get; } = kind;

    public MarkdownSourceSpan SourceSpan { get; } = sourceSpan;

    public string BodyMarkdown { get; } = bodyMarkdown;

    public MarkdownSourceSpan BodySpan { get; } = bodySpan;

    public IReadOnlyList<MarkdownAlertDiagnostic> Diagnostics { get; } = diagnostics;
}

internal sealed class MarkdownAlertKindOption(string value, string label, string description)
{
    public string Value { get; } = value;

    public string Label { get; } = label;

    public string Description { get; } = description;

    public override string ToString() => Label;
}
