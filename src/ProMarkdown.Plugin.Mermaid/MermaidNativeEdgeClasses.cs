using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ProMarkdown.Plugin.Mermaid;

internal sealed partial class MermaidNativeEdgeClasses
{
    private const string Identifier = @"[\w][\w-]*";
    private const string IdentifierList = Identifier + @"(?:[ \t]*,[ \t]*" + Identifier + ")*";
    private const string Target = MermaidNativeEdgeSyntax.EdgeIdentifier;
    private const string TargetList = Target + @"(?:[ \t]*,[ \t]*" + Target + ")*";
    private readonly Dictionary<string, Style> _classes = new(StringComparer.Ordinal);
    private readonly List<(int Start, int Length, string[] Names)> _nonLegacyDefinitions = [];
    private readonly List<(int Start, int Length, string Targets, string Name, bool IsInline)> _assignments = [];

    [GeneratedRegex(@"\AclassDef[ \t]+(?<names>" + IdentifierList + @")[ \t]+(?<style>.+)\z")]
    private static partial Regex ClassDefinition();

    [GeneratedRegex(@"\A(?:class[ \t]+(?<ids>" + TargetList + @")[ \t]+|(?<ids>" + Target +
        @")(?<inline>:::))(?<name>" + Identifier + @")\z")]
    private static partial Regex ClassAssignment();

    [GeneratedRegex(@"\A\s*(?<name>[\w-]+)\s*:\s*|(?<!\\),\s*(?<name>[\w-]+)\s*:\s*")]
    private static partial Regex StyleDeclaration();

    public void Read(ReadOnlySpan<char> statement, int start, int length)
    {
        var source = statement.ToString();
        var definition = ClassDefinition().Match(source);
        if (definition.Success)
        {
            var names = definition.Groups["names"].Value.Split(',', StringSplitOptions.TrimEntries);
            var style = ReadStyle(definition.Groups["style"].Value);
            foreach (var name in names)
            {
                _classes[name] = _classes.TryGetValue(name, out var existing)
                    ? existing.WithOverrides(style)
                    : style;
            }
            if (names.Length > 1 || names[0].Contains('-'))
                _nonLegacyDefinitions.Add((start, length, names));
        }
        else if (ClassAssignment().Match(source) is { Success: true } assignment)
        {
            _assignments.Add((start, length, assignment.Groups["ids"].Value, assignment.Groups["name"].Value,
                assignment.Groups["inline"].Success));
        }
    }

    public IEnumerable<ClassEdit> Apply(
        IReadOnlyDictionary<string, int> edgeDeclarations,
        Dictionary<string, MermaidNativeEdgeSyntax.EdgeAnimation> animations)
    {
        if (_assignments.Count == 0)
            yield break;

        var edgeStyles = new Dictionary<string, Style>(StringComparer.Ordinal);
        var usedClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in _assignments)
        {
            var hasStyle = _classes.TryGetValue(assignment.Name, out var style);
            var targets = assignment.Targets.Split(',', StringSplitOptions.TrimEntries);
            var hasNamedEdgeTarget = false;
            foreach (var target in targets)
            {
                if (!edgeDeclarations.TryGetValue(target, out var declaration))
                    continue;
                hasNamedEdgeTarget = true;
                if (hasStyle && declaration < assignment.Start)
                    edgeStyles[target] = edgeStyles.GetValueOrDefault(target).WithOverrides(style);
            }
            if (!hasNamedEdgeTarget)
                continue;

            usedClasses.Add(assignment.Name);
            if (assignment.IsInline)
            {
                if (targets[0].Contains('.'))
                    yield return new ClassEdit(assignment.Start, assignment.Length, null, StandaloneNodeId: targets[0]);
                continue;
            }
            if (assignment.Name.Contains('-'))
            {
                // Older Mermaider parsers only accept word characters in class names.
                // Source-defined CSS is already stripped by the plugin's strict styling policy.
                yield return new ClassEdit(assignment.Start, assignment.Length, null);
            }
            else if (assignment.Targets.Contains('.'))
            {
                // Mermaider cannot assign classes to IDs with periods. Edge styles have already been collected.
                var remaining = targets.Where(target => !target.Contains('.') || !edgeDeclarations.ContainsKey(target));
                var targetList = string.Join(',', remaining);
                yield return new ClassEdit(assignment.Start, assignment.Length,
                    targetList.Length == 0 ? null : $"class {targetList} {assignment.Name}");
            }
            else if (assignment.Targets.AsSpan().ContainsAny(' ', '\t'))
            {
                yield return new ClassEdit(assignment.Start, assignment.Length,
                    $"class {string.Join(',', targets)} {assignment.Name}");
            }
        }

