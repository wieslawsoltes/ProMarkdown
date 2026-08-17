using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ProMarkdown.Controls;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;

namespace ProMarkdown.Services;

internal static class MarkdownTaskListNormalizer
{
    private const string CheckedGlyph = "☑ ";
    private const string UncheckedGlyph = "☐ ";
    private static readonly ConditionalWeakTable<CheckBox, TaskCheckBoxMetadata> CheckBoxMetadata = new();
    private static readonly MarkdownPipeline SourcePipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseTaskLists()
        .Build();

    public static void Normalize(
        InlineCollection inlines,
        MarkdownParseResult parseResult,
        MarkdownRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        var markers = ParseMarkers(parseResult);
        if (markers.Count == 0)
            return;

        var markerIndex = 0;
        for (var index = 0; index < inlines.Count && markerIndex < markers.Count; index++)
        {
            if (inlines[index] is not Run { Text: { } text } run ||
                MarkdownRenderedElementMetadata.GetElementInfo(run)?.AstNode.Node is not ListItemBlock ||
                !IsRenderedMarker(text, out var isChecked))
            {
                continue;
            }

            var marker = markers[markerIndex];
            if (marker.IsChecked != isChecked)
                continue;

            var taskText = GetTaskText(inlines, index + 1);
            var indicatorExtent = Math.Max(18, Math.Ceiling(context.FontSize * 1.4));
            var checkBox = CreateCheckBox(marker.Offset, taskText, isChecked, context, indicatorExtent);
            var host = new MarkdownTaskCheckBoxHost(
                CreateMarkerText(run, context, indicatorExtent),
                checkBox);

            var container = new InlineUIContainer(host)
            {
                BaselineAlignment = BaselineAlignment.Center
            };
            var elementInfo = MarkdownRenderedElementMetadata.GetElementInfo(run);
            if (elementInfo is not null)
            {
                MarkdownRenderedElementMetadata.SetElementInfo(container, elementInfo);
                MarkdownRenderedElementMetadata.SetElementInfo(host, elementInfo);
            }

            inlines[index] = container;
            markerIndex++;
        }
    }

