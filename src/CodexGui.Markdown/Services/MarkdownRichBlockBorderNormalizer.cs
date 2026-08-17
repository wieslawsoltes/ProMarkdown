using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace CodexGui.Markdown.Services;

internal static class MarkdownRichBlockBorderNormalizer
{
    public static void PreserveRoundedBorders(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
                PreserveRoundedBorders(span.Inlines);

            if (inline is InlineUIContainer { Child: { } child })
                PreserveRoundedBorders(child);
        }
    }

    private static void PreserveRoundedBorders(Control control)
    {
        if (control is MarkdownRichBlockBorder border && RequiresUnclippedStroke(border))
            SeparateStrokeAndContentClipping(border);

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    PreserveRoundedBorders(child);
                break;
            case Decorator { Child: { } child }:
                PreserveRoundedBorders(child);
                break;
            case ContentControl { Content: Control child }:
                PreserveRoundedBorders(child);
                break;
        }
    }

    private static bool RequiresUnclippedStroke(Border border) =>
        border.ClipToBounds &&
        border.Child is not null &&
        HasRoundedCorner(border.CornerRadius) &&
        HasBorder(border.BorderThickness);

    private static void SeparateStrokeAndContentClipping(Border border)
    {
        var content = border.Child;
        border.Child = null;

        var contentClip = new Border
        {
            ClipToBounds = true,
            CornerRadius = GetInnerCornerRadius(border.CornerRadius, border.BorderThickness),
            Child = content
        };

        border.ClipToBounds = false;
        border.Child = contentClip;
    }

    private static CornerRadius GetInnerCornerRadius(CornerRadius radius, Thickness thickness) =>
        new(
            Math.Max(0, radius.TopLeft - Math.Max(thickness.Top, thickness.Left)),
            Math.Max(0, radius.TopRight - Math.Max(thickness.Top, thickness.Right)),
            Math.Max(0, radius.BottomRight - Math.Max(thickness.Bottom, thickness.Right)),
            Math.Max(0, radius.BottomLeft - Math.Max(thickness.Bottom, thickness.Left)));

    private static bool HasRoundedCorner(CornerRadius radius) =>
        radius.TopLeft > 0 ||
        radius.TopRight > 0 ||
        radius.BottomRight > 0 ||
        radius.BottomLeft > 0;

    private static bool HasBorder(Thickness thickness) =>
        thickness.Left > 0 ||
        thickness.Top > 0 ||
        thickness.Right > 0 ||
        thickness.Bottom > 0;
}

internal sealed class MarkdownRichBlockBorder : Border
{
}