        foreach (var definition in _nonLegacyDefinitions)
        {
            if (definition.Names.Any(usedClasses.Contains))
                yield return new ClassEdit(definition.Start, definition.Length, null);
        }

        foreach (var (edgeId, style) in edgeStyles)
        {
            if (!style.UnsupportedAnimation && (style.DashArray is not null || style.DashOffset.HasValue || style.Duration.HasValue))
            {
                var animation = animations.GetValueOrDefault(edgeId);
                animations[edgeId] = animation with { ClassDash = new MermaidEdgeDash(style.DashArray, style.DashOffset, style.Duration) };
            }
        }
    }

    private static Style ReadStyle(string source)
    {
        var style = default(Style);
        for (var declaration = StyleDeclaration().Match(source); declaration.Success;)
        {
            var next = declaration.NextMatch();
            var valueStart = declaration.Index + declaration.Length;
            var value = source.AsSpan(valueStart, (next.Success ? next.Index : source.Length) - valueStart).Trim();
            var name = declaration.Groups["name"].ValueSpan;
            if (name.Equals("stroke-dasharray", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadDashArray(value) is { } array)
                    style = style with { DashArray = array };
            }
            else if (name.Equals("stroke-dashoffset", StringComparison.OrdinalIgnoreCase) && TryReadLength(value, out var offset))
            {
                style = style with { DashOffset = offset };
            }
            else if (name.Equals("animation", StringComparison.OrdinalIgnoreCase))
            {
                var duration = ReadDashDuration(value);
                style = style with
                {
                    Duration = duration ?? TimeSpan.Zero,
                    UnsupportedAnimation = duration is null
                };
            }
            declaration = next;
        }
        return style;
    }

    private static string? TryReadDashArray(ReadOnlySpan<char> value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return "none";

        // Mermaid's escaped commas delimit lengths inside a single style value.
        var unescaped = value.Contains('\\') ? value.ToString().Replace(@"\,", ",", StringComparison.Ordinal) : null;
        var lengths = unescaped is null ? value : unescaped.AsSpan();
        var result = new StringBuilder();
        var hasPositiveLength = false;
        foreach (var range in lengths.SplitAny(", \t\r\n"))
        {
            var item = lengths[range];
            if (item.IsEmpty)
                continue;
            if (!TryReadLength(item, out var length) || length < 0)
                return null;
            hasPositiveLength |= length > 0;
            if (result.Length > 0)
                result.Append(' ');
            result.Append(length.ToString("R", CultureInfo.InvariantCulture));
        }
        if (result.Length == 0)
            return null;
        return hasPositiveLength ? result.ToString() : "none";
    }

    private static bool TryReadLength(ReadOnlySpan<char> value, out double length)
    {
        if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            value = value[..^2];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out length) &&
            Math.Abs(length) <= float.MaxValue;
    }

    private static TimeSpan? ReadDashDuration(ReadOnlySpan<char> value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.Zero;

        var dash = false;
        var linear = false;
        var infinite = false;
        TimeSpan? duration = null;
        foreach (var range in value.SplitAny(" \t\r\n"))
        {
            var token = value[range];
            if (token.IsEmpty)
                continue;
            if (!dash && token is "dash")
                dash = true;
            else if (!linear && token.Equals("linear", StringComparison.OrdinalIgnoreCase))
                linear = true;
            else if (!infinite && token.Equals("infinite", StringComparison.OrdinalIgnoreCase))
                infinite = true;
            else if (!duration.HasValue && TryReadDuration(token, out var parsed))
                duration = parsed;
            else
                return null;
        }
        return dash && linear && infinite ? duration : null;
    }

    private static bool TryReadDuration(ReadOnlySpan<char> value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var milliseconds = value.EndsWith("ms", StringComparison.OrdinalIgnoreCase);
        if (!value.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            !double.TryParse(value[..^(milliseconds ? 2 : 1)], NumberStyles.Float, CultureInfo.InvariantCulture, out var units))
            return false;

        var seconds = milliseconds ? units / 1000 : units;
        if (!(seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds))
            return false;
        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }

    public readonly record struct ClassEdit(int Start, int Length, string? Replacement, string? StandaloneNodeId = null);
    private readonly record struct Style(string? DashArray, double? DashOffset, TimeSpan? Duration, bool UnsupportedAnimation)
    {
        public Style WithOverrides(Style other) => new(
            other.DashArray ?? DashArray, other.DashOffset ?? DashOffset, other.Duration ?? Duration,
            other.Duration.HasValue ? other.UnsupportedAnimation : UnsupportedAnimation);
    }
}

internal sealed record MermaidEdgeDash(string? Array, double? Offset, TimeSpan? Duration);