    public static IEnumerable<CheckBox> EnumerateCheckBoxes(InlineCollection? inlines)
    {
        if (inlines is null)
            yield break;

        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var checkBox in EnumerateCheckBoxes(span.Inlines))
                    yield return checkBox;
            }

            if (inline is not InlineUIContainer { Child: { } child })
                continue;

            foreach (var checkBox in EnumerateCheckBoxes(child))
                yield return checkBox;
        }
    }

    public static bool IsInteractiveTaskControl(object? source) =>
        source is Visual visual && visual.GetSelfAndVisualAncestors()
            .OfType<CheckBox>()
            .Any(IsInteractive);

    public static bool TryCreateToggleRequest(
        string sourceMarkdown,
        CheckBox checkBox,
        out MarkdownTaskListToggleRequest request,
        bool? requestedIsChecked = null)
    {
        if (!CheckBoxMetadata.TryGetValue(checkBox, out var metadata))
        {
            request = null!;
            return false;
        }

        var markerOffset = metadata.MarkerOffset;
        var isChecked = requestedIsChecked ?? checkBox.IsChecked == true;
        if ((uint)markerOffset >= (uint)sourceMarkdown.Length ||
            markerOffset == 0 ||
            markerOffset + 1 >= sourceMarkdown.Length ||
            sourceMarkdown[markerOffset - 1] != '[' ||
            sourceMarkdown[markerOffset + 1] != ']' ||
            sourceMarkdown[markerOffset] is not (' ' or 'x' or 'X'))
        {
            request = null!;
            return false;
        }

        var updatedMarkdown = string.Concat(
            sourceMarkdown.AsSpan(0, markerOffset),
            isChecked ? "x" : " ",
            sourceMarkdown.AsSpan(markerOffset + 1));
        request = new MarkdownTaskListToggleRequest(
            sourceMarkdown,
            updatedMarkdown,
            markerOffset,
            isChecked,
            metadata.TaskText);
        return true;
    }

    public static bool IsInteractive(CheckBox checkBox) =>
        CheckBoxMetadata.TryGetValue(checkBox, out var metadata) && metadata.IsInteractive;

    public static void SetInteractive(CheckBox checkBox, bool value)
    {
        if (CheckBoxMetadata.TryGetValue(checkBox, out var metadata))
            metadata.IsInteractive = value;
    }

    private static IReadOnlyList<TaskMarker> ParseMarkers(MarkdownParseResult parseResult)
    {
        var markdown = parseResult.OriginalMarkdown;
        if (string.IsNullOrEmpty(markdown))
            return [];

        var document = parseResult.UsesOriginalSourceSpans
            ? parseResult.Document
            : Markdig.Markdown.Parse(markdown, SourcePipeline);
        var markers = new List<TaskMarker>();
        foreach (var task in document.Descendants().OfType<TaskList>())
        {
            var offset = FindMarkerOffset(markdown, task);
            if (offset >= 0)
                markers.Add(new TaskMarker(offset, task.Checked));
        }

        return markers;
    }

    private static int FindMarkerOffset(string markdown, TaskList task)
    {
        var start = Math.Max(0, task.Span.Start - 1);
        var end = Math.Min(markdown.Length - 3, task.Span.End + 1);
        var offset = FindMarkerOffset(markdown, start, end, task.Checked);
        if (offset >= 0)
            return offset;

        start = 0;
        for (var line = 0; line < task.Line && start < markdown.Length; line++)
        {
            var newline = markdown.IndexOf('\n', start);
            if (newline < 0)
                return -1;

            start = newline + 1;
        }

        end = markdown.IndexOf('\n', start);
        if (end < 0)
            end = markdown.Length;

        return FindMarkerOffset(markdown, start, Math.Max(start, end - 1), task.Checked);
    }

    private static int FindMarkerOffset(
        string markdown,
        int start,
        int end,
        bool isChecked)
    {
        end = Math.Min(markdown.Length - 3, end);
        for (var index = start; index <= end; index++)
        {
            if (markdown[index] == '[' &&
                markdown[index + 2] == ']' &&
                (isChecked
                    ? markdown[index + 1] is 'x' or 'X'
                    : markdown[index + 1] == ' '))
            {
                return index + 1;
            }
        }

        return -1;
    }

    private static CheckBox CreateCheckBox(
        int markerOffset,
        string taskText,
        bool isChecked,
        MarkdownRenderContext context,
        double indicatorExtent)
    {
        var checkBox = new CheckBox
        {
            IsChecked = isChecked,
            FontSize = context.FontSize,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            MinHeight = indicatorExtent,
            MinWidth = indicatorExtent,
            VerticalAlignment = VerticalAlignment.Center
        };
        CheckBoxMetadata.Add(checkBox, new TaskCheckBoxMetadata(markerOffset, taskText));
        AutomationProperties.SetName(checkBox, taskText);
        checkBox.AttachedToVisualTree += OnCheckBoxAttachedToVisualTree;
        context.ResourceTracker.Track(new TaskCheckBoxCleanup(checkBox));
        return checkBox;
    }

    private static SelectableTextBlock CreateMarkerText(
        Run run,
        MarkdownRenderContext context,
        double indicatorExtent)
    {
        var marker = new SelectableTextBlock
        {
            Background = Brushes.Transparent,
            ClipToBounds = true,
            FontFamily = context.FontFamily,
            FontSize = context.FontSize,
            FontStyle = run.FontStyle,
            FontWeight = run.FontWeight,
            Foreground = Brushes.Transparent,
            IsHitTestVisible = true,
            SelectionForegroundBrush = Brushes.Transparent,
            Text = run.Text,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            Width = indicatorExtent
        };
        MarkdownDocumentSelection.RegisterSegment(marker);
        return marker;
    }

    private static bool IsRenderedMarker(string text, out bool isChecked)
    {
        if (string.Equals(text, CheckedGlyph, StringComparison.Ordinal))
        {
            isChecked = true;
            return true;
        }

        isChecked = false;
        return string.Equals(text, UncheckedGlyph, StringComparison.Ordinal);
    }

    private static string GetTaskText(InlineCollection inlines, int startIndex)
    {
        var parts = new List<string>();
        for (var index = startIndex; index < inlines.Count && inlines[index] is not LineBreak; index++)
            AppendText(inlines[index], parts);

        return string.Concat(parts).Trim();
    }

    private static void AppendText(Inline inline, List<string> parts)
    {
        if (inline is Run { Text: { } text })
        {
            parts.Add(text);
            return;
        }

        if (inline is not Span span)
            return;

        foreach (var child in span.Inlines)
            AppendText(child, parts);
    }

    private static IEnumerable<CheckBox> EnumerateCheckBoxes(Control control)
    {
        // A nested Markdown document owns and configures its own task-list controls.
        // The caller starts from the owning document's inlines, so stopping here
        // does not hide check boxes that belong to that caller.
        if (control is MarkdownTextBlock)
            yield break;

        if (control is CheckBox checkBox && CheckBoxMetadata.TryGetValue(checkBox, out _))
            yield return checkBox;

        if (control is TextBlock { Inlines: { } inlines })
        {
            foreach (var nested in EnumerateCheckBoxes(inlines))
                yield return nested;
        }

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    foreach (var nested in EnumerateCheckBoxes(child))
                        yield return nested;
                break;
            case Decorator { Child: { } child }:
                foreach (var nested in EnumerateCheckBoxes(child))
                    yield return nested;
                break;
            case ContentControl { Content: Control child }:
                foreach (var nested in EnumerateCheckBoxes(child))
                    yield return nested;
                break;
        }
    }

    private static void OnCheckBoxAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is CheckBox checkBox &&
            checkBox.FindAncestorOfType<MarkdownTextBlock>() is { } owner)
        {
            owner.ConfigureTaskListCheckBox(checkBox);
        }
    }

    private readonly record struct TaskMarker(int Offset, bool IsChecked);

    private sealed class TaskCheckBoxMetadata(int markerOffset, string taskText)
    {
        public int MarkerOffset { get; } = markerOffset;

        public string TaskText { get; } = taskText;

        public bool IsInteractive { get; set; }
    }

    private sealed class TaskCheckBoxCleanup(CheckBox checkBox) : IDisposable
    {
        private CheckBox? _checkBox = checkBox;

        public void Dispose()
        {
            if (_checkBox is not { } current)
                return;

            _checkBox = null;
            current.AttachedToVisualTree -= OnCheckBoxAttachedToVisualTree;
            current.Command = null;
            current.CommandParameter = null;
        }
    }
}

internal sealed class MarkdownTaskCheckBoxHost : Grid
{
    public MarkdownTaskCheckBoxHost(SelectableTextBlock marker, CheckBox checkBox)
    {
        Background = Brushes.Transparent;
        Children.Add(marker);
        Children.Add(checkBox);
    }
}
