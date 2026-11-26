using System.Diagnostics;
using System.Text;
using YamlDotNet.Core;

namespace ProMarkdown.Plugin.Mermaid;

internal readonly record struct MermaidFrontMatter(int Start, int Length, bool HasIndentedDelimiter, Range DelimiterIndent)
{
    public const double DefaultStepSeconds = 1;

    public (double StepSeconds, bool ShowStepNumbers, string? Title) Read(string source)
    {
        var frontMatter = source.AsSpan(Start, Length);
        if (frontMatter.IsWhiteSpace())
            return (DefaultStepSeconds, false, null);

        try
        {
            var document = MermaidYamlMapping.Parse(Dedent(frontMatter, source.AsSpan(DelimiterIndent)));
            var flowchart = document.ReadMapping("config").ReadMapping("flowchart");
            var stepSeconds = flowchart.ReadNumber("stepDuration") is > 0 and var duration ? duration : DefaultStepSeconds;
            var showStepNumbers = flowchart.ReadBoolean("stepNumbers") == true;
            var title = HasIndentedDelimiter ? document.FindScalar("title")?.Value : null;
            return (stepSeconds, showStepNumbers, title);
        }
        catch (YamlException)
        {
            // Mermaider handles the diagram's front matter independently of these optional playback settings.
            Trace.TraceWarning("Could not read Mermaid native edge settings from YAML front matter.");
            return (DefaultStepSeconds, false, null);
        }
    }

    private static string Dedent(ReadOnlySpan<char> frontMatter, ReadOnlySpan<char> indent)
    {
        if (indent.IsEmpty)
            return frontMatter.ToString();

        var result = new StringBuilder(frontMatter.Length);
        var offset = 0;
        foreach (var line in frontMatter.EnumerateLines())
        {
            result.Append(line.StartsWith(indent, StringComparison.Ordinal) ? line[indent.Length..] : line);
            offset += line.Length;
            if (offset < frontMatter.Length && frontMatter[offset] == '\r')
                result.Append(frontMatter[offset++]);
            if (offset < frontMatter.Length && frontMatter[offset] == '\n')
                result.Append(frontMatter[offset++]);
        }
        return result.ToString();
    }

    public string FormatForMermaider(string source, string? title)
    {
        // Mermaider reads only a single-line title and trims all candidate closing fences.
        var replacement = new StringBuilder();
        if (!string.IsNullOrEmpty(title))
            replacement.Append("title: \"").Append(title.TrimEnd().ReplaceLineEndings(" ")).Append('"');

        // Keep the authored line breaks for parser diagnostics and preserve the actual closing fence.
        foreach (var character in source.AsSpan(Start, Length))
        {
            if (character is '\r' or '\n')
                replacement.Append(character);
        }
        return replacement.ToString();
    }
}
