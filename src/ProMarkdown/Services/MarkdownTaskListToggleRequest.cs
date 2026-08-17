using System;

namespace ProMarkdown.Services;

/// <summary>
/// Describes an interactive Markdown task-list checkbox change.
/// </summary>
public sealed class MarkdownTaskListToggleRequest
{
    /// <summary>
    /// Initializes a new task-list checkbox change request.
    /// </summary>
    /// <param name="sourceMarkdown">The Markdown source before the checkbox changed.</param>
    /// <param name="updatedMarkdown">The Markdown source with the requested checkbox state applied.</param>
    /// <param name="markerOffset">The zero-based source offset of the character between the marker brackets.</param>
    /// <param name="isChecked">The requested checked state.</param>
    /// <param name="taskText">The rendered task text, without the checkbox marker.</param>
    public MarkdownTaskListToggleRequest(
        string sourceMarkdown,
        string updatedMarkdown,
        int markerOffset,
        bool isChecked,
        string taskText)
    {
        ArgumentNullException.ThrowIfNull(sourceMarkdown);
        ArgumentNullException.ThrowIfNull(updatedMarkdown);
        ArgumentNullException.ThrowIfNull(taskText);

        if ((uint)markerOffset >= (uint)sourceMarkdown.Length)
            throw new ArgumentOutOfRangeException(nameof(markerOffset));

        SourceMarkdown = sourceMarkdown;
        UpdatedMarkdown = updatedMarkdown;
        MarkerOffset = markerOffset;
        IsChecked = isChecked;
        TaskText = taskText;
    }

    /// <summary>
    /// Gets the Markdown source before the checkbox changed.
    /// </summary>
    public string SourceMarkdown { get; }

    /// <summary>
    /// Gets the Markdown source with the requested checkbox state applied.
    /// </summary>
    public string UpdatedMarkdown { get; }

    /// <summary>
    /// Gets the zero-based source offset of the character between the task marker brackets.
    /// </summary>
    public int MarkerOffset { get; }

    /// <summary>
    /// Gets the requested checked state.
    /// </summary>
    public bool IsChecked { get; }

    /// <summary>
    /// Gets the rendered task text, without the checkbox marker.
    /// </summary>
    public string TaskText { get; }
}
