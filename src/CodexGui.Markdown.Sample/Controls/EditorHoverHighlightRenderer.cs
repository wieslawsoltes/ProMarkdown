using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace CodexGui.Markdown.Sample.Controls;

internal sealed class EditorHoverHighlightRenderer : IBackgroundRenderer
{
    private static readonly IBrush HighlightFill = new SolidColorBrush(Color.FromArgb(64, 14, 165, 233));
    private static readonly Pen HighlightBorder = new(new SolidColorBrush(Color.FromArgb(180, 14, 165, 233)), 1);
    private TextSegment? _segment;

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_segment is null || _segment.Length <= 0)
        {
            return;
        }

        var geometryBuilder = new BackgroundGeometryBuilder
        {
            AlignToWholePixels = true,
            BorderThickness = 1,
            CornerRadius = 4
        };
        geometryBuilder.AddSegment(textView, _segment);

        var geometry = geometryBuilder.CreateGeometry();
        if (geometry is null)
        {
            return;
        }

        drawingContext.DrawGeometry(HighlightFill, HighlightBorder, geometry);
    }

    public void SetHighlight(TextView textView, int startOffset, int length)
    {
        var previous = _segment;
        _segment = length > 0
            ? new TextSegment
            {
                StartOffset = startOffset,
                Length = length
            }
            : null;

        if (previous is not null)
        {
            textView.Redraw(previous);
        }

        if (_segment is not null)
        {
            textView.Redraw(_segment);
        }
    }

    public void Clear(TextView textView)
    {
        SetHighlight(textView, 0, 0);
    }
}
