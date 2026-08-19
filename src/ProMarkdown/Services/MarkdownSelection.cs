using System.Threading.Tasks;
using ProMarkdown.Controls;

namespace ProMarkdown.Services;

/// <summary>
/// Marks a nested Markdown control that owns its routed pointer and keyboard input.
/// </summary>
/// <remarks>
/// Document-wide selection does not intercept input originating within an input boundary.
/// </remarks>
public interface IMarkdownInputBoundary
{
}

/// <summary>
/// Provides document-wide selection operations for rendered Markdown controls.
/// </summary>
public static class MarkdownSelection
{
    /// <summary>
    /// Gets whether the rendered Markdown document contains a non-empty selection.
    /// </summary>
    public static bool CanCopy(MarkdownTextBlock? control) =>
        MarkdownDocumentSelection.CanCopy(control);

    /// <summary>
    /// Copies the selected rendered text to the target control's clipboard.
    /// </summary>
    public static Task CopyAsync(MarkdownTextBlock? control) =>
        MarkdownDocumentSelection.CopyAsync(control);

    /// <summary>
    /// Selects all rendered text in the Markdown document.
    /// </summary>
    public static void SelectAll(MarkdownTextBlock? control) =>
        MarkdownDocumentSelection.SelectAll(control);

    /// <summary>
    /// Gets the selected rendered text without accessing the clipboard.
    /// </summary>
    public static string GetSelectedText(MarkdownTextBlock? control) =>
        control is null ? string.Empty : MarkdownDocumentSelection.GetSelectedText(control);

    /// <summary>Gets all rendered document text without changing the active selection.</summary>
    public static string GetDocumentText(MarkdownTextBlock? control) =>
        control is null ? string.Empty : MarkdownDocumentSelection.GetDocumentText(control);

    /// <summary>Copies all rendered document text without changing the active selection.</summary>
    public static Task CopyDocumentTextAsync(MarkdownTextBlock? control) =>
        MarkdownDocumentSelection.CopyDocumentTextAsync(control);
}
