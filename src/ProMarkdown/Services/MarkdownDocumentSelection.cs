using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProMarkdown.Controls;

namespace ProMarkdown.Services;

internal sealed class MarkdownDocumentSelection
{
    private static readonly ConditionalWeakTable<MarkdownTextBlock, SelectionState> States = new();
    private static readonly ConditionalWeakTable<SelectableTextBlock, SegmentRegistration> SegmentRegistrations = new();

    private MarkdownDocumentSelection()
    {
    }

    static MarkdownDocumentSelection()
    {
        SelectableTextBlock.SelectionBrushProperty.Changed.AddClassHandler<MarkdownTextBlock>(
            static (control, _) => SynchronizeSelectionBrushes(control));
        SelectableTextBlock.SelectionForegroundBrushProperty.Changed.AddClassHandler<MarkdownTextBlock>(
            static (control, _) => SynchronizeSelectionBrushes(control));
    }

    public static void SetEnabled(MarkdownTextBlock control, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (enabled)
        {
            States.GetValue(control, static owner => new SelectionState(owner));
            return;
        }

        if (States.TryGetValue(control, out var current))
        {
            current.Dispose();
            States.Remove(control);
        }
    }

    public static void Reset(MarkdownTextBlock control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (States.TryGetValue(control, out var state))
            state.Reset();
    }

    public static void InvalidateLayout(MarkdownTextBlock control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (States.TryGetValue(control, out var state))
            state.InvalidateLayout();
    }

    private static void SynchronizeSelectionBrushes(MarkdownTextBlock control)
    {
        if (States.TryGetValue(control, out var state))
            state.SynchronizeSelectionBrushes();
    }

    public static bool CanCopy(MarkdownTextBlock? control) =>
        control is not null && States.TryGetValue(control, out var state) && state.CanCopy;

    public static async Task CopyAsync(MarkdownTextBlock? control)
    {
        if (control is null || !States.TryGetValue(control, out var state))
            return;

        var text = state.GetSelectedText();
        if (!string.IsNullOrEmpty(text) && TopLevel.GetTopLevel(control)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public static async Task CopyDocumentTextAsync(MarkdownTextBlock? control)
    {
        if (control is null || !States.TryGetValue(control, out var state))
            return;

        var text = state.GetDocumentText();
        if (!string.IsNullOrEmpty(text) && TopLevel.GetTopLevel(control)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public static void SelectAll(MarkdownTextBlock? control)
    {
        if (control is not null && States.TryGetValue(control, out var state))
            state.SelectAll();
    }

    internal static void SelectRange(
        MarkdownTextBlock control,
        SelectableTextBlock anchorControl,
        int anchorOffset,
        SelectableTextBlock focusControl,
        int focusOffset)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(anchorControl);
        ArgumentNullException.ThrowIfNull(focusControl);
        if (States.TryGetValue(control, out var state))
            state.SelectRange(anchorControl, anchorOffset, focusControl, focusOffset);
    }

    internal static IReadOnlyList<SelectableTextBlock> GetSegmentControls(MarkdownTextBlock control) =>
        States.TryGetValue(control, out var state) ? state.GetSegmentControls() : [];

    internal static int GetSegmentSnapshotVersion(MarkdownTextBlock control) =>
        States.TryGetValue(control, out var state) ? state.SegmentSnapshotVersion : 0;

    internal static void SelectWord(MarkdownTextBlock control, SelectableTextBlock segment, int offset)
    {
        if (States.TryGetValue(control, out var state))
            state.SelectWord(segment, offset);
    }

    internal static void SelectBlock(MarkdownTextBlock control, SelectableTextBlock segment)
    {
        if (States.TryGetValue(control, out var state))
            state.SelectBlock(segment);
    }

    internal static string GetSelectedText(MarkdownTextBlock control) =>
        States.TryGetValue(control, out var state) ? state.GetSelectedText() : string.Empty;

    internal static string GetDocumentText(MarkdownTextBlock control) =>
        States.TryGetValue(control, out var state) ? state.GetDocumentText() : string.Empty;

    internal static bool HasDocumentText(MarkdownTextBlock control) =>
        States.TryGetValue(control, out var state) && state.HasDocumentText();

    internal static bool TryGetSegmentBounds(
        MarkdownTextBlock control,
        SelectableTextBlock segment,
        out Rect bounds)
    {
        if (States.TryGetValue(control, out var state))
            return state.TryGetSegmentBounds(segment, out bounds);

        bounds = default;
        return false;
    }

    internal static bool TryHitTestSegment(
        MarkdownTextBlock control,
        Point point,
        out SelectableTextBlock? segment,
        out Point localPoint,
        out Rect segmentBounds)
    {
        if (States.TryGetValue(control, out var state))
            return state.TryHitTestSegment(point, out segment, out localPoint, out segmentBounds);

        segment = null;
        localPoint = default;
        segmentBounds = default;
        return false;
    }

    internal static bool ShouldBypassDocumentInput(MarkdownTextBlock owner, object? source)
    {
        if (owner.ActiveEditorSession is not null || MarkdownTaskListNormalizer.IsInteractiveTaskControl(source))
            return true;

        for (var current = source as Visual;
             current is not null && !ReferenceEquals(current, owner);
             current = current.GetVisualParent())
        {
            if (current is IMarkdownInputBoundary or Button or TextBox ||
                current is Control { Focusable: true } and not SelectableTextBlock)
                return true;
        }

        return false;
    }

    internal static void RegisterSegment(
        SelectableTextBlock control,
        object? flowGroup = null,
        int lineBreaksBefore = 0)
    {
        ArgumentNullException.ThrowIfNull(control);
        var registration = SegmentRegistrations.GetValue(control, static segment =>
        {
            segment.AddHandler(
                InputElement.PointerPressedEvent,
                OnSegmentPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            segment.AddHandler(
                InputElement.PointerMovedEvent,
                OnSegmentPointerMoved,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            segment.AddHandler(
                InputElement.PointerReleasedEvent,
                OnSegmentPointerReleased,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            return new SegmentRegistration();
        });
        registration.FlowGroup = flowGroup ?? registration.FlowGroup;
        if (lineBreaksBefore > 0)
            registration.LineBreaksBefore = lineBreaksBefore;
    }

    internal static void RegisterTableCellSegments(
        Control control,
        object table,
        int row,
        int column)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(table);
        RegisterTableCellSegments(control, new TableCellPosition(table, row, column));
    }

    private static void RegisterTableCellSegments(Control control, TableCellPosition position)
    {
        if (control is SelectableTextBlock selectable and not MarkdownTextBlock)
        {
            RegisterSegment(selectable);
            var registration = SegmentRegistrations.GetValue(selectable, static _ => new SegmentRegistration());
            registration.TableCell = position;
        }

        if (control is TextBlock { Inlines: { } inlines })
            RegisterTableCellSegments(inlines, position);

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    RegisterTableCellSegments(child, position);
                break;
            case Decorator { Child: { } child }:
                RegisterTableCellSegments(child, position);
                break;
            case ContentControl { Content: Control child }:
                RegisterTableCellSegments(child, position);
                break;
        }
    }

    private static void RegisterTableCellSegments(InlineCollection inlines, TableCellPosition position)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
                RegisterTableCellSegments(span.Inlines, position);
            if (inline is InlineUIContainer { Child: { } child })
                RegisterTableCellSegments(child, position);
        }
    }

    internal static bool RegisterLineBreaksBeforeFirstSegment(Control control, int lineBreaksBefore)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (lineBreaksBefore <= 0)
            return false;

        if (control is SelectableTextBlock selectable and not MarkdownTextBlock)
        {
            RegisterSegment(selectable, lineBreaksBefore: lineBreaksBefore);
            return true;
        }

        if (control is TextBlock { Inlines: { } inlines } &&
            RegisterLineBreaksBeforeFirstSegment(inlines, lineBreaksBefore))
        {
            return true;
        }

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                {
                    if (RegisterLineBreaksBeforeFirstSegment(child, lineBreaksBefore))
                        return true;
                }

                break;
            case Decorator { Child: { } child }:
                return RegisterLineBreaksBeforeFirstSegment(child, lineBreaksBefore);
            case ContentControl { Content: Control child }:
                return RegisterLineBreaksBeforeFirstSegment(child, lineBreaksBefore);
        }

        return false;
    }

