using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ProMarkdown.Sample.Controls;

public sealed class PreviewScrollViewer : ScrollViewer
{
    // Match ScrollContentPresenter's physical wheel distance.
    private const double WheelStep = 50;
    private static readonly TimeSpan WheelDuration = TimeSpan.FromMilliseconds(140);

    private readonly Action<TimeSpan> _advanceFrame;
    private double _start;
    private double _target;
    private long _started;
    private bool _isScrolling;
    private bool _isFrameRequested;
    private bool _isApplyingOffset;

    public PreviewScrollViewer()
    {
        _advanceFrame = AdvanceFrame;
        AddHandler(PointerWheelChangedEvent, OnPreviewWheel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, _) => _isScrolling = false, RoutingStrategies.Tunnel);
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OffsetProperty && !_isApplyingOffset)
            _isScrolling = false;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isScrolling = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta;
        if (!IsScrollInertiaEnabled || e.Pointer.Type != PointerType.Mouse || e.KeyModifiers != KeyModifiers.None ||
            delta.X != 0 || delta.Y == 0 || Math.Truncate(delta.Y) != delta.Y ||
            e.Source is not Visual source || HasOwnWheelBehavior(source) ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            _isScrolling = false;
            return;
        }

        var current = Offset.Y;
        var distance = -delta.Y * WheelStep;
        var origin = _isScrolling && Math.Sign(_target - current) == Math.Sign(distance) ? _target : current;
        var target = Math.Clamp(origin + distance, 0, ScrollBarMaximum.Y);
        if (target == current)
        {
            _isScrolling = false;
            return;
        }

        e.Handled = true;
        if (_isScrolling && _target == target)
            return;

        _start = current;
        _target = target;
        _started = Stopwatch.GetTimestamp();
        _isScrolling = true;
        RequestFrame(topLevel);
    }

    private bool HasOwnWheelBehavior(Visual source)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, this); visual = visual.GetVisualParent())
        {
            if (visual is ScrollViewer or ComboBox or Slider)
                return true;
        }
        return false;
    }

    private void RequestFrame(TopLevel topLevel)
    {
        if (_isFrameRequested)
            return;

        _isFrameRequested = true;
        topLevel.RequestAnimationFrame(_advanceFrame);
    }

    private void AdvanceFrame(TimeSpan timestamp)
    {
        _isFrameRequested = false;
        if (!_isScrolling || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            _isScrolling = false;
            return;
        }

        var remaining = Math.Max(0, 1 - (Stopwatch.GetElapsedTime(_started) / WheelDuration));
        var offset = _target + ((_start - _target) * remaining * remaining * remaining);
        _isApplyingOffset = true;
        try
        {
            SetCurrentValue(OffsetProperty, new Vector(Offset.X, offset));
        }
        finally
        {
            _isApplyingOffset = false;
        }

        if (remaining > 0)
            RequestFrame(topLevel);
        else
            _isScrolling = false;
    }
}
