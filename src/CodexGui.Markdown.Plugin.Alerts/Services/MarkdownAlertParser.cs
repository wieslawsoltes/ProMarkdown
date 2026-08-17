using System.Text;
using System.Text.RegularExpressions;
using CodexGui.Markdown.Services;
using Markdig;
using Markdig.Extensions.Alerts;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Alerts;

internal static class MarkdownAlertParser
{
    private static readonly Lazy<MarkdownPipeline> Pipeline = new(() => new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation()
        .UseAdvancedExtensions()
        .Build());

    public static MarkdownAlertDocument Parse(AlertBlock alertBlock, MarkdownParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(alertBlock);
        ArgumentNullException.ThrowIfNull(parseResult);

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(alertBlock.Span);
        var source = sourceSpan.Slice(parseResult.OriginalMarkdown);
        if (string.IsNullOrWhiteSpace(source) && !parseResult.UsesOriginalSourceSpans)
        {
            source = sourceSpan.Slice(parseResult.ParsedMarkdown);
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            source = MarkdownAlertSyntax.BuildAlertBlock(alertBlock.Kind.ToString(), string.Empty);
        }

        return Parse(source, alertBlock.Kind.ToString());
    }

    public static MarkdownAlertDocument Parse(string? markdown)
    {
        return Parse(markdown, fallbackKind: null);
    }

    private static MarkdownAlertDocument Parse(string? markdown, string? fallbackKind)
    {
        var source = MarkdownAlertSyntax.NormalizeBlockText(markdown);
        List<MarkdownAlertDiagnostic> diagnostics = [];
        if (string.IsNullOrWhiteSpace(source))
        {
            diagnostics.Add(new MarkdownAlertDiagnostic("Alert content is empty.", MarkdownSourceSpan.Empty));
            return new MarkdownAlertDocument(
                string.Empty,
                MarkdownAlertSyntax.DefaultKind,
                MarkdownSourceSpan.Empty,
                string.Empty,
                MarkdownSourceSpan.Empty,
                diagnostics);
        }

        var document = Markdig.Markdown.Parse(source, Pipeline.Value);
        var alertBlocks = new List<AlertBlock>();
        foreach (var block in document)
        {
            switch (block)
            {
                case AlertBlock alert:
                    alertBlocks.Add(alert);
                    break;
                case BlankLineBlock:
                    break;
                default:
                    diagnostics.Add(new MarkdownAlertDiagnostic(
                        $"Unexpected block '{block.GetType().Name}' inside alert source.",
                        MarkdownSourceSpan.FromMarkdig(block.Span)));
                    break;
            }
        }

        if (alertBlocks.Count == 0)
        {
            diagnostics.Add(new MarkdownAlertDiagnostic(
                "Markdig did not produce an alert block from the provided markdown.",
                MarkdownSourceSpan.Empty));
        }
        else if (alertBlocks.Count > 1)
        {
            diagnostics.Add(new MarkdownAlertDiagnostic(
                "Expected a single alert block but parsed multiple alert blocks.",
                MarkdownSourceSpan.FromMarkdig(alertBlocks[1].Span)));
        }

        if (!MarkdownAlertSyntax.TryExtractHeaderKind(source, out var parsedKind, out _))
        {
            if (string.IsNullOrWhiteSpace(fallbackKind))
            {
                diagnostics.Add(new MarkdownAlertDiagnostic(
                    "Alert header is missing a valid `[!KIND]` marker.",
                    MarkdownSourceSpan.Empty));
            }

            parsedKind = fallbackKind;
        }

        var kind = MarkdownAlertSyntax.NormalizeKind(parsedKind ?? fallbackKind);
        var (bodyMarkdown, bodySpan) = ExtractBodyMarkdown(source, diagnostics);
        if (string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            diagnostics.Add(new MarkdownAlertDiagnostic("Alert body is empty.", bodySpan));
        }

        return new MarkdownAlertDocument(
            source,
            kind,
            new MarkdownSourceSpan(0, source.Length),
            bodyMarkdown,
            bodySpan,
            diagnostics);
    }

    private static (string Markdown, MarkdownSourceSpan Span) ExtractBodyMarkdown(string source, List<MarkdownAlertDiagnostic> diagnostics)
    {
        var normalized = MarkdownAlertSyntax.NormalizeLineEndings(source);
        var headerEnd = normalized.IndexOf('\n');
        if (headerEnd < 0 || headerEnd + 1 >= normalized.Length)
        {
            return (string.Empty, MarkdownSourceSpan.Empty);
        }

        var bodySource = normalized[(headerEnd + 1)..];
        var bodySpan = new MarkdownSourceSpan(headerEnd + 1, bodySource.Length);
        var lines = bodySource.Split('\n', StringSplitOptions.None);
        var builder = new StringBuilder(bodySource.Length);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!MarkdownAlertSyntax.TryUnwrapQuotedLine(line, out var unwrapped))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    diagnostics.Add(new MarkdownAlertDiagnostic(
                        "Alert body lines should remain quoted with `>` markers.",
                        bodySpan));
                }

                unwrapped = line.TrimStart();
            }

            builder.Append(unwrapped);
            if (index < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return (MarkdownAlertSyntax.NormalizeBodyMarkdown(builder.ToString()), bodySpan);
    }
}

