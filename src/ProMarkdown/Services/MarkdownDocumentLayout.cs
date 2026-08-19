using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProMarkdown.Controls;

namespace ProMarkdown.Services;

internal static class MarkdownDocumentLayout
{
    private static readonly ConditionalWeakTable<MarkdownTextBlock, LayoutState> States = new();

    public static void SetEnabled(MarkdownTextBlock control, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (enabled)
        {
            States.GetValue(control, static owner => new LayoutState(owner));
            return;
        }

        if (States.TryGetValue(control, out var state))
        {
            state.Dispose();
            States.Remove(control);
        }
    }

    public static void Refresh(MarkdownTextBlock control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (States.TryGetValue(control, out var state))
            state.Refresh();
    }

    internal static void Flush(MarkdownTextBlock control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (States.TryGetValue(control, out var state))
            state.Flush();
    }

    private sealed class LayoutState : IDisposable
    {
        private const double WidthTolerance = 0.01;
        private static readonly TimeSpan LayoutUpdateDelay = TimeSpan.Zero;
        private readonly MarkdownTextBlock _owner;
        private readonly DispatcherTimer _resizeTimer;
        private double _lastAppliedWidth = double.NaN;

        public LayoutState(MarkdownTextBlock owner)
        {
            _owner = owner;
            _resizeTimer = new DispatcherTimer(
                LayoutUpdateDelay,
                DispatcherPriority.Background,
                OnResizeTimerTick);
            _owner.SizeChanged += OnOwnerSizeChanged;
            _owner.AttachedToVisualTree += OnOwnerAttachedToVisualTree;
            _owner.DetachedFromVisualTree += OnOwnerDetachedFromVisualTree;
            Refresh();
        }

        public void Dispose()
        {
            _resizeTimer.Stop();
            _resizeTimer.Tick -= OnResizeTimerTick;
            _owner.SizeChanged -= OnOwnerSizeChanged;
            _owner.AttachedToVisualTree -= OnOwnerAttachedToVisualTree;
            _owner.DetachedFromVisualTree -= OnOwnerDetachedFromVisualTree;
        }

        public void Refresh()
        {
            _resizeTimer.Stop();
            _lastAppliedWidth = double.NaN;
            MarkdownDocumentSelection.InvalidateLayout(_owner);
            ApplyCurrentWidth();
        }

        public void Flush()
        {
            _resizeTimer.Stop();
            ApplyCurrentWidth();
        }

        private void OnOwnerSizeChanged(object? sender, SizeChangedEventArgs args)
        {
            MarkdownDocumentSelection.InvalidateLayout(_owner);
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private void OnOwnerAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            MarkdownDocumentSelection.InvalidateLayout(_owner);
            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private void OnOwnerDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args) =>
            _resizeTimer.Stop();

        private void OnResizeTimerTick(object? sender, EventArgs args)
        {
            _resizeTimer.Stop();
            ApplyCurrentWidth();
        }

        private void ApplyCurrentWidth()
        {
            var width = _owner.Bounds.Width - _owner.Padding.Left - _owner.Padding.Right;
            if (!double.IsFinite(width) || width <= 0 ||
                Math.Abs(_lastAppliedWidth - width) <= WidthTolerance)
            {
                return;
            }

            _lastAppliedWidth = width;
            if (_owner.Inlines is { } inlines)
                ApplyWidth(inlines, width);
            MarkdownDocumentSelection.InvalidateLayout(_owner);
        }

        private static void ApplyWidth(InlineCollection inlines, double width)
        {
            foreach (var inline in inlines)
            {
                if (inline is Span span)
                    ApplyWidth(span.Inlines, width);

                if (inline is InlineUIContainer { Child: { } child })
                {
                    var containsThematicBreak = ContainsThematicBreak(child);
                    var stretchesToDocumentWidth = containsThematicBreak ||
                                                   MarkdownRenderedElementMetadata.GetStretchesToDocumentWidth(child);
                    if (stretchesToDocumentWidth)
                        ApplyBlockWidth(child, width);

                    ApplyWidth(child, stretchesToDocumentWidth ? width : ResolveChildWidth(child, width));
                }
            }
        }

        private static void ApplyBlockWidth(Control control, double width)
        {
            var availableWidth = Math.Max(1, width - control.Margin.Left - control.Margin.Right);
            if (!double.IsFinite(control.Width) ||
                Math.Abs(control.Width - availableWidth) > WidthTolerance)
            {
                control.Width = availableWidth;
            }
        }

        private static bool ContainsThematicBreak(Control control)
        {
            if (control is MarkdownThematicBreak)
                return true;

            switch (control)
            {
                case MarkdownTextBlock:
                    return false;
                case TextBlock { Inlines: { } inlines }:
                    foreach (var inline in inlines)
                    {
                        if (inline is InlineUIContainer { Child: { } child } && ContainsThematicBreak(child))
                            return true;
                    }

                    break;
                case Panel panel:
                    foreach (var child in panel.Children)
                    {
                        if (ContainsThematicBreak(child))
                            return true;
                    }

                    break;
                case Decorator { Child: { } child }:
                    return ContainsThematicBreak(child);
                case ContentControl { Content: Control child }:
                    return ContainsThematicBreak(child);
            }

            return false;
        }

        private static void ApplyWidth(Control control, double width)
        {
            if (control is MarkdownTextBlock)
                return;

            if (control is MarkdownThematicBreak)
            {
                ApplyBlockWidth(control, width);
                return;
            }

            if (control is MarkdownWrappingSelectableTextBlock selectableText)
            {
                if (Math.Abs(selectableText.MaxWidth - width) > WidthTolerance)
                    selectableText.MaxWidth = width;
            }

            var contentWidth = GetContentWidth(control, width);
            if (control is TextBlock { Inlines: { } inlines })
                ApplyWidth(inlines, contentWidth);

            switch (control)
            {
                case Panel panel:
                    var stretchesChildren = MarkdownRenderedElementMetadata.GetStretchesToDocumentWidth(control);
                    foreach (var child in panel.Children)
                    {
                        var childWidth = stretchesChildren
                            ? Math.Max(1, contentWidth - Math.Max(0, child.Bounds.X))
                            : ResolveChildWidth(child, contentWidth);
                        ApplyWidth(child, childWidth);
                    }
                    break;
                case Decorator { Child: { } child }:
                    ApplyWidth(child, contentWidth);
                    break;
                case ContentControl { Content: Control child }:
                    ApplyWidth(child, contentWidth);
                    break;
            }
        }

        private static double ResolveChildWidth(Control child, double parentWidth)
        {
            var availableWidth = Math.Max(1, parentWidth - Math.Max(0, child.Bounds.X));
            if (child is MarkdownWrappingSelectableTextBlock or MarkdownThematicBreak ||
                !double.IsFinite(child.Bounds.Width) ||
                child.Bounds.Width <= 0)
            {
                return availableWidth;
            }

            return Math.Max(1, Math.Min(availableWidth, child.Bounds.Width));
        }

        private static double GetContentWidth(Control control, double width)
        {
            var horizontalInset = control switch
            {
                Border border => border.Padding.Left + border.Padding.Right +
                                 border.BorderThickness.Left + border.BorderThickness.Right,
                TextBlock text => text.Padding.Left + text.Padding.Right,
                _ => 0
            };
            return Math.Max(1, width - horizontalInset);
        }
    }
}
