using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexGui.Markdown.Sample.Controls;

public sealed class MarkdownHighlightOverlay : Control
{
    public static readonly StyledProperty<IReadOnlyList<Rect>> HighlightRectsProperty =
        AvaloniaProperty.Register<MarkdownHighlightOverlay, IReadOnlyList<Rect>>(
            nameof(HighlightRects),
            Array.Empty<Rect>());

    private static readonly IBrush HighlightFill = new SolidColorBrush(Color.FromArgb(56, 79, 70, 229));
    private static readonly Pen HighlightBorder = new(new SolidColorBrush(Color.FromArgb(180, 79, 70, 229)), 1.5);

    public IReadOnlyList<Rect> HighlightRects
    {
        get => GetValue(HighlightRectsProperty);
        set => SetValue(HighlightRectsProperty, value);
    }

    static MarkdownHighlightOverlay()
    {
        HighlightRectsProperty.Changed.AddClassHandler<MarkdownHighlightOverlay>((overlay, _) => overlay.InvalidateVisual());
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        foreach (var rect in HighlightRects)
        {
            if (rect.Width <= 0 && rect.Height <= 0)
            {
                continue;
            }

            context.DrawRectangle(HighlightFill, HighlightBorder, rect, 6, 6);
        }
    }
}
