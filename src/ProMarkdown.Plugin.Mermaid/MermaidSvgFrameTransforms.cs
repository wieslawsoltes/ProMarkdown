using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;

namespace ProMarkdown.Plugin.Mermaid;

internal static class MermaidSvgFrameTransforms
{
    private const string TransformStart = "transform:translate(";
    private const string ScaleStart = ") scale(";
    private const string OriginStart = ";transform-box:view-box;transform-origin:";
    private const string UnsupportedTransform = "The generated Mermaid animation contains an unsupported SVG transform.";

    public static void Normalize(XAttribute style)
    {
        var value = style.Value.AsSpan();
        var originStart = value.IndexOf(OriginStart, StringComparison.Ordinal);
        if (originStart < 0)
            return;

        var declarations = value[..originStart];
        var transformStart = declarations.IndexOf(TransformStart, StringComparison.Ordinal);
        if (transformStart < 0 || (transformStart > 0 && declarations[transformStart - 1] != ';'))
            ThrowUnsupportedTransform();

        var transform = declarations[(transformStart + TransformStart.Length)..];
        var separator = transform.IndexOf(';');
        var remaining = separator < 0 ? ReadOnlySpan<char>.Empty : transform[(separator + 1)..];
        if (separator >= 0)
            transform = transform[..separator];

        var scaleStart = transform.IndexOf(ScaleStart, StringComparison.Ordinal);
        if (scaleStart < 0 || transform[^1] != ')')
            ThrowUnsupportedTransform();

        var (x, y) = ReadPair(transform[..scaleStart], ',', "px");
        var (scaleX, scaleY) = ReadPair(transform[(scaleStart + ScaleStart.Length)..^1], ',', "", allowSingle: true);
        var (originX, originY) = ReadPair(value[(originStart + OriginStart.Length)..], ' ', "px");
        var offsetX = x + (1 - scaleX) * originX;
        var offsetY = y + (1 - scaleY) * originY;
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY))
            ThrowUnsupportedTransform();

        // Svg.Skia needs a presentation transform for Mermaider's CSS translation and scale.
        style.Parent!.SetAttributeValue("transform", string.Create(
            CultureInfo.InvariantCulture, $"matrix({scaleX} 0 0 {scaleY} {offsetX} {offsetY})"));
        var preserved = string.Concat(declarations[..transformStart], remaining).TrimEnd(';');
        if (preserved.Length == 0)
            style.Remove();
        else
            style.Value = preserved;
    }

    private static (double X, double Y) ReadPair(
        ReadOnlySpan<char> value,
        char separator,
        ReadOnlySpan<char> suffix,
        bool allowSingle = false)
    {
        var split = value.IndexOf(separator);
        if (allowSingle && split < 0)
        {
            var scale = ReadNumber(value, suffix);
            return (scale, scale);
        }
        if (split < 0)
            ThrowUnsupportedTransform();
        return (ReadNumber(value[..split], suffix), ReadNumber(value[(split + 1)..], suffix));
    }

    private static double ReadNumber(ReadOnlySpan<char> value, ReadOnlySpan<char> suffix)
    {
        if (value.EndsWith(suffix, StringComparison.Ordinal) &&
            double.TryParse(value[..(value.Length - suffix.Length)], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number))
        {
            return number;
        }
        throw new InvalidOperationException(UnsupportedTransform);
    }

    [DoesNotReturn]
    private static void ThrowUnsupportedTransform() =>
        throw new InvalidOperationException(UnsupportedTransform);
}