internal static partial class MarkdownAlertSyntax
{
    public const string DefaultKind = "note";

    private static readonly MarkdownAlertKindOption[] KindOptions =
    [
        new("note", "Note", "General information or context."),
        new("info", "Info", "Reference information or additional context."),
        new("tip", "Tip", "Helpful guidance or a best practice."),
        new("success", "Success", "A positive outcome or confirmed result."),
        new("important", "Important", "High-priority guidance that should stand out."),
        new("warning", "Warning", "A potential issue that needs attention."),
        new("caution", "Caution", "Use care before continuing."),
        new("danger", "Danger", "A high-risk condition or breaking impact."),
        new("error", "Error", "A failure state or invalid result.")
    ];

    public static IReadOnlyList<MarkdownAlertKindOption> AvailableKinds => KindOptions;

    public static string NormalizeLineEndings(string? source)
    {
        return (source ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string NormalizeBlockText(string? source)
    {
        return NormalizeLineEndings(source).TrimEnd('\n');
    }

    public static string NormalizeInlineText(string? source)
    {
        return NormalizeLineEndings(source).Trim();
    }

    public static string NormalizeBodyMarkdown(string? source)
    {
        return NormalizeLineEndings(source).Trim('\n');
    }

    public static string NormalizeKind(string? kind, string? fallbackKind = null)
    {
        var normalized = NormalizeInlineText(kind)
            .Trim()
            .TrimStart('!')
            .ToLowerInvariant();

        return normalized.Length > 0 ? normalized : fallbackKind ?? DefaultKind;
    }

    public static MarkdownAlertKindOption ResolveKindOption(string? kind)
    {
        var normalized = NormalizeKind(kind);
        foreach (var option in KindOptions)
        {
            if (string.Equals(option.Value, normalized, StringComparison.Ordinal))
            {
                return option;
            }
        }

        var label = MarkdownCalloutRendering.FormatLabel(normalized);
        return new MarkdownAlertKindOption(normalized, string.IsNullOrWhiteSpace(label) ? "Alert" : label, "Alert block");
    }

    public static bool TryExtractHeaderKind(string source, out string? kind, out MarkdownSourceSpan span)
    {
        var normalized = NormalizeLineEndings(source);
        var header = normalized;
        var newlineIndex = normalized.IndexOf('\n');
        if (newlineIndex >= 0)
        {
            header = normalized[..newlineIndex];
        }

        var match = AlertHeaderRegex().Match(header);
        if (!match.Success)
        {
            kind = null;
            span = MarkdownSourceSpan.Empty;
            return false;
        }

        var kindGroup = match.Groups["kind"];
        kind = NormalizeKind(kindGroup.Value);
        span = new MarkdownSourceSpan(kindGroup.Index, kindGroup.Length);
        return true;
    }

    public static bool TryUnwrapQuotedLine(string line, out string content)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
        {
            content = string.Empty;
            return true;
        }

        if (!trimmed.StartsWith('>'))
        {
            content = line;
            return false;
        }

        var remainder = trimmed.Length > 1 ? trimmed[1..] : string.Empty;
        if (remainder.StartsWith(' '))
        {
            remainder = remainder[1..];
        }

        content = remainder;
        return true;
    }

    public static string BuildAlertBlock(string? kind, string? bodyMarkdown)
    {
        var normalizedKind = NormalizeKind(kind);
        var normalizedBody = NormalizeBodyMarkdown(bodyMarkdown);
        var builder = new StringBuilder();
        builder.Append("> [!").Append(normalizedKind.ToUpperInvariant()).Append(']');

        if (normalizedBody.Length == 0)
        {
            return builder.ToString();
        }

        var lines = NormalizeLineEndings(normalizedBody).Split('\n', StringSplitOptions.None);
        foreach (var line in lines)
        {
            builder.Append('\n');
            if (line.Length == 0)
            {
                builder.Append('>');
            }
            else
            {
                builder.Append("> ").Append(line);
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^\s*>\s*\[!(?<kind>[A-Za-z][A-Za-z0-9_-]*)\]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AlertHeaderRegex();
}
