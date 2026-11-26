using System.Globalization;
using System.Xml.Linq;
using Mermaider;
using Mermaider.Animate;
using Mermaider.Models;
using SkiaSharp;

namespace ProMarkdown.Plugin.Mermaid;

internal sealed class MermaidNativeEdgeFrames
{
    private const int PathSampleCount = 65;
    private static readonly MermaidEdgeDash FastDash = new("9 5", 900, TimeSpan.FromSeconds(20));
    private static readonly MermaidEdgeDash SlowDash = new("9 5", 900, TimeSpan.FromSeconds(50));
    private readonly Lock _gate = new();
    private readonly XElement _svg;
    private readonly double _stepSeconds;
    private readonly FrameEdge[] _edges;

    private MermaidNativeEdgeFrames(XElement svg, double stepSeconds, FrameEdge[] edges)
    {
        _svg = svg;
        _stepSeconds = stepSeconds;
        _edges = edges;
    }

    public static MermaidDiagramRenderResult Create(
        string svg,
        MermaidNativeEdgeSyntax syntax,
        bool enableAnimation,
        string edgeForeground,
        string surface,
        CancellationToken cancellationToken)
    {
        if (syntax.ProbeSource is not { } probe)
            return new MermaidDiagramRenderResult(svg);

        var sourceEdges = ParseEdges(syntax.StructuralSource);
        var annotatedEdges = ParseEdges(probe);
        if (sourceEdges.Count != annotatedEdges.Count)
            throw new InvalidOperationException("Mermaid edge animation could not match the flowchart's links.");

        var animations = MatchAnnotations(annotatedEdges, syntax.Animations);
        // SVG render order can change across disconnected components. Match by edge identity,
        // keeping the source order for otherwise identical links between the same nodes.
        var byIdentity = new Dictionary<EdgeKey, Queue<MermaidNativeEdgeSyntax.EdgeAnimation>>();
        for (var index = 0; index < sourceEdges.Count; index++)
        {
            var edge = sourceEdges[index];
            if (edge.Style == EdgeStyle.Invisible)
                continue;
            var key = EdgeKey.FromSource(edge);
            if (!byIdentity.TryGetValue(key, out var group))
                byIdentity.Add(key, group = new Queue<MermaidNativeEdgeSyntax.EdgeAnimation>());
            group.Enqueue(animations.GetValueOrDefault(index));
        }

        var root = XElement.Parse(svg, LoadOptions.PreserveWhitespace);
        var ns = root.Name.Namespace;
        var motionLayer = new XElement(ns + "g", new XAttribute("aria-hidden", "true"));
        var badges = new XElement(ns + "g", new XAttribute("aria-hidden", "true"));
        var animatedEdges = new List<FrameEdge>();
        var steps = new SortedSet<int>();
        var longestDashDuration = TimeSpan.Zero;
        foreach (var path in root.Elements(ns + "path"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((string?)path.Attribute("class") != "edge" ||
                !byIdentity.TryGetValue(EdgeKey.FromPath(path), out var group) ||
                !group.TryDequeue(out var animation) || (animation.Id is null && !animation.Endings.HasMarkers))
                continue;

            var stroke = (string?)path.Attribute("stroke") ?? edgeForeground;
            var geometry = (string?)path.Attribute("d");
            path.SetAttributeValue("data-mermaid-edge-id", animation.Id);
            var samples = Array.Empty<SKPoint>();
            XElement? highlight = null;
            XElement? dot = null;
            if (geometry is not null && (animation.Step > 0 || animation.Endings.HasMarkers))
            {
                using var route = SKPath.ParseSvgPathData(geometry);
                using var measure = new SKPathMeasure(route, false);
                if (measure.Length > 0)
                {
                    AddEnding(path, measure, animation.Endings.Start, start: true, stroke, surface);
                    AddEnding(path, measure, animation.Endings.End, start: false, stroke, surface);
                    if (animation.Step > 0 && syntax.ShowStepNumbers)
                        AddBadge(badges, measure, animation.Step, stroke, surface, path.Attribute("data-label") is not null);
                    if (animation.Step > 0 && enableAnimation)
                    {
                        samples = SampleRoute(measure);
                        steps.Add(animation.Step);
                        highlight = new XElement(ns + "path",
                            new XAttribute("class", "edge-step-highlight"),
                            new XAttribute("d", geometry), new XAttribute("fill", "none"),
                            new XAttribute("stroke", stroke), new XAttribute("stroke-width", "3.5"),
                            new XAttribute("stroke-linecap", "round"));
                        // Keep the edge highlight beneath labels and nodes.
                        path.AddBeforeSelf(highlight);
                        dot = new XElement(ns + "circle", new XAttribute("class", "edge-step-dot"),
                            new XAttribute("r", "4"), new XAttribute("fill", stroke));
                        motionLayer.Add(dot);
                    }
                }
            }

            var classDash = animation.ClassDash;
            var defaultDash = animation.HasDashes && (enableAnimation || classDash is not null)
                ? animation.Speed == MermaidNativeEdgeSyntax.DashSpeed.Slow ? SlowDash : FastDash
                : null;
            DashMotion? dashMotion = null;
            var array = classDash?.Array ?? defaultDash?.Array;
            if ((array ?? (classDash is null ? null : (string?)path.Attribute("stroke-dasharray"))) is { } effectiveArray)
            {
                var offset = classDash?.Offset ?? defaultDash?.Offset;
                var dashDuration = classDash?.Duration ?? defaultDash?.Duration ?? TimeSpan.Zero;
                if (array is not null)
                    path.SetAttributeValue("stroke-dasharray", array);
                if (array is not null || offset.HasValue)
                    path.SetAttributeValue("stroke-dashoffset", Number(offset.GetValueOrDefault()));
                if (defaultDash is not null && classDash?.Array is null)
                    path.SetAttributeValue("stroke-linecap", "round");
                if (enableAnimation && effectiveArray != "none" && offset is { } startOffset &&
                    startOffset != 0 && dashDuration > TimeSpan.Zero)
                {
                    dashMotion = new(startOffset, dashDuration);
                    if (dashDuration > longestDashDuration)
                        longestDashDuration = dashDuration;
                }
            }
            if (enableAnimation && (dashMotion is not null || dot is not null))
                animatedEdges.Add(new FrameEdge(path, dashMotion, animation.Step, highlight, dot, samples));
        }

        if (motionLayer.HasElements)
            root.Add(motionLayer);
        if (badges.HasElements)
            root.Add(badges);
        if (animatedEdges.Count == 0)
            return new MermaidDiagramRenderResult(Serialize(root));

        var stepSeconds = syntax.StepSeconds;
        if (steps.Count > 0 && (stepSeconds < 1d / TimeSpan.TicksPerSecond ||
            stepSeconds * steps.Max >= TimeSpan.MaxValue.TotalSeconds))
            stepSeconds = MermaidFrontMatter.DefaultStepSeconds;

        var duration = steps.Count == 0 ? longestDashDuration : TimeSpan.FromSeconds(steps.Max * stepSeconds);
        var timeline = steps.Select(step => new AnimationStep(null,
            TimeSpan.FromSeconds((step - 1) * stepSeconds),
            TimeSpan.FromSeconds(step * stepSeconds))).ToArray();
        var frames = new MermaidNativeEdgeFrames(root, stepSeconds, animatedEdges.ToArray());
        string RenderFrame(TimeSpan time, Int128 elapsedTicks) => frames.RenderFrame(time, elapsedTicks, duration);
        return new MermaidDiagramRenderResult(RenderFrame(TimeSpan.Zero, 0),
            new MermaidAnimation(RenderFrame, duration, duration, timeline, Repeats: true), IsAnimationFrame: true);
    }

    private static IReadOnlyList<MermaidEdge> ParseEdges(MermaidSourceNormalization source)
    {
        try
        {
            return MermaidRenderer.Parse(source.Source).Edges;
        }
        catch (MermaidParseException error)
        {
            var mapped = source.MapParseError(error);
            if (ReferenceEquals(mapped, error))
                throw;
            throw mapped;
        }
    }

    private static Dictionary<int, MermaidNativeEdgeSyntax.EdgeAnimation> MatchAnnotations(
        IReadOnlyList<MermaidEdge> annotatedEdges,
        IReadOnlyDictionary<string, MermaidNativeEdgeSyntax.EdgeAnimation> animations)
    {
        var matches = new Dictionary<int, MermaidNativeEdgeSyntax.EdgeAnimation>();
        for (var index = 0; index < annotatedEdges.Count; index++)
        {
            var edge = annotatedEdges[index];
            if (edge.Label is not { } marker || !animations.TryGetValue(marker, out var animation))
                continue;

            // Every expanded edge keeps its authored endings. Only the last source's first outgoing link keeps the ID.
            var first = index;
            var assignedIndex = index;
            while (index + 1 < annotatedEdges.Count && annotatedEdges[index + 1].Label == marker)
            {
                index++;
                if (annotatedEdges[index].Source != annotatedEdges[assignedIndex].Source)
                    assignedIndex = index;
            }
            if (animation.Endings.HasMarkers)
            {
                var endingsOnly = new MermaidNativeEdgeSyntax.EdgeAnimation(Endings: animation.Endings,
                    Id: null, Step: 0, Animate: false, Speed: MermaidNativeEdgeSyntax.DashSpeed.Unspecified);
                for (var expanded = first; expanded <= index; expanded++)
                    matches.Add(expanded, endingsOnly);
            }
            if (animation.Id is not null)
                matches[assignedIndex] = animation;
        }
        return matches;
    }

    private static void AddEnding(XElement path, SKPathMeasure route, MermaidNativeEdgeSyntax.EdgeEnding ending,
        bool start, string stroke, string surface)
    {
        if (ending == MermaidNativeEdgeSyntax.EdgeEnding.None)
            return;
        const float radius = 4;
        var inset = Math.Min(radius, route.Length / 4);
        if (!route.GetPositionAndTangent(start ? inset : route.Length - inset, out var center, out var tangent))
            return;

        var ns = path.Name.Namespace;
        XElement marker;
        if (ending == MermaidNativeEdgeSyntax.EdgeEnding.Circle)
        {
            marker = new XElement(ns + "circle", new XAttribute("class", "edge-marker-circle"),
                new XAttribute("cx", Number(center.X)), new XAttribute("cy", Number(center.Y)),
                new XAttribute("r", Number(radius)), new XAttribute("fill", surface));
        }
        else
        {
            var diagonalA = new SKPoint((tangent.X - tangent.Y) * radius, (tangent.Y + tangent.X) * radius);
            var diagonalB = new SKPoint((tangent.X + tangent.Y) * radius, (tangent.Y - tangent.X) * radius);
            marker = new XElement(ns + "path", new XAttribute("class", "edge-marker-cross"),
                new XAttribute("d", $"M {Point(center - diagonalA)} L {Point(center + diagonalA)} M {Point(center - diagonalB)} L {Point(center + diagonalB)}"),
                new XAttribute("fill", "none"), new XAttribute("stroke-linecap", "round"));
        }
        marker.SetAttributeValue("stroke", stroke);
        marker.SetAttributeValue("stroke-width", "1.5");
        marker.SetAttributeValue("aria-hidden", "true");
        path.AddAfterSelf(marker);
    }

    private static string Point(SKPoint point) => $"{Number(point.X)} {Number(point.Y)}";

    private string RenderFrame(TimeSpan time, Int128 elapsedTicks, TimeSpan duration)
    {
        var seconds = Math.Clamp(time.TotalSeconds, 0, duration.TotalSeconds);
        var stepTime = seconds / _stepSeconds;
        lock (_gate)
        {
            foreach (var edge in _edges)
            {
                if (edge.Dash is { } dash)
                {
                    var dashProgress = (long)(elapsedTicks % dash.Duration.Ticks) / (double)dash.Duration.Ticks;
                    edge.Path.SetAttributeValue("stroke-dashoffset", Number(dash.Offset * (1 - dashProgress)));
                }

                if (edge.Dot is not { } dot)
                    continue;
                var progress = stepTime - (edge.Step - 1);
                var active = progress >= 0 && progress < 1;
                edge.Highlight!.SetAttributeValue("opacity", active ? "1" : "0");
                dot.SetAttributeValue("opacity", active ? "1" : "0");
                if (!active)
                    continue;

                var position = progress * (PathSampleCount - 1);
                var index = Math.Min((int)position, PathSampleCount - 2);
                var fraction = (float)(position - index);
                var from = edge.Samples[index];
                var to = edge.Samples[index + 1];
                dot.SetAttributeValue("cx", Number(from.X + (to.X - from.X) * fraction));
                dot.SetAttributeValue("cy", Number(from.Y + (to.Y - from.Y) * fraction));
            }
            return Serialize(_svg);
        }
    }

    private static SKPoint[] SampleRoute(SKPathMeasure measure)
    {
        var samples = new SKPoint[PathSampleCount];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = measure.GetPosition(measure.Length * index / (samples.Length - 1));
        return samples;
    }

    private static void AddBadge(XElement badges, SKPathMeasure route, int step, string stroke, string surface, bool hasLabel)
    {
        if (!route.GetPositionAndTangent(route.Length / 2, out var center, out var tangent))
            return;

        var offset = hasLabel ? 22 : 12;
        var x = center.X + tangent.Y * offset;
        var y = center.Y - tangent.X * offset;
        var label = step.ToString(CultureInfo.InvariantCulture);
        var width = 9 + label.Length * 6;
        var ns = badges.Name.Namespace;
        badges.Add(new XElement(ns + "g", new XAttribute("class", "edge-step-badge"),
            new XElement(ns + "rect",
                new XAttribute("x", Number(x - width / 2d)), new XAttribute("y", Number(y - 7)),
                new XAttribute("width", width), new XAttribute("height", 14), new XAttribute("rx", 7),
                new XAttribute("fill", surface), new XAttribute("stroke", stroke)),
            new XElement(ns + "text",
                new XAttribute("x", Number(x)), new XAttribute("y", Number(y + 3.5)),
                new XAttribute("text-anchor", "middle"), new XAttribute("font-size", "10"),
                new XAttribute("fill", stroke), label)));
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Serialize(XElement svg)
    {
        var frame = svg.ToString(SaveOptions.DisableFormatting);
        if (frame.Length > MermaiderSvgRenderer.MaximumSvgCharacters)
            throw new InvalidOperationException("The generated Mermaid edge animation exceeds the configured size limit.");
        return frame;
    }

    private readonly record struct DashMotion(double Offset, TimeSpan Duration);

    private sealed record FrameEdge(
        XElement Path, DashMotion? Dash, int Step, XElement? Highlight, XElement? Dot, SKPoint[] Samples);

    private readonly record struct EdgeKey(string From, string To, string? Label, EdgeStyle Style, bool ArrowStart, bool ArrowEnd)
    {
        public static EdgeKey FromSource(MermaidEdge edge) =>
            new(edge.Source, edge.Target, NormalizeAttributeLabel(edge.Label), edge.Style, edge.HasArrowStart, edge.HasArrowEnd);

        private static string? NormalizeAttributeLabel(string? label)
        {
            // XML parsers normalize literal whitespace in the SVG's data-label attribute.
            if (label is null || !label.AsSpan().ContainsAny('\r', '\n', '\t'))
                return label;
            return label.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }

        public static EdgeKey FromPath(XElement path) =>
            new((string?)path.Attribute("data-from") ?? "", (string?)path.Attribute("data-to") ?? "",
                (string?)path.Attribute("data-label"),
                (string?)path.Attribute("data-style") switch
                {
                    "dotted" => EdgeStyle.Dotted,
                    "thick" => EdgeStyle.Thick,
                    _ => EdgeStyle.Solid
                },
                (string?)path.Attribute("data-arrow-start") == "true",
                (string?)path.Attribute("data-arrow-end") == "true");
    }
}