    internal static void RegisterFlowGroupForUnassignedSegments(Control control, object flowGroup)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(flowGroup);

        if (control is MarkdownTextBlock)
            return;

        if (control is SelectableTextBlock selectable)
        {
            RegisterSegment(selectable);
            var registration = SegmentRegistrations.GetValue(selectable, static _ => new SegmentRegistration());
            registration.FlowGroup ??= flowGroup;
        }

        if (control is TextBlock { Inlines: { } inlines })
            RegisterFlowGroupForUnassignedSegments(inlines, flowGroup);

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    RegisterFlowGroupForUnassignedSegments(child, flowGroup);
                break;
            case Decorator { Child: { } child }:
                RegisterFlowGroupForUnassignedSegments(child, flowGroup);
                break;
            case ContentControl { Content: Control child }:
                RegisterFlowGroupForUnassignedSegments(child, flowGroup);
                break;
        }
    }

    private static void RegisterFlowGroupForUnassignedSegments(
        InlineCollection inlines,
        object flowGroup)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
                RegisterFlowGroupForUnassignedSegments(span.Inlines, flowGroup);
            if (inline is InlineUIContainer { Child: { } child })
                RegisterFlowGroupForUnassignedSegments(child, flowGroup);
        }
    }

    private static bool RegisterLineBreaksBeforeFirstSegment(
        InlineCollection inlines,
        int lineBreaksBefore)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span &&
                RegisterLineBreaksBeforeFirstSegment(span.Inlines, lineBreaksBefore))
            {
                return true;
            }

            if (inline is InlineUIContainer { Child: { } child } &&
                RegisterLineBreaksBeforeFirstSegment(child, lineBreaksBefore))
            {
                return true;
            }
        }

        return false;
    }

    private static void OnSegmentPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (TryGetSegmentState(sender, out var state))
            state.OnPointerPressed(sender, args);
    }

    private static void OnSegmentPointerMoved(object? sender, PointerEventArgs args)
    {
        if (TryGetSegmentState(sender, out var state))
            state.OnPointerMoved(sender, args);
    }

    private static void OnSegmentPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (TryGetSegmentState(sender, out var state))
            state.OnPointerReleased(sender, args);
    }

    private static bool TryGetSegmentState(object? sender, out SelectionState state)
    {
        var owner = FindDocumentOwner(sender);
        if (owner is not null && States.TryGetValue(owner, out var current))
        {
            state = current;
            return true;
        }

        state = null!;
        return false;
    }

    private static MarkdownTextBlock? FindDocumentOwner(object? source)
    {
        MarkdownTextBlock? owner = null;
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is MarkdownTextBlock candidate && States.TryGetValue(candidate, out _))
                owner = candidate;
        }

        return owner;
    }

    private sealed class SelectionState : IDisposable
    {
        private const double DragThreshold = 3;
        private const double AutoScrollEdge = 32;
        private const double AutoScrollMinimumStep = 2;
        private const double AutoScrollMaximumStep = 18;
        private static readonly TimeSpan AutoScrollInterval = TimeSpan.FromMilliseconds(16);
        private readonly MarkdownTextBlock _owner;
        private RoutedEventArgs? _lastPointerEvent;
        private DispatcherTimer? _autoScrollTimer;
        private ScrollViewer? _autoScrollViewer;
        private Point _autoScrollViewportPosition;
        private double _autoScrollStep;
        private PressState? _press;
        private bool _isDragging;
        private IBrush? _synchronizedSelectionBrush;
        private IBrush? _synchronizedSelectionForegroundBrush;
        private readonly HashSet<AvaloniaObject> _observedObjects = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<INotifyCollectionChanged> _observedCollections = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Visual, Transform> _observedTransforms = new(ReferenceEqualityComparer.Instance);
        private readonly List<SelectableTextBlock> _selectedControls = [];
        private IReadOnlyList<Segment>? _segments;
        private SelectionPoint? _lastSelectionStart;
        private SelectionPoint? _lastSelectionEnd;
        private SelectableTextBlock? _lastSelectionStartControl;
        private SelectableTextBlock? _lastSelectionEndControl;
        private int _segmentSnapshotVersion;
        private bool _layoutUpdatePending;
        private bool _topologyInvalidated = true;

        public SelectionState(MarkdownTextBlock owner)
        {
            _owner = owner;
            _owner.AddHandler(
                InputElement.PointerPressedEvent,
                OnPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _owner.AddHandler(
                InputElement.PointerMovedEvent,
                OnPointerMoved,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _owner.AddHandler(
                InputElement.PointerReleasedEvent,
                OnPointerReleased,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _owner.AddHandler(
                InputElement.PointerCaptureLostEvent,
                OnPointerCaptureLost,
                RoutingStrategies.Direct,
                handledEventsToo: true);
            _owner.AddHandler(
                InputElement.KeyDownEvent,
                OnKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _owner.AddHandler(
                SelectableTextBlock.CopyingToClipboardEvent,
                OnCopyingToClipboard,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
            _owner.LayoutUpdated += OnLayoutUpdated;
            _owner.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        public bool CanCopy => GetSegments().Any(segment => segment.Control.SelectionStart != segment.Control.SelectionEnd);

        public int SegmentSnapshotVersion => _segmentSnapshotVersion;

        public IReadOnlyList<SelectableTextBlock> GetSegmentControls() =>
            GetSegments().Select(segment => segment.Control).ToArray();

        public void Dispose()
        {
            _owner.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            _owner.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            _owner.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            _owner.RemoveHandler(InputElement.PointerCaptureLostEvent, OnPointerCaptureLost);
            _owner.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _owner.RemoveHandler(SelectableTextBlock.CopyingToClipboardEvent, OnCopyingToClipboard);
            _owner.LayoutUpdated -= OnLayoutUpdated;
            _owner.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            DisposeAutoScrollTimer();
            if (_press?.Pointer.Captured == _owner)
                _press.Pointer.Capture(null);

            _lastPointerEvent = null;
            _press = null;
            _isDragging = false;
            _segments = null;
            ClearTopologySubscriptions();
            ClearActiveSelectionCoordinates();
        }

        public void Reset()
        {
            var hadSelection = _owner.CanCopyDocumentSelection;
            StopAutoScroll();
            if (_press?.Pointer.Captured == _owner)
                _press.Pointer.Capture(null);

            ClearTopologySubscriptions();
            _topologyInvalidated = true;
            _layoutUpdatePending = false;
            _segments = null;
            ClearActiveSelectionCoordinates();
            _owner.ClearSelection();
            SynchronizeSelectionBrushes();
            foreach (var segment in GetSegments())
                segment.Control.ClearSelection();

            _press = null;
            _isDragging = false;
            _lastPointerEvent = null;
            if (hadSelection)
                _owner.NotifySelectionChanged();
            else
                _owner.NotifySelectionDocumentChanged();
        }

        public void InvalidateLayout()
        {
            _segments = null;
            _layoutUpdatePending = true;
        }

        public bool TryGetSegmentBounds(SelectableTextBlock control, out Rect bounds)
        {
            foreach (var segment in GetSegments())
            {
                if (!ReferenceEquals(segment.Control, control))
                    continue;

                bounds = segment.Bounds;
                return true;
            }

            bounds = default;
            return false;
        }

        public bool TryHitTestSegment(
            Point point,
            out SelectableTextBlock? control,
            out Point localPoint,
            out Rect segmentBounds)
        {
            var segments = GetSegments();
            var index = FindSegmentAt(segments, point, allowNearest: false);
            if (index < 0)
            {
                control = null;
                localPoint = default;
                segmentBounds = default;
                return false;
            }

            var segment = segments[index];
            control = segment.Control;
            segmentBounds = segment.Bounds;
            localPoint = GetLocalPosition(segment, point);
            return true;
        }

        public void SelectAll()
        {
            var segments = GetSegments();
            if (segments.Count == 0)
            {
                if (!HasActiveSelection())
                    return;

                ClearActiveSelectionCoordinates();
                _owner.NotifySelectionChanged();
                return;
            }

            var start = new SelectionPoint(0, 0);
            var end = new SelectionPoint(segments.Count - 1, segments[^1].Length);
            var selectionChanged = !IsSameActiveSelection(start, end);
            _owner.ClearSelection();
            SynchronizeSelectionBrushes();
            foreach (var segment in segments)
            {
                segment.Control.SelectionStart = 0;
                segment.Control.SelectionEnd = segment.Length;
            }

            SetActiveSelectionCoordinates(segments, start, end);
            if (selectionChanged)
                _owner.NotifySelectionChanged();
        }

        public void SynchronizeSelectionBrushes()
        {
            var selectionBrush = _owner.SelectionBrush;
            var selectionForegroundBrush = _owner.SelectionForegroundBrush;
            foreach (var segment in GetSegments())
            {
                if (segment.Control.SelectionBrush is null ||
                    ReferenceEquals(segment.Control.SelectionBrush, _synchronizedSelectionBrush))
                {
                    segment.Control.SetCurrentValue(
                        SelectableTextBlock.SelectionBrushProperty,
                        selectionBrush);
                }

                if (segment.Control.SelectionForegroundBrush is null ||
                    ReferenceEquals(
                        segment.Control.SelectionForegroundBrush,
                        _synchronizedSelectionForegroundBrush))
                {
                    segment.Control.SetCurrentValue(
                        SelectableTextBlock.SelectionForegroundBrushProperty,
                        selectionForegroundBrush);
                }
            }

            _synchronizedSelectionBrush = selectionBrush;
            _synchronizedSelectionForegroundBrush = selectionForegroundBrush;
        }

        public string GetSelectedText()
        {
            var selectedSegments = GetSegments()
                .Where(segment => segment.Control.SelectionStart != segment.Control.SelectionEnd)
                .ToArray();
            if (selectedSegments.Length == 0)
                return string.Empty;

            var builder = new StringBuilder();
            Segment? previous = null;
            foreach (var segment in selectedSegments)
            {
                var selectedText = GetSelectedText(segment.Control);
                if (selectedText.Length == 0)
                    continue;

                if (previous is { } preceding)
                    builder.Append(GetSeparator(preceding, segment));

                builder.Append(selectedText);
                previous = segment;
            }

            return builder.ToString();
        }

        public string GetDocumentText()
        {
            var segments = GetSegments();
            if (segments.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            Segment? previous = null;
            foreach (var segment in segments)
            {
                var text = GetSourceText(segment.Control).Replace("\uFFFC", string.Empty, StringComparison.Ordinal);
                if (text.Length == 0)
                    continue;
                if (previous is { } preceding)
                    builder.Append(GetSeparator(preceding, segment).Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
                builder.Append(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
                previous = segment;
            }

            var result = builder.ToString();
            return Environment.NewLine == "\n"
                ? result
                : result.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }

        public bool HasDocumentText()
        {
            var segments = GetSegments();
            for (var index = 0; index < segments.Count; index++)
            {
                if (HasSourceText(segments[index].Control))
                    return true;
            }

            return false;
        }

        public void SelectRange(
            SelectableTextBlock anchorControl,
            int anchorOffset,
            SelectableTextBlock focusControl,
            int focusOffset)
        {
            SynchronizeSelectionBrushes();
            var segments = GetSegments();
            var anchorIndex = FindSegmentIndex(segments, anchorControl);
            var focusIndex = FindSegmentIndex(segments, focusControl);
            if (anchorIndex < 0 || focusIndex < 0)
                return;

            ApplySelection(
                segments,
                new SelectionPoint(anchorIndex, Math.Clamp(anchorOffset, 0, segments[anchorIndex].Length)),
                new SelectionPoint(focusIndex, Math.Clamp(focusOffset, 0, segments[focusIndex].Length)));
        }

        public void SelectWord(SelectableTextBlock control, int offset)
        {
            var segments = GetSegments();
            var index = FindSegmentIndex(segments, control);
            if (index < 0)
                return;
            var range = GetWordRange(GetSourceText(control), offset);
            ApplySelection(
                segments,
                new SelectionPoint(index, range.Start),
                new SelectionPoint(index, range.End));
        }

        public void SelectBlock(SelectableTextBlock control)
        {
            var segments = GetSegments();
            var index = FindSegmentIndex(segments, control);
            if (index < 0)
                return;
            var range = GetBlockRange(segments, index);
            ApplySelection(segments, range.Start, range.End);
        }

        public void OnPointerPressed(object? sender, PointerPressedEventArgs args)
        {
            if (!OwnsInput(args.Source ?? sender))
                return;

            if (IsDuplicatePointerEvent(args))
                return;

            if (ShouldBypassDocumentInput(_owner, args.Source))
                return;

            if (!args.GetCurrentPoint(_owner).Properties.IsLeftButtonPressed)
                return;

            SynchronizeSelectionBrushes();
            var segments = GetSegments();
            var position = args.GetPosition(_owner);
            var segmentIndex = FindSegmentAt(segments, position, allowNearest: true);
            var hadSelection = HasActiveSelection();
            _owner.ClearSelection();

            if (segmentIndex < 0)
            {
                ClearSegments(segments, exceptIndex: -1);
                ClearActiveSelectionCoordinates();
                _press = null;
                if (hadSelection)
                    _owner.NotifySelectionChanged();
                return;
            }

            var segment = segments[segmentIndex];
            var offset = HitTest(segment, GetLocalPosition(segment, position));
            var granularity = GetGranularity(args.ClickCount);
            var anchorRange = granularity switch
            {
                SelectionGranularity.Word => ToSelectionRange(
                    segmentIndex,
                    GetWordRange(GetSourceText(segment.Control), offset)),
                SelectionGranularity.Block => GetBlockRange(segments, segmentIndex),
                _ => new SelectionRange(
                    new SelectionPoint(segmentIndex, offset),
                    new SelectionPoint(segmentIndex, offset))
            };
            _owner.Focus(NavigationMethod.Pointer);
            ClearSegments(segments, exceptIndex: -1);
            _press = new PressState(
                args.Pointer,
                position,
                segment.Control,
                offset,
                anchorRange,
                granularity);
            _isDragging = false;
            args.Pointer.Capture(_owner);
            args.Handled = true;

            if (granularity != SelectionGranularity.Character)
            {
                ApplySelection(
                    segments,
                    anchorRange.Start,
                    anchorRange.End);
                return;
            }

            ClearActiveSelectionCoordinates();
            if (hadSelection)
                _owner.NotifySelectionChanged();
        }

        public void OnPointerMoved(object? sender, PointerEventArgs args)
        {
            if (!OwnsInput(args.Source ?? sender))
                return;

            if (IsDuplicatePointerEvent(args))
                return;

            if (_press is not { } press ||
                !ReferenceEquals(args.Pointer, press.Pointer) ||
                !args.GetCurrentPoint(_owner).Properties.IsLeftButtonPressed)
                return;

            args.Handled = true;
            var position = args.GetPosition(_owner);
            if (!_isDragging && DistanceSquared(press.Position, position) < DragThreshold * DragThreshold)
                return;

            _isDragging = true;
            args.PreventGestureRecognition();
            UpdateSelection(position);
            UpdateAutoScroll(args);
        }

        public void OnPointerReleased(object? sender, PointerReleasedEventArgs args)
        {
            if (!OwnsInput(args.Source ?? sender))
                return;

            if (IsDuplicatePointerEvent(args))
                return;

            if (_press is not { } press || !ReferenceEquals(args.Pointer, press.Pointer))
                return;

            var handled = _isDragging || press.Granularity != SelectionGranularity.Character;
            StopAutoScroll();
            _owner.ClearSelection();
            if (args.Pointer.Captured == _owner)
                args.Pointer.Capture(null);

            _press = null;
            _isDragging = false;
            args.Handled = handled;

        }

        private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
        {
            StopAutoScroll();
            _press = null;
            _isDragging = false;
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            StopAutoScroll();
            if (_press?.Pointer.Captured == _owner)
                _press.Pointer.Capture(null);

            _press = null;
            _isDragging = false;
        }

        private void OnLayoutUpdated(object? sender, EventArgs args)
        {
            if (!_layoutUpdatePending)
                return;

            var documentChanged = _topologyInvalidated;
            _layoutUpdatePending = false;
            var previousSegments = _segments;
            _segments = null;
            if (!HasActiveSelection())
            {
                if (documentChanged)
                    _owner.NotifySelectionDocumentChanged();
                return;
            }

            var currentSegments = GetSegments();
            if (TryRemapActiveSelection(
                    currentSegments,
                    out var start,
                    out var end,
                    out var selectionTopologyChanged))
            {
                if (selectionTopologyChanged || !IsSelectionApplied(currentSegments, start, end))
                {
                    ClearTrackedSelectionControls();
                    ClearSegments(currentSegments, exceptIndex: -1);
                    ApplySelectionToSegments(currentSegments, start, end);
                }

                SetActiveSelectionCoordinates(currentSegments, start, end);
                if (selectionTopologyChanged)
                    _owner.NotifySelectionChanged();
                return;
            }

            ClearTrackedSelectionControls();
            if (previousSegments is not null)
                ClearSegments(previousSegments, exceptIndex: -1);
            ClearSegments(currentSegments, exceptIndex: -1);
            _owner.ClearSelection();
            ClearActiveSelectionCoordinates();
            _owner.NotifySelectionChanged();
        }

        private async void OnKeyDown(object? sender, KeyEventArgs args)
        {
            if (!OwnsInput(args.Source ?? sender) ||
                ShouldBypassDocumentInput(_owner, args.Source) ||
                !HasPrimaryModifier(args.KeyModifiers))
                return;

            switch (args.Key)
            {
                case Key.A:
                    SelectAll();
                    args.Handled = true;
                    break;
                case Key.C when CanCopy:
                    args.Handled = true;
                    await CopyFromInputAsync();
                    break;
            }
        }

        private async void OnCopyingToClipboard(object? sender, RoutedEventArgs args)
        {
            if (!OwnsInput(args.Source ?? sender) ||
                ShouldBypassDocumentInput(_owner, args.Source) ||
                !CanCopy)
                return;

            args.Handled = true;
            await CopyFromInputAsync();
        }

        private async Task CopyFromInputAsync()
        {
            try
            {
                await CopyAsync(_owner);
            }
            catch (Exception exception) when (MarkdownAsyncExceptionBoundary.IsRecoverable(exception))
            {
                Trace.TraceWarning("Markdown selection could not be copied to the clipboard: {0}", exception);
            }
        }

        private bool OwnsInput(object? source) => ReferenceEquals(FindDocumentOwner(source), _owner);

        private void ApplySelection(
            IReadOnlyList<Segment> segments,
            SelectionPoint anchor,
            SelectionPoint focus)
        {
            _owner.ClearSelection();
            _owner.Focus(NavigationMethod.Pointer);
            var selectionTopologyChanged = false;
            if (HasActiveSelection())
            {
                if (TryRemapActiveSelection(
                        segments,
                        out var remappedStart,
                        out var remappedEnd,
                        out selectionTopologyChanged))
                {
                    if (selectionTopologyChanged)
                    {
                        ClearTrackedSelectionControls();
                        ClearSegments(segments, exceptIndex: -1);
                    }

                    SetActiveSelectionCoordinates(segments, remappedStart, remappedEnd);
                }
                else
                {
                    selectionTopologyChanged = true;
                    ClearTrackedSelectionControls();
                    ClearSegments(segments, exceptIndex: -1);
                    ClearActiveSelectionCoordinates();
                }
            }
            var start = Compare(anchor, focus) <= 0 ? anchor : focus;
            var end = Compare(anchor, focus) <= 0 ? focus : anchor;
            var selectionChanged = selectionTopologyChanged || (HasActiveSelection()
                ? !IsSameActiveSelection(start, end)
                : Compare(start, end) != 0);

            var updateStart = start.SegmentIndex;
            var updateEnd = end.SegmentIndex;
            if (_lastSelectionStart is { } previousStart && _lastSelectionEnd is { } previousEnd)
            {
                updateStart = Math.Min(updateStart, previousStart.SegmentIndex);
                updateEnd = Math.Max(updateEnd, previousEnd.SegmentIndex);
            }

            for (var index = updateStart; index <= updateEnd; index++)
            {
                var segment = segments[index];
                if (index < start.SegmentIndex || index > end.SegmentIndex)
                {
                    segment.Control.ClearSelection();
                    continue;
                }

                var startOffset = index == start.SegmentIndex ? start.Offset : 0;
                var endOffset = index == end.SegmentIndex ? end.Offset : segment.Length;
                segment.Control.SelectionStart = startOffset;
                segment.Control.SelectionEnd = endOffset;
            }

            SetActiveSelectionCoordinates(segments, start, end);
            if (selectionChanged)
                _owner.NotifySelectionChanged();
        }

        private bool HasActiveSelection() =>
            _lastSelectionStart is { } start &&
            _lastSelectionEnd is { } end &&
            Compare(start, end) != 0;

        private bool IsSameActiveSelection(SelectionPoint start, SelectionPoint end) =>
            HasActiveSelection() &&
            _lastSelectionStart == start &&
            _lastSelectionEnd == end;

        private void SetActiveSelectionCoordinates(
            IReadOnlyList<Segment> segments,
            SelectionPoint start,
            SelectionPoint end)
        {
            _lastSelectionStart = start;
            _lastSelectionEnd = end;
            _lastSelectionStartControl = segments[start.SegmentIndex].Control;
            _lastSelectionEndControl = segments[end.SegmentIndex].Control;
            _selectedControls.Clear();
            for (var index = start.SegmentIndex; index <= end.SegmentIndex; index++)
                _selectedControls.Add(segments[index].Control);
        }

        private void ClearActiveSelectionCoordinates()
        {
            _lastSelectionStart = null;
            _lastSelectionEnd = null;
            _lastSelectionStartControl = null;
            _lastSelectionEndControl = null;
            _selectedControls.Clear();
        }

        private bool TryRemapActiveSelection(
            IReadOnlyList<Segment> segments,
            out SelectionPoint start,
            out SelectionPoint end,
            out bool selectionTopologyChanged)
        {
            start = default;
            end = default;
            selectionTopologyChanged = false;
            if (_lastSelectionStart is not { } previousStart ||
                _lastSelectionEnd is not { } previousEnd ||
                _lastSelectionStartControl is null ||
                _lastSelectionEndControl is null)
            {
                return false;
            }

            var startIndex = FindSegmentIndex(segments, _lastSelectionStartControl);
            var endIndex = FindSegmentIndex(segments, _lastSelectionEndControl);
            if (startIndex < 0 || endIndex < 0)
                return false;

            start = new SelectionPoint(
                startIndex,
                Math.Clamp(previousStart.Offset, 0, segments[startIndex].Length));
            end = new SelectionPoint(
                endIndex,
                Math.Clamp(previousEnd.Offset, 0, segments[endIndex].Length));
            if (Compare(start, end) > 0)
                (start, end) = (end, start);
            if (Compare(start, end) == 0)
                return false;

            selectionTopologyChanged = previousStart.Offset != start.Offset ||
                                       previousEnd.Offset != end.Offset ||
                                       _selectedControls.Count != end.SegmentIndex - start.SegmentIndex + 1;
            if (!selectionTopologyChanged)
            {
                for (var index = start.SegmentIndex; index <= end.SegmentIndex; index++)
                {
                    if (ReferenceEquals(_selectedControls[index - start.SegmentIndex], segments[index].Control))
                        continue;

                    selectionTopologyChanged = true;
                    break;
                }
            }

            return true;
        }

        private static bool IsSelectionApplied(
            IReadOnlyList<Segment> segments,
            SelectionPoint start,
            SelectionPoint end)
        {
            for (var index = start.SegmentIndex; index <= end.SegmentIndex; index++)
            {
                var segment = segments[index];
                var expectedStart = index == start.SegmentIndex ? start.Offset : 0;
                var expectedEnd = index == end.SegmentIndex ? end.Offset : segment.Length;
                if (segment.Control.SelectionStart != expectedStart || segment.Control.SelectionEnd != expectedEnd)
                    return false;
            }

            return true;
        }

        private static void ApplySelectionToSegments(
            IReadOnlyList<Segment> segments,
            SelectionPoint start,
            SelectionPoint end)
        {
            for (var index = start.SegmentIndex; index <= end.SegmentIndex; index++)
            {
                var segment = segments[index];
                segment.Control.SelectionStart = index == start.SegmentIndex ? start.Offset : 0;
                segment.Control.SelectionEnd = index == end.SegmentIndex ? end.Offset : segment.Length;
            }
        }

        private void ClearTrackedSelectionControls()
        {
            for (var index = 0; index < _selectedControls.Count; index++)
                _selectedControls[index].ClearSelection();
        }

        private void UpdateSelection(Point position)
        {
            if (_press is not { } press)
                return;

            var segments = GetSegments();
            var anchorIndex = FindSegmentIndex(segments, press.Control);
            var focusIndex = FindSegmentAt(segments, position, allowNearest: true);
            if (anchorIndex < 0 || focusIndex < 0)
                return;

            var focusSegment = segments[focusIndex];
            var localPosition = GetLocalPosition(focusSegment, position);
            var focusOffset = HitTest(focusSegment, localPosition);
            var rawFocus = new SelectionPoint(focusIndex, focusOffset);
            var rawAnchor = new SelectionPoint(anchorIndex, press.Offset);
            var isForward = Compare(rawAnchor, rawFocus) <= 0;

            SelectionPoint anchor;
            SelectionPoint focus;
            switch (press.Granularity)
            {
                case SelectionGranularity.Word:
                    var focusRange = GetWordRange(GetSourceText(focusSegment.Control), focusOffset);
                    var focusSelectionRange = ToSelectionRange(focusIndex, focusRange);
                    anchor = isForward ? press.AnchorRange.Start : press.AnchorRange.End;
                    focus = isForward ? focusSelectionRange.End : focusSelectionRange.Start;
                    break;
                case SelectionGranularity.Block:
                    var focusBlockRange = GetBlockRange(segments, focusIndex);
                    anchor = isForward ? press.AnchorRange.Start : press.AnchorRange.End;
                    focus = isForward ? focusBlockRange.End : focusBlockRange.Start;
                    break;
                default:
                    anchor = rawAnchor;
                    focus = rawFocus;
                    break;
            }

            ApplySelection(segments, anchor, focus);
        }

        private static Point GetLocalPosition(Segment segment, Point ownerPosition) =>
            segment.OwnerToLocalTransform is { } transform
                ? transform.Transform(ownerPosition)
                : new Point(
                    ownerPosition.X - segment.Bounds.X,
                    ownerPosition.Y - segment.Bounds.Y);

        private void UpdateAutoScroll(PointerEventArgs args)
        {
            var scrollViewer = _owner.FindAncestorOfType<ScrollViewer>();
            if (scrollViewer is null || scrollViewer.Viewport.Height <= 0 || scrollViewer.Extent.Height <= scrollViewer.Viewport.Height)
            {
                StopAutoScroll();
                return;
            }

            var viewportPosition = args.GetPosition(scrollViewer);
            var viewportHeight = Math.Min(scrollViewer.Bounds.Height, scrollViewer.Viewport.Height);
            var edge = Math.Min(AutoScrollEdge, viewportHeight / 2);
            var step = 0d;
            if (viewportPosition.Y < edge)
            {
                var intensity = Math.Clamp((edge - viewportPosition.Y) / edge, 0, 1);
                step = -(AutoScrollMinimumStep + intensity * (AutoScrollMaximumStep - AutoScrollMinimumStep));
            }
            else if (viewportPosition.Y > viewportHeight - edge)
            {
                var intensity = Math.Clamp((viewportPosition.Y - (viewportHeight - edge)) / edge, 0, 1);
                step = AutoScrollMinimumStep + intensity * (AutoScrollMaximumStep - AutoScrollMinimumStep);
            }

            if (step == 0)
            {
                StopAutoScroll();
                return;
            }

            _autoScrollViewer = scrollViewer;
            _autoScrollViewportPosition = viewportPosition;
            _autoScrollStep = step;
            EnsureAutoScrollTimer();
            ScrollAndExtendSelection();
        }

        private void EnsureAutoScrollTimer()
        {
            _autoScrollTimer ??= new DispatcherTimer(
                AutoScrollInterval,
                DispatcherPriority.Input,
                OnAutoScrollTimerTick);
            if (!_autoScrollTimer.IsEnabled)
                _autoScrollTimer.Start();
        }

        private void OnAutoScrollTimerTick(object? sender, EventArgs args) => ScrollAndExtendSelection();

        private void ScrollAndExtendSelection()
        {
            if (_autoScrollViewer is not { } scrollViewer || _press is null || !_isDragging)
            {
                StopAutoScroll();
                return;
            }

            var maximum = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            var nextY = Math.Clamp(scrollViewer.Offset.Y + _autoScrollStep, 0, maximum);
            if (Math.Abs(nextY - scrollViewer.Offset.Y) < double.Epsilon)
            {
                StopAutoScroll();
                return;
            }

            scrollViewer.SetCurrentValue(
                ScrollViewer.OffsetProperty,
                new Vector(scrollViewer.Offset.X, nextY));
            if (scrollViewer.TranslatePoint(_autoScrollViewportPosition, _owner) is { } ownerPosition)
                UpdateSelection(ownerPosition);
        }

        private void StopAutoScroll()
        {
            _autoScrollTimer?.Stop();
            _autoScrollViewer = null;
            _autoScrollStep = 0;
        }

        private void DisposeAutoScrollTimer()
        {
            StopAutoScroll();
            if (_autoScrollTimer is null)
                return;

            _autoScrollTimer.Tick -= OnAutoScrollTimerTick;
            _autoScrollTimer = null;
        }

        private IReadOnlyList<Segment> GetSegments()
        {
            if (_segments is not null)
                return _segments;

            if (_topologyInvalidated)
                ClearTopologySubscriptions();

            var candidates = new List<SegmentCandidate>();
            AppendInlineSegmentControls(_owner, _owner.Inlines, default, candidates, _owner.IsVisible);
            var seen = new HashSet<SelectableTextBlock>();
            var segments = new List<Segment>();

            foreach (var candidate in candidates)
            {
                var control = candidate.Control;
                if (!seen.Add(control))
                    continue;

                RegisterSegment(control);
                var length = GetTextLength(control);

                if (length > 0)
                {
                    var localBounds = new Rect(ResolveSegmentSize(control));
                    var localToOwnerTransform = control.TransformToVisual(_owner);
                    if (localToOwnerTransform is { } transform && transform.TryInvert(out var ownerToLocalTransform))
                    {
                        segments.Add(new Segment(
                            control,
                            localBounds.TransformToAABB(transform),
                            localBounds,
                            ownerToLocalTransform,
                            length));
                    }
                    else
                    {
                        var origin = control.TranslatePoint(default, _owner) ?? candidate.Origin;
                        segments.Add(new Segment(
                            control,
                            new Rect(origin, localBounds.Size),
                            localBounds,
                            null,
                            length));
                    }
                }
            }

            _topologyInvalidated = false;
            _segmentSnapshotVersion++;
            _segments = segments;
            return _segments;
        }

        private static Size ResolveSegmentSize(SelectableTextBlock control)
        {
            var layoutWidth = control.TextLayout.WidthIncludingTrailingWhitespace +
                              control.Padding.Left +
                              control.Padding.Right;
            var layoutHeight = control.TextLayout.Height +
                               control.Padding.Top +
                               control.Padding.Bottom;
            return new Size(
                Math.Max(control.Bounds.Width, Math.Max(control.DesiredSize.Width, layoutWidth)),
                Math.Max(control.Bounds.Height, Math.Max(control.DesiredSize.Height, layoutHeight)));
        }

        private void AppendInlineSegmentControls(
            TextBlock textBlock,
            InlineCollection? inlines,
            Point textBlockOrigin,
            ICollection<SegmentCandidate> candidates,
            bool ancestorsVisible)
        {
            ObserveObject(textBlock);
            if (inlines is null)
                return;

            ObserveCollection(inlines);
            var textPosition = 0;
            AppendInlineSegmentControls(
                textBlock,
                inlines,
                textBlockOrigin,
                candidates,
                ancestorsVisible,
                ref textPosition);
        }

        private void AppendInlineSegmentControls(
            TextBlock textBlock,
            InlineCollection inlines,
            Point textBlockOrigin,
            ICollection<SegmentCandidate> candidates,
            bool ancestorsVisible,
            ref int textPosition)
        {
            foreach (var inline in inlines)
            {
                ObserveObject(inline);
                switch (inline)
                {
                    case Run run:
                        textPosition += run.Text?.Length ?? 0;
                        break;
                    case LineBreak:
                        textPosition++;
                        break;
                    case Span span:
                        AppendInlineSegmentControls(
                            textBlock,
                            span.Inlines,
                            textBlockOrigin,
                            candidates,
                            ancestorsVisible,
                            ref textPosition);
                        break;
                    case InlineUIContainer { Child: { } child }:
                        var inlineBounds = textBlock.TextLayout.HitTestTextPosition(textPosition);
                        var childOrigin = textBlockOrigin + new Vector(
                            textBlock.Padding.Left + inlineBounds.X,
                            textBlock.Padding.Top + inlineBounds.Y);
                        AppendSegmentControls(child, childOrigin, candidates, ancestorsVisible);
                        textPosition++;
                        break;
                }
            }
        }

        private void AppendSegmentControls(
            Control control,
            Point origin,
            ICollection<SegmentCandidate> candidates,
            bool ancestorsVisible)
        {
            ObserveObject(control);
            var isVisible = ancestorsVisible && control.IsVisible;

            if (isVisible && control is SelectableTextBlock selectable and not MarkdownTextBlock)
                candidates.Add(new SegmentCandidate(selectable, origin));

            if (control is TextBlock { Inlines: { } inlines })
                AppendInlineSegmentControls((TextBlock)control, inlines, origin, candidates, isVisible);

            switch (control)
            {
                case Panel panel:
                    ObserveCollection(panel.Children);
                    foreach (var child in panel.Children)
                        AppendSegmentControls(
                            child,
                            origin + (Vector)child.Bounds.Position,
                            candidates,
                            isVisible);
                    break;
                case Decorator { Child: { } child }:
                    AppendSegmentControls(
                        child,
                        origin + (Vector)child.Bounds.Position,
                        candidates,
                        isVisible);
                    break;
                case ContentControl { Content: Control child }:
                    AppendSegmentControls(
                        child,
                        origin + (Vector)child.Bounds.Position,
                        candidates,
                        isVisible);
                    break;
            }
        }

        private void ObserveObject(AvaloniaObject item)
        {
            if (_observedObjects.Add(item))
                item.PropertyChanged += OnObservedPropertyChanged;

            if (item is Visual visual)
                SynchronizeTransformSubscription(visual);
        }

        private void ObserveCollection(object collection)
        {
            if (collection is not INotifyCollectionChanged observable || !_observedCollections.Add(observable))
                return;

            observable.CollectionChanged += OnObservedCollectionChanged;
        }

        private void OnObservedPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Visual.IsVisibleProperty ||
                args.Property == TextBlock.TextProperty ||
                args.Property == TextBlock.InlinesProperty ||
                args.Property == Decorator.ChildProperty ||
                args.Property == ContentControl.ContentProperty ||
                args.Property == Run.TextProperty ||
                args.Property == Span.InlinesProperty ||
                args.Property == InlineUIContainer.ChildProperty)
            {
                InvalidateTopology();
                return;
            }

            if (args.Property == Visual.RenderTransformProperty && sender is Visual visual)
                SynchronizeTransformSubscription(visual);

            if (args.Property == Visual.BoundsProperty ||
                args.Property == Visual.RenderTransformProperty ||
                args.Property == Visual.RenderTransformOriginProperty)
            {
                InvalidateLayout();
            }
        }

        private void SynchronizeTransformSubscription(Visual visual)
        {
            var transform = visual.RenderTransform as Transform;
            if (_observedTransforms.TryGetValue(visual, out var observed))
            {
                if (ReferenceEquals(observed, transform))
                    return;

                observed.Changed -= OnObservedTransformChanged;
                _observedTransforms.Remove(visual);
            }

            if (transform is null)
                return;

            transform.Changed += OnObservedTransformChanged;
            _observedTransforms.Add(visual, transform);
        }

        private void OnObservedTransformChanged(object? sender, EventArgs args) => InvalidateLayout();

        private void OnObservedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
            InvalidateTopology();

        private void InvalidateTopology()
        {
            ClearTopologySubscriptions();
            _topologyInvalidated = true;
            InvalidateLayout();
        }

        private void ClearTopologySubscriptions()
        {
            foreach (var transform in _observedTransforms.Values)
                transform.Changed -= OnObservedTransformChanged;
            _observedTransforms.Clear();

            foreach (var item in _observedObjects)
                item.PropertyChanged -= OnObservedPropertyChanged;
            _observedObjects.Clear();

            foreach (var collection in _observedCollections)
                collection.CollectionChanged -= OnObservedCollectionChanged;
            _observedCollections.Clear();
        }

        private bool IsDuplicatePointerEvent(RoutedEventArgs args)
        {
            if (ReferenceEquals(_lastPointerEvent, args))
                return true;

            _lastPointerEvent = args;
            return false;
        }

        private static SelectionGranularity GetGranularity(int clickCount) => clickCount switch
        {
            >= 3 => SelectionGranularity.Block,
            2 => SelectionGranularity.Word,
            _ => SelectionGranularity.Character
        };

        private static TextRange GetWordRange(string text, int offset)
        {
            if (text.Length == 0)
                return default;

            var characterIndex = Math.Clamp(offset, 0, text.Length - 1);
            var category = GetWordCategory(text[characterIndex]);
            var start = characterIndex;
            while (start > 0 && GetWordCategory(text[start - 1]) == category)
                start--;

            var end = characterIndex + 1;
            while (end < text.Length && GetWordCategory(text[end]) == category)
                end++;

            return new TextRange(start, end);
        }

        private static SelectionRange ToSelectionRange(int segmentIndex, TextRange range) => new(
            new SelectionPoint(segmentIndex, range.Start),
            new SelectionPoint(segmentIndex, range.End));

        private static SelectionRange GetBlockRange(IReadOnlyList<Segment> segments, int segmentIndex)
        {
            var startIndex = segmentIndex;
            while (startIndex > 0 &&
                   AreInSameFlowGroup(segments[startIndex - 1].Control, segments[segmentIndex].Control))
            {
                startIndex--;
            }

            var endIndex = segmentIndex;
            while (endIndex + 1 < segments.Count &&
                   AreInSameFlowGroup(segments[segmentIndex].Control, segments[endIndex + 1].Control))
            {
                endIndex++;
            }

            return new SelectionRange(
                new SelectionPoint(startIndex, 0),
                new SelectionPoint(endIndex, segments[endIndex].Length));
        }

        private static WordCategory GetWordCategory(char character)
        {
            if (char.IsWhiteSpace(character))
                return WordCategory.Whitespace;

            return char.IsLetterOrDigit(character) || character == '_'
                ? WordCategory.Word
                : WordCategory.Punctuation;
        }

        private static int GetTextLength(SelectableTextBlock control)
        {
            var contentLength = GetSourceText(control).Length;
            if (contentLength > 0)
                return contentLength;

            var lines = control.TextLayout.TextLines;
            if (lines.Count > 0)
            {
                var lastLine = lines[^1];
                var layoutLength = lastLine.FirstTextSourceIndex + lastLine.Length;
                if (layoutLength > 0)
                    return layoutLength;
            }

            var originalStart = control.SelectionStart;
            var originalEnd = control.SelectionEnd;
            control.SelectAll();
            var selectionLength = Math.Max(control.SelectionStart, control.SelectionEnd);
            control.SelectionStart = originalStart;
            control.SelectionEnd = originalEnd;
            return selectionLength;
        }

        private static string GetSourceText(SelectableTextBlock control) =>
            control.Text ?? GetInlineText(control.Inlines);

        private static bool HasSourceText(SelectableTextBlock control)
        {
            if (control.Text is { Length: > 0 } text)
            {
                foreach (var character in text)
                {
                    if (character != '\uFFFC')
                        return true;
                }

                return false;
            }

            return HasInlineText(control.Inlines);
        }

        private static bool HasInlineText(InlineCollection? inlines)
        {
            if (inlines is null)
                return false;

            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run { Text.Length: > 0 }:
                    case LineBreak:
                        return true;
                    case Span span when HasInlineText(span.Inlines):
                        return true;
                }
            }

            return false;
        }

        private static string GetSelectedText(SelectableTextBlock control)
        {
            var source = GetSourceText(control);
            if (source.Length == 0)
                return string.Empty;

            var start = Math.Clamp(Math.Min(control.SelectionStart, control.SelectionEnd), 0, source.Length);
            var end = Math.Clamp(Math.Max(control.SelectionStart, control.SelectionEnd), 0, source.Length);
            var selectedText = source[start..end].Replace("\uFFFC", string.Empty, StringComparison.Ordinal);
            return control.Text is null && Environment.NewLine != "\n"
                ? selectedText.Replace("\n", Environment.NewLine, StringComparison.Ordinal)
                : selectedText;
        }

        private static string GetInlineText(InlineCollection? inlines)
        {
            if (inlines is null)
                return string.Empty;

            var builder = new StringBuilder(GetInlineTextLength(inlines));
            AppendInlineText(builder, inlines);
            return builder.ToString();
        }

        private static void AppendInlineText(StringBuilder builder, InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run { Text: { } text }:
                        builder.Append(text);
                        break;
                    case LineBreak:
                        builder.Append('\n');
                        break;
                    case Span span:
                        AppendInlineText(builder, span.Inlines);
                        break;
                    case InlineUIContainer:
                        builder.Append('\uFFFC');
                        break;
                }
            }
        }

        private static int GetInlineTextLength(InlineCollection? inlines)
        {
            if (inlines is null)
                return 0;

            var length = 0;
            foreach (var inline in inlines)
            {
                length += inline switch
                {
                    Run run => run.Text?.Length ?? 0,
                    LineBreak => 1,
                    Span span => GetInlineTextLength(span.Inlines),
                    InlineUIContainer => 1,
                    _ => 0
                };
            }

            return length;
        }

        private static int HitTest(Segment segment, Point localPosition)
        {
            var padding = segment.Control.Padding;
            var textPosition = new Point(
                Math.Clamp(
                    localPosition.X - padding.Left,
                    0,
                    Math.Max(segment.Control.TextLayout.WidthIncludingTrailingWhitespace, 0)),
                Math.Clamp(
                    localPosition.Y - padding.Top,
                    0,
                    Math.Max(segment.Control.TextLayout.Height, 0)));
            var hit = segment.Control.TextLayout.HitTestPoint(textPosition);
            return Math.Clamp(hit.TextPosition, 0, segment.Length);
        }

        private static int FindSegmentAt(IReadOnlyList<Segment> segments, Point point, bool allowNearest)
        {
            var nearestIndex = -1;
            var nearestVerticalDistance = double.MaxValue;
            var nearestHorizontalDistance = double.MaxValue;
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var bounds = segment.Bounds;
                if (bounds.Contains(point))
                {
                    if (segment.LocalBounds.Contains(GetLocalPosition(segment, point)))
                        return index;

                    // A transformed rectangle may leave empty space inside its axis-aligned bounds.
                    // Do not reinterpret that visual hole as a zero-distance nearest hit.
                    continue;
                }

                if (!allowNearest)
                    continue;

                var verticalDistance = point.Y < bounds.Top
                    ? bounds.Top - point.Y
                    : point.Y > bounds.Bottom
                        ? point.Y - bounds.Bottom
                        : 0;
                var horizontalDistance = point.X < bounds.Left
                    ? bounds.Left - point.X
                    : point.X > bounds.Right
                        ? point.X - bounds.Right
                        : 0;
                if (verticalDistance < nearestVerticalDistance ||
                    verticalDistance == nearestVerticalDistance && horizontalDistance < nearestHorizontalDistance)
                {
                    nearestIndex = index;
                    nearestVerticalDistance = verticalDistance;
                    nearestHorizontalDistance = horizontalDistance;
                }
            }

            return nearestIndex;
        }

        private static int FindSegmentIndex(IReadOnlyList<Segment> segments, SelectableTextBlock control)
        {
            for (var index = 0; index < segments.Count; index++)
            {
                if (ReferenceEquals(segments[index].Control, control))
                    return index;
            }

            return -1;
        }

        private static string GetSeparator(Segment previous, Segment current)
        {
            if (TryGetTableCellPosition(previous.Control, out var previousCell) &&
                TryGetTableCellPosition(current.Control, out var currentCell) &&
                ReferenceEquals(previousCell.Table, currentCell.Table))
            {
                if (previousCell.Row != currentCell.Row)
                    return Environment.NewLine;

                var columnDelta = currentCell.Column - previousCell.Column;
                return columnDelta > 0 ? new string('\t', columnDelta) : string.Empty;
            }

            if (SegmentRegistrations.TryGetValue(current.Control, out var registration) &&
                registration.LineBreaksBefore > 0)
            {
                return registration.LineBreaksBefore > 1
                    ? Environment.NewLine + Environment.NewLine
                    : Environment.NewLine;
            }

            if (AreInSameFlowGroup(previous.Control, current.Control))
                return string.Empty;

            var lineTolerance = Math.Min(previous.Bounds.Height, current.Bounds.Height) * 0.4;
            if (Math.Abs(previous.Bounds.Top - current.Bounds.Top) <= lineTolerance &&
                current.Bounds.Left >= previous.Bounds.Left)
            {
                var horizontalGap = current.Bounds.Left - previous.Bounds.Right;
                var inlineGapTolerance = Math.Min(previous.Control.FontSize, current.Control.FontSize) * 0.5;
                return horizontalGap <= inlineGapTolerance ? string.Empty : "\t";
            }

            var verticalGap = current.Bounds.Top - previous.Bounds.Bottom;
            var paragraphGap = Math.Max(previous.Control.FontSize, current.Control.FontSize) * 0.75;
            return verticalGap > paragraphGap ? Environment.NewLine + Environment.NewLine : Environment.NewLine;
        }

        private static bool AreInSameFlowGroup(
            SelectableTextBlock previous,
            SelectableTextBlock current) =>
            SegmentRegistrations.TryGetValue(previous, out var previousRegistration) &&
            SegmentRegistrations.TryGetValue(current, out var currentRegistration) &&
            previousRegistration.FlowGroup is { } flowGroup &&
            ReferenceEquals(flowGroup, currentRegistration.FlowGroup);

        private static bool TryGetTableCellPosition(
            SelectableTextBlock control,
            out TableCellPosition position)
        {
            if (SegmentRegistrations.TryGetValue(control, out var registration) &&
                registration.TableCell is { } tableCell)
            {
                position = tableCell;
                return true;
            }

            position = default;
            return false;
        }

        private static void ClearSegments(IReadOnlyList<Segment> segments, int exceptIndex)
        {
            for (var index = 0; index < segments.Count; index++)
            {
                if (index != exceptIndex)
                    segments[index].Control.ClearSelection();
            }
        }

        private static bool HasPrimaryModifier(KeyModifiers modifiers) =>
            (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        private static int Compare(SelectionPoint left, SelectionPoint right)
        {
            var segmentComparison = left.SegmentIndex.CompareTo(right.SegmentIndex);
            return segmentComparison != 0 ? segmentComparison : left.Offset.CompareTo(right.Offset);
        }

        private static double DistanceSquared(Point left, Point right)
        {
            var horizontal = left.X - right.X;
            var vertical = left.Y - right.Y;
            return horizontal * horizontal + vertical * vertical;
        }

        private readonly record struct Segment(
            SelectableTextBlock Control,
            Rect Bounds,
            Rect LocalBounds,
            Matrix? OwnerToLocalTransform,
            int Length);

        private readonly record struct SegmentCandidate(SelectableTextBlock Control, Point Origin);

        private readonly record struct SelectionPoint(int SegmentIndex, int Offset);

        private readonly record struct SelectionRange(SelectionPoint Start, SelectionPoint End);

        private readonly record struct TextRange(int Start, int End);

        private enum SelectionGranularity
        {
            Character,
            Word,
            Block
        }

        private enum WordCategory
        {
            Whitespace,
            Word,
            Punctuation
        }

        private sealed record PressState(
            IPointer Pointer,
            Point Position,
            SelectableTextBlock Control,
            int Offset,
            SelectionRange AnchorRange,
            SelectionGranularity Granularity);
    }

    private sealed class SegmentRegistration
    {
        public object? FlowGroup { get; set; }

        public int LineBreaksBefore { get; set; }

        public TableCellPosition? TableCell { get; set; }
    }

    private readonly record struct TableCellPosition(object Table, int Row, int Column);
}
