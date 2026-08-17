using CodexGui.Markdown.Services;

namespace CodexGui.Markdown.Plugin.DefinitionLists;

internal sealed record MarkdownDefinitionListDiagnostic(string Message, MarkdownSourceSpan Span);

internal sealed record MarkdownDefinitionTerm(
    string Markdown,
    string PlainText,
    MarkdownSourceSpan Span);

internal sealed record MarkdownDefinitionEntry(
    IReadOnlyList<MarkdownDefinitionTerm> Terms,
    string DefinitionMarkdown,
    MarkdownSourceSpan Span);

internal sealed class MarkdownDefinitionListDocument(
    string source,
    IReadOnlyList<MarkdownDefinitionEntry> entries,
    IReadOnlyList<MarkdownDefinitionListDiagnostic> diagnostics)
{
    public string Source { get; } = source;

    public IReadOnlyList<MarkdownDefinitionEntry> Entries { get; } = entries;

    public IReadOnlyList<MarkdownDefinitionListDiagnostic> Diagnostics { get; } = diagnostics;

    public bool HasDiagnostics => Diagnostics.Count > 0;

    public bool IsEmpty => Entries.Count == 0;
}

internal sealed record MarkdownDefinitionDraftEntry(
    string TermsText,
    string DefinitionMarkdown);
