using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;

namespace CodexGui.Markdown.Services;

public sealed class MarkdownHitTestingService : IMarkdownHitTestingService
{
    public MarkdownHitTestResult? HitTest(MarkdownHitTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Host);
        ArgumentNullException.ThrowIfNull(request.RenderResult);

        if (request.Host is CodexGui.Markdown.Controls.MarkdownTextBlock markdownHost &&
            TryHitTestSelectableSegment(markdownHost, request.Point, request.RenderResult, out var segmentMatch))
        {
            return segmentMatch;
        }

        if (TryHitTestVisual(request.Host, request.Point, request.RenderResult.RenderMap, out var visualMatch, out var hitVisual))
        {
            return CreateVisualHitResult(request, hitVisual, visualMatch);
        }

        if (request.TextLayout is null)
        {
            return null;
        }

        if (TryHitTestTextByGeometry(request, out var textHitResult))
        {
            return textHitResult;
        }

        var textPoint = new Point(
            Math.Max(request.Point.X - request.Padding.Left, 0),
            Math.Max(request.Point.Y - request.Padding.Top, 0));
        var textHit = request.TextLayout.HitTestPoint(textPoint);
        if (!textHit.IsInside)
        {
            return null;
        }

        return HitTestTextPosition(request.RenderResult, request.TextLayout, request.Padding, textHit.TextPosition);
    }

    private static bool TryHitTestSelectableSegment(
        CodexGui.Markdown.Controls.MarkdownTextBlock host,
        Point hostPoint,
        MarkdownRenderResult renderResult,
        out MarkdownHitTestResult? result)
    {
        result = null;
        if (!MarkdownDocumentSelection.TryHitTestSegment(
                host,
                hostPoint,
                out var selectable,
                out var localPoint,
                out var segmentBounds) ||
            selectable is not MarkdownWrappingSelectableTextBlock segment)
        {
            return false;
        }

        var padding = segment.Padding;
        var textPoint = new Point(
            Math.Clamp(
                localPoint.X - padding.Left,
                0,
                Math.Max(segment.TextLayout.WidthIncludingTrailingWhitespace, 0)),
            Math.Clamp(
                localPoint.Y - padding.Top,
                0,
                Math.Max(segment.TextLayout.Height, 0)));
        var hit = segment.TextLayout.HitTestPoint(textPoint);
        if (!hit.IsInside ||
            !segment.RenderMap.TryGetTextEntry(hit.TextPosition, out var entry) ||
            entry is null ||
            renderResult.ParseResult.ParentMap.Count > 0 &&
            !renderResult.ParseResult.ParentMap.ContainsKey(entry.AstNode.Node))
        {
            return false;
        }

        var localHighlightRects = ResolveTextHighlightRects(segment.TextLayout, padding, entry);
        result = new MarkdownHitTestResult(
            entry.AstNode,
            entry.ElementKind,
            renderResult.ParseResult,
            visual: segment,
            highlightRects: OffsetRects(localHighlightRects, segmentBounds.TopLeft));
        return true;
    }

    private static IReadOnlyList<Rect> OffsetRects(IReadOnlyList<Rect> rects, Point offset)
    {
        if (rects.Count == 0)
            return Array.Empty<Rect>();

        var offsetRects = new Rect[rects.Count];
        for (var index = 0; index < rects.Count; index++)
            offsetRects[index] = rects[index].Translate((Vector)offset);
        return offsetRects;
    }

    public MarkdownHitTestResult? HitTestTextPosition(MarkdownRenderResult renderResult, int textPosition)
    {
        return HitTestTextPosition(renderResult, textLayout: null, padding: default, textPosition);
    }

    private static MarkdownHitTestResult? HitTestTextPosition(
        MarkdownRenderResult renderResult,
        TextLayout? textLayout,
        Thickness padding,
        int textPosition)
    {
        ArgumentNullException.ThrowIfNull(renderResult);
        if (!renderResult.RenderMap.TryGetTextEntry(textPosition, out var textEntry) || textEntry is null)
        {
            return null;
        }

        return new MarkdownHitTestResult(
            textEntry.AstNode,
            textEntry.ElementKind,
            renderResult.ParseResult,
            renderedTextPosition: textPosition,
            highlightRects: textLayout is null
                ? Array.Empty<Rect>()
                : ResolveTextHighlightRects(textLayout, padding, textEntry));
    }

    private static bool TryHitTestTextByGeometry(MarkdownHitTestRequest request, out MarkdownHitTestResult? result)
    {
        foreach (var textEntry in request.RenderResult.RenderMap.TextEntries)
        {
            var highlightRects = ResolveTextHighlightRects(request.TextLayout!, request.Padding, textEntry);
            if (!ContainsPoint(highlightRects, request.Point))
            {
                continue;
            }

            result = new MarkdownHitTestResult(
                textEntry.AstNode,
                textEntry.ElementKind,
                request.RenderResult.ParseResult,
                renderedTextPosition: textEntry.RenderedTextStart,
                highlightRects: highlightRects);
            return true;
        }

        result = null;
        return false;
    }

    private static MarkdownHitTestResult CreateVisualHitResult(
        MarkdownHitTestRequest request,
        Control hitVisual,
        MarkdownVisualSourceMapEntry visualMatch)
    {
        var astNode = visualMatch.AstNode;
        var elementKind = visualMatch.ElementKind;
        IReadOnlyList<Rect> highlightRects = ResolveControlHighlightRects(visualMatch.Control, request.Host);
        MarkdownVisualHitTestResult? pluginMatch = null;

        if (visualMatch.HitTestHandler is not null &&
            request.Host.TranslatePoint(request.Point, visualMatch.Control) is { } localPoint)
        {
            pluginMatch = visualMatch.HitTestHandler(new MarkdownVisualHitTestRequest
            {
                Host = request.Host,
                HitVisual = hitVisual,
                Control = visualMatch.Control,
                HostPoint = request.Point,
                LocalPoint = localPoint,
                RenderResult = request.RenderResult,
                DefaultEntry = visualMatch
            });

            if (pluginMatch is not null)
            {
                astNode = pluginMatch.AstNode ?? astNode;
                elementKind = pluginMatch.ElementKind ?? elementKind;
                if (pluginMatch.LocalHighlightRects.Count > 0)
                {
                    highlightRects = TransformLocalRectsToHost(visualMatch.Control, request.Host, pluginMatch.LocalHighlightRects);
                }
            }
        }

        return new MarkdownHitTestResult(
            astNode,
            elementKind,
            request.RenderResult.ParseResult,
            visual: visualMatch.Control,
            highlightRects: highlightRects,
            sourceSpanOverride: pluginMatch?.SourceSpan);
    }

    private static bool TryHitTestVisual(
        Control host,
        Point point,
        MarkdownRenderMap renderMap,
        out MarkdownVisualSourceMapEntry visualEntry,
        out Control hitVisual)
    {
        foreach (var visual in host.GetVisualsAt(point))
        {
            if (visual is not Control control)
            {
                continue;
            }

            for (Control? current = control; current is not null; current = current.GetVisualParent() as Control)
            {
                if (renderMap.TryGetVisualEntry(current, out var match) && match is not null)
                {
                    visualEntry = match;
                    hitVisual = control;
                    return true;
                }
            }
        }

        visualEntry = null!;
        hitVisual = null!;
        return false;
    }

    private static IReadOnlyList<Rect> ResolveTextHighlightRects(
        TextLayout textLayout,
        Thickness padding,
        MarkdownTextSourceMapEntry textEntry)
    {
        var highlightRects = new List<Rect>();
        foreach (var rect in textLayout.HitTestTextRange(textEntry.RenderedTextStart, textEntry.RenderedTextLength))
        {
            var translatedRect = new Rect(
                rect.X + padding.Left,
                rect.Y + padding.Top,
                rect.Width,
                rect.Height);
            if (translatedRect.Width > 0 || translatedRect.Height > 0)
            {
                highlightRects.Add(translatedRect);
            }
        }

        return highlightRects;
    }

    private static bool ContainsPoint(IReadOnlyList<Rect> rects, Point point)
    {
        for (var index = 0; index < rects.Count; index++)
        {
            if (rects[index].Inflate(1).Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<Rect> ResolveControlHighlightRects(Control control, Control host)
    {
        return TransformLocalRectsToHost(control, host, [new Rect(control.Bounds.Size)]);
    }

    private static IReadOnlyList<Rect> TransformLocalRectsToHost(Control source, Control host, IReadOnlyList<Rect> localRects)
    {
        if (localRects.Count == 0)
        {
            return Array.Empty<Rect>();
        }

        var hostRects = new List<Rect>(localRects.Count);
        foreach (var rect in localRects)
        {
            if (source.TranslatePoint(rect.TopLeft, host) is not { } topLeft)
            {
                continue;
            }

            hostRects.Add(new Rect(topLeft, rect.Size));
        }

        return hostRects;
    }
}
