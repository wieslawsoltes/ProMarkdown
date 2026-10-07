using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace ProMarkdown.Plugin.Mermaid;

/// <summary>Flattens Mermaider's sRGB color mixes for the native SVG paint parser.</summary>
internal static class MermaidSvgColorResolver
{
    private const int MaximumExpressionDepth = 32;
    internal const int MaximumExpressionEvaluations = 65536;
    private const int MaximumProcessedCharacters = 64 * 1024 * 1024;
    private const int MaximumResolvedValueCharacters = 8 * 1024 * 1024;

    public static void Resolve(XElement root)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in root.Descendants())
        {
            if (element.Name.LocalName == "style")
                ReadSvgVariables(element.Value.AsSpan(), variables);
        }

        // Mermaider's tokens are document-wide; root inline values override the stylesheet.
        ReadVariables(((string?)root.Attribute("style")).AsSpan(), variables);
        var context = new ResolutionContext(variables);
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.LocalName == "style")
                element.Value = context.ResolveMixes(element.Value);

            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.LocalName is "fill" or "stroke" or "stop-color" or
                    "flood-color" or "lighting-color" or "color" or "style")
                {
                    attribute.Value = context.ResolveMixes(attribute.Value);
                }
            }
        }
    }

    private static void ReadSvgVariables(ReadOnlySpan<char> stylesheet, Dictionary<string, string> variables)
    {
        while (stylesheet.IndexOf('{') is var start && start >= 0)
        {
            var body = stylesheet[(start + 1)..];
            var end = body.IndexOf('}');
            if (end < 0)
                throw UnsupportedExpression();
            if (stylesheet[..start].Trim().SequenceEqual("svg"))
                ReadVariables(body[..end], variables);
            stylesheet = body[(end + 1)..];
        }
    }

    private static void ReadVariables(ReadOnlySpan<char> css, Dictionary<string, string> variables)
    {
        while (css.IndexOf("--", StringComparison.Ordinal) is var start && start >= 0)
        {
            css = css[start..];
            var nameLength = 2;
            while (nameLength < css.Length && (char.IsAsciiLetterOrDigit(css[nameLength]) || css[nameLength] is '-' or '_'))
                nameLength++;
            var tail = css[nameLength..].TrimStart();
            if (!tail.IsEmpty && tail[0] == ':')
            {
                tail = tail[1..];
                var end = tail.IndexOfAny(';', '}');
                if (end < 0)
                    end = tail.Length;
                variables[css[..nameLength].ToString()] = tail[..end].Trim().ToString();
                css = tail[end..];
            }
            else
            {
                css = css[nameLength..];
            }
        }
    }

    private sealed class ResolutionContext(Dictionary<string, string> variables)
    {
        private readonly Dictionary<string, Rgba> _resolvedColors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _resolvedValues = new(StringComparer.Ordinal);
        private int _remainingEvaluations = MaximumExpressionEvaluations;
        private int _remainingCharacters = MaximumProcessedCharacters;

        public string ResolveMixes(string value, int depth = 0, bool resolveVariables = false)
        {
            CheckDepth(depth);
            SpendCharacters(value.Length);
            var remaining = value.AsSpan();
            if (!resolveVariables && !remaining.Contains("color-mix(", StringComparison.OrdinalIgnoreCase))
                return value;
            var start = FindNextFunction(remaining);
            if (start < 0)
                return value;

            var result = new StringBuilder(value.Length);
            do
            {
                if (start > MaximumResolvedValueCharacters - result.Length)
                    throw WorkLimitExceeded();
                result.Append(remaining[..start]);
                remaining = remaining[start..];
                var end = FindFunctionEnd(remaining);
                var expression = remaining[..(end + 1)];
                var replacement = expression.StartsWith("var(", StringComparison.OrdinalIgnoreCase)
                    ? ResolveVariable(expression[4..^1], depth + 1)
                    : ParseColor(expression, depth).ToCss();
                SpendCharacters(replacement.Length);
                if (replacement.Length > MaximumResolvedValueCharacters - result.Length)
                    throw WorkLimitExceeded();
                result.Append(replacement);
                remaining = remaining[(end + 1)..];
                start = FindNextFunction(remaining);
            } while (start >= 0);
            if (remaining.Length > MaximumResolvedValueCharacters - result.Length)
                throw WorkLimitExceeded();
            return result.Append(remaining).ToString();
        }

        private string ResolveVariable(ReadOnlySpan<char> body, int depth)
        {
            SpendEvaluation(depth);
            var comma = FindComma(body);
            var name = (comma < 0 ? body : body[..comma]).Trim();
            if (_resolvedValues.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var resolved))
                return resolved;
            if (variables.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var value))
            {
                resolved = ResolveMixes(value, depth, resolveVariables: true);
                _resolvedValues.Add(name.ToString(), resolved);
                return resolved;
            }
            if (comma >= 0)
                return ResolveMixes(body[(comma + 1)..].Trim().ToString(), depth, resolveVariables: true);
            throw UnsupportedExpression();
        }

        private Rgba ParseColor(ReadOnlySpan<char> expression, int depth)
        {
            SpendEvaluation(depth);
            SpendCharacters(expression.Length);
            expression = expression.Trim();
            if (expression.StartsWith("var(", StringComparison.OrdinalIgnoreCase) && expression[^1] == ')')
            {
                var body = expression[4..^1];
                var comma = FindComma(body);
                var name = (comma < 0 ? body : body[..comma]).Trim();
                if (_resolvedColors.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var resolved))
                    return resolved;
                if (variables.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(name, out var value))
                {
                    resolved = ParseColor(value.AsSpan(), depth + 1);
                    _resolvedColors.Add(name.ToString(), resolved);
                    return resolved;
                }
                if (comma >= 0)
                    return ParseColor(body[(comma + 1)..], depth + 1);
            }
            else if (expression.StartsWith("color-mix(", StringComparison.OrdinalIgnoreCase) && expression[^1] == ')')
            {
                var body = expression[10..^1];
                var spaceComma = FindComma(body);
                if (spaceComma < 0 || !body[..spaceComma].Trim().Equals("in srgb", StringComparison.OrdinalIgnoreCase))
                    throw UnsupportedExpression();

                body = body[(spaceComma + 1)..];
                var colorComma = FindComma(body);
                if (colorComma < 0)
                    throw UnsupportedExpression();
                var first = body[..colorComma].Trim();
                var weightStart = first.LastIndexOf(' ');
                if (weightStart < 0 || first[^1] != '%' ||
                    !double.TryParse(first[(weightStart + 1)..^1], NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var percent) || percent is < 0 or > 100)
                    throw UnsupportedExpression();

                var a = ParseColor(first[..weightStart], depth + 1);
                var b = ParseColor(body[(colorComma + 1)..], depth + 1);
                var weight = percent / 100;
                var alpha = a.A * weight + b.A * (1 - weight);
                if (alpha == 0)
                    return default;
                return new Rgba(
                    (a.R * a.A * weight + b.R * b.A * (1 - weight)) / alpha,
                    (a.G * a.A * weight + b.G * b.A * (1 - weight)) / alpha,
                    (a.B * a.A * weight + b.B * b.A * (1 - weight)) / alpha,
                    alpha);
            }
            else if (expression.Length is 4 or 7 && expression[0] == '#' &&
                     uint.TryParse(expression[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            {
                return expression.Length == 4
                    ? new Rgba(((rgb >> 8) & 15) * 17, ((rgb >> 4) & 15) * 17, (rgb & 15) * 17, 1)
                    : new Rgba((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255, 1);
            }
            else if (expression.Equals("black", StringComparison.OrdinalIgnoreCase))
                return new Rgba(0, 0, 0, 1);
            else if (expression.Equals("white", StringComparison.OrdinalIgnoreCase))
                return new Rgba(255, 255, 255, 1);
            else if (expression.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                return default;

            throw UnsupportedExpression();
        }

        private void SpendEvaluation(int depth)
        {
            CheckDepth(depth);
            if (--_remainingEvaluations < 0)
                throw WorkLimitExceeded();
        }

        private void SpendCharacters(int count)
        {
            if (count > _remainingCharacters)
                throw WorkLimitExceeded();
            _remainingCharacters -= count;
        }

        private static void CheckDepth(int depth)
        {
            if (depth >= MaximumExpressionDepth)
                throw new InvalidOperationException("The Mermaid SVG color expression is cyclic or too deeply nested.");
        }
    }

    private static InvalidOperationException WorkLimitExceeded() =>
        new("The Mermaid SVG color resolution exceeds the configured work limit.");

    private static int FindNextFunction(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if ((value[index] is 'v' or 'V' && value[index..].StartsWith("var(", StringComparison.OrdinalIgnoreCase)) ||
                (value[index] is 'c' or 'C' && value[index..].StartsWith("color-mix(", StringComparison.OrdinalIgnoreCase)))
                return index;
        }
        return -1;
    }

    private static int FindComma(ReadOnlySpan<char> value)
    {
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '(': depth++; break;
                case ')': depth--; break;
                case ',' when depth == 0: return index;
            }
        }
        return -1;
    }

    private static int FindFunctionEnd(ReadOnlySpan<char> value)
    {
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '(')
                depth++;
            else if (value[index] == ')' && --depth == 0)
                return index;
        }
        throw UnsupportedExpression();
    }

    private static InvalidOperationException UnsupportedExpression() =>
        new("The Mermaid SVG contains an unsupported native color-mix expression.");

    private readonly record struct Rgba(double R, double G, double B, double A)
    {
        public string ToCss()
        {
            var r = (int)Math.Round(R, MidpointRounding.AwayFromZero);
            var g = (int)Math.Round(G, MidpointRounding.AwayFromZero);
            var b = (int)Math.Round(B, MidpointRounding.AwayFromZero);
            return A == 1
                ? $"#{r:X2}{g:X2}{b:X2}"
                : string.Create(CultureInfo.InvariantCulture, $"rgba({r},{g},{b},{A:0.######})");
        }
    }
}
