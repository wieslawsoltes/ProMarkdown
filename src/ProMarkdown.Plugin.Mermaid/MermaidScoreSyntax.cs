using System.Text;
using System.Text.RegularExpressions;
using Mermaider;
using Mermaider.Animate;

namespace ProMarkdown.Plugin.Mermaid;

internal sealed partial class MermaidScoreSyntax
{
    private const string Keyword = "animate";
    private const string BlockIdentifier = @"[\p{L}_][\p{L}\p{Nd}_-]*";
    private const string BlockLabel = """(?:\[(?:"[^"]*"|[^"\]]*)\]|\((?:"[^"]*"|[^")]*)\))""";
    private const string BlockSpan = @"(?::0*[1-9][0-9]*)";
    private readonly DiagramKind _kind;
    private readonly int _diagramBodyStart;
    private readonly int _firstCandidate;

    private MermaidScoreSyntax(DiagramKind kind, int diagramBodyStart, int firstCandidate, IReadOnlyList<Header> headers)
    {
        _kind = kind;
        _diagramBodyStart = diagramBodyStart;
        _firstCandidate = firstCandidate;
        Headers = headers;
    }

    public IReadOnlyList<Header> Headers { get; }

    public static MermaidScoreSyntax? Parse(string source)
    {
        if (!source.Contains(Keyword, StringComparison.Ordinal))
            return null;

        var header = MermaidNativeEdgeSyntax.ReadDiagramHeader(source, out var diagramBodyStart);
        var kind = GetDiagramKind(header);
        var allowsLabelAsFirstStatement = kind is DiagramKind.Mindmap or DiagramKind.Block or DiagramKind.TreeView;
        var allowsMultipleRootLabels = kind is DiagramKind.Block or DiagramKind.TreeView;
        var allowsBareNode = kind == DiagramKind.Flowchart || allowsLabelAsFirstStatement;
        var text = source.AsSpan();
        List<Header>? headers = null;
        var lineNumber = 0;
        var firstCandidate = -1;
        Header? unknownHeader = null;
        var bodyLineCount = 0;
        var hasDiagramStatements = false;
        var insideDescription = false;
        var groupDepth = 0;
        string? labelEnd = null;
        foreach (var range in text.Split('\n'))
        {
            lineNumber++;
            var start = range.Start.Value;
            var line = text[range];
            var startsCandidate = StartsCandidate(line);
            if (startsCandidate && firstCandidate < 0)
                firstCandidate = start;

            // Mermaider searches raw lines, but front matter and comments are not diagram statements.
            if (start < diagramBodyStart)
                continue;
            var content = line.Trim();
            if (content.IsEmpty || content.StartsWith("%%"))
                continue;

            bodyLineCount++;
            if (IsProtectedLine(content, kind, ref labelEnd, ref insideDescription))
                continue;

            if (kind is DiagramKind.Block or DiagramKind.Flowchart)
            {
                var opensGroup = kind == DiagramKind.Block
                    ? BlockGroupStart().IsMatch(content)
                    : content.Length > "subgraph".Length && FirstWord(content) is "subgraph";
                if (opensGroup)
                    groupDepth++;
                else if (groupDepth > 0 && content is "end")
                    groupDepth--;
            }

            if (startsCandidate && groupDepth == 0 && (hasDiagramStatements || !allowsLabelAsFirstStatement))
            {
                var suffix = GetHeaderOptions(line[Keyword.Length..].Trim(), kind, out var hasLabelAnnotation);
                if (kind == DiagramKind.Block && suffix.ContainsAny(':', '[', '('))
                    hasLabelAnnotation = BlockNodeRow().IsMatch(suffix);
                if (!hasLabelAnnotation && (suffix.IsEmpty || StartsWithScoreOption(suffix, kind)))
                {
                    var canBeLabel = suffix.IsEmpty ? allowsBareNode : allowsMultipleRootLabels;
                    var fallback = canBeLabel ? Fallback.UnrecognizedBody : Fallback.None;
                    headers ??= [];
                    if (headers.Count > 0)
                        headers[^1] = headers[^1] with { BodyLineCount = bodyLineCount - 1 };
                    headers.Add(new Header(start, lineNumber, fallback, canBeLabel && HasOnlyScoreOptions(suffix)));
                    bodyLineCount = 0;
                    if (!canBeLabel)
                        break;
                    continue;
                }
                if (!hasLabelAnnotation && unknownHeader is null && (kind != DiagramKind.Flowchart ||
                    !MermaidNativeEdgeSyntax.HasNodeContinuation(line, Keyword.Length)))
                    unknownHeader = new Header(start, lineNumber, Fallback.Diagram);
            }

            hasDiagramStatements = true;
            labelEnd = GetLabelEnd(content, kind);
        }

        if (headers is { Count: > 0 })
        {
            headers[^1] = headers[^1] with { BodyLineCount = bodyLineCount };
            if (headers.Count == 1 && headers[0].IsEmptyLabel)
                return null;
            return new MermaidScoreSyntax(kind, diagramBodyStart, firstCandidate, headers);
        }
        return unknownHeader is { } unknown
            ? new MermaidScoreSyntax(kind, diagramBodyStart, firstCandidate, [unknown])
            : null;
    }

    public string PrepareSource(string source, Header header)
    {
        if (header.Start == _firstCandidate)
            return source;

        // Mermaider uses the first unindented animate keyword. For mindmaps shift the
        // entire diagram body to preserve relative indentation between nodes.
        var preserveIndentation = _kind == DiagramKind.Mindmap;
        var rewriteStart = preserveIndentation ? Math.Min(_firstCandidate, _diagramBodyStart) : _firstCandidate;
        return IndentPrefix(source, rewriteStart, header.Start, preserveIndentation ? _diagramBodyStart : int.MaxValue);
    }

    internal static string ProtectScoreHeaders(string prefix) => IndentPrefix(prefix, 0, prefix.Length, int.MaxValue);

    private static string IndentPrefix(string source, int rewriteStart, int rewriteEnd, int preserveIndentationFrom)
    {
        StringBuilder? prepared = null;
        var offset = 0;
        var prefix = source.AsSpan(rewriteStart, rewriteEnd - rewriteStart);
        foreach (var range in prefix.Split('\n'))
        {
            var start = rewriteStart + range.Start.Value;
            var line = prefix[range];
            if (!StartsCandidate(line) && (start < preserveIndentationFrom || line.IsEmpty))
                continue;
            prepared ??= new StringBuilder(source.Length);
            prepared.Append(source.AsSpan(offset, start - offset)).Append(' ');
            offset = start;
        }
        return prepared is null ? source : prepared.Append(source.AsSpan(offset)).ToString();
    }

    public int? FindRejectedStatementLine(string source, int firstIndex, int endIndex, MermaidParseException error)
    {
        // Mermaider uses the first raw line matching the statement, including copies in labels or descriptions.
        var statement = error.SourceLine.AsSpan();
        var newline = statement.IndexOf('\n');
        if (newline >= 0)
            statement = statement[..newline];
        statement = statement.Trim();

        var first = Headers[firstIndex];
        var end = endIndex < Headers.Count ? Headers[endIndex].Start : source.Length;
        var candidates = source.AsSpan(first.Start, end - first.Start);
        var lineNumber = first.Line - 1;
        var insideDescription = false;
        string? labelEnd = null;
        foreach (var range in candidates.Split('\n'))
        {
            lineNumber++;
            var content = candidates[range].Trim();
            if (content.IsEmpty || content.StartsWith("%%") || IsProtectedLine(content, _kind, ref labelEnd, ref insideDescription))
                continue;
            if (lineNumber == error.Line || content.SequenceEqual(statement))
                return lineNumber;
            labelEnd = GetLabelEnd(content, _kind);
        }
        return null;
    }

    public int GetNextCandidateIndex(int index, Exception error, out bool requiresValidFlowchart)
    {
        requiresValidFlowchart = false;
        var header = Headers[index];
        if (header.Fallback == Fallback.None)
            return -1;
        if (error is MermaidParseException)
            return index + 1;
        if (error is not AnimationException animationError)
            return -1;
        if (header.Fallback == Fallback.Diagram)
            return index + 1;

        // The compiler parsed every later candidate as score body. Reuse its diagnostics
        // to skip labels while keeping any candidate with an accepted score statement.
        var rejectedLines = _kind == DiagramKind.Flowchart ? new HashSet<int>() : null;
        var remaining = Headers.Count - index;
        Span<DiagnosticEvidence> evidence = remaining <= 64
            ? stackalloc DiagnosticEvidence[remaining]
            : new DiagnosticEvidence[remaining];
        evidence.Clear();
        var candidate = index;
        foreach (var diagnostic in animationError.Diagnostics)
        {
            if (diagnostic.Line == header.Line && rejectedLines is not null && StepLimitDiagnostic().IsMatch(diagnostic.Message))
                continue;
            if (diagnostic.Line <= header.Line)
                return -1;
            if (diagnostic.Line < Headers[candidate].Line)
                candidate = FindHeader(index, candidate, diagnostic.Line);
            while (candidate + 1 < Headers.Count && diagnostic.Line >= Headers[candidate + 1].Line)
                candidate++;

            ref var candidateEvidence = ref evidence[candidate - index];
            if (diagnostic.Line == Headers[candidate].Line)
            {
                if (diagnostic.Message.StartsWith("Unknown verb 'animate'.", StringComparison.Ordinal))
                    candidateEvidence.HeaderWasUnrecognized = true;
                else
                    candidateEvidence.HasHeaderError = true;
                continue;
            }
            if (rejectedLines?.Add(diagnostic.Line) == true)
                candidateEvidence.RejectedBodyLines++;
            if (diagnostic.Message.StartsWith("Unknown verb '", StringComparison.Ordinal))
                candidateEvidence.UnrecognizedBodyLines++;
            else
                candidateEvidence.HasRecognizedBodyError = true;
        }

        if (!evidence[0].IsUnrecognizedBody(header.BodyLineCount))
        {
            if (rejectedLines is null || evidence[0].RejectedBodyLines != header.BodyLineCount)
                return -1;
            requiresValidFlowchart = true;
        }
        var next = index + 1;
        while (next < Headers.Count)
        {
            var examined = Headers[next];
            var observed = evidence[next - index];
            if (!examined.IsEmptyLabel)
            {
                if (!examined.HasOnlyOptions || !observed.HeaderWasUnrecognized || observed.HasHeaderError)
                    break;
                if (!observed.IsUnrecognizedBody(examined.BodyLineCount))
                {
                    if (rejectedLines is null || observed.RejectedBodyLines != examined.BodyLineCount)
                        break;
                    requiresValidFlowchart = true;
                }
            }
            next++;
        }
        return next;
    }

    private int FindHeader(int low, int high, int line)
    {
        while (high - low > 1)
        {
            var middle = low + (high - low) / 2;
            if (Headers[middle].Line <= line)
                low = middle;
            else
                high = middle;
        }
        return low;
    }

    private static bool StartsCandidate(ReadOnlySpan<char> line) =>
        line.StartsWith(Keyword, StringComparison.Ordinal) &&
        (line.Length == Keyword.Length || char.IsWhiteSpace(line[Keyword.Length]));

    [GeneratedRegex(@"\Ablock(?::[^\s:]+(?::\d+)?)?\z")]
    private static partial Regex BlockGroupStart();

    [GeneratedRegex(@"\A(?:" + BlockIdentifier + @"\s+)*" + BlockIdentifier +
        "(?:" + BlockLabel + BlockSpan + "?|" + BlockSpan + ")" +
        @"(?:\s*(?>" + BlockIdentifier + BlockLabel + "?" + BlockSpan + @"?))*\z", RegexOptions.None, 500)]
    private static partial Regex BlockNodeRow();

    [GeneratedRegex(@"\A:::[\w][\w-]*")]
    private static partial Regex TreeClassAnnotation();

    [GeneratedRegex(@"\Aicon\((?:(?:[\w-]*:)?[\w-]+)?\)")]
    private static partial Regex TreeIconAnnotation();

    [GeneratedRegex(@"""\s*\]")]
    private static partial Regex FlowchartLabelEnd();

    [GeneratedRegex(@"\AThe score has \d+ steps; the limit is \d+\.\z")]
    private static partial Regex StepLimitDiagnostic();

    private static DiagramKind GetDiagramKind(ReadOnlySpan<char> header)
    {
        if (MermaidNativeEdgeSyntax.IsFlowchartHeader(header))
            return DiagramKind.Flowchart;
        var kind = FirstWord(header);
        if (kind.Equals("mindmap", StringComparison.OrdinalIgnoreCase))
            return DiagramKind.Mindmap;
        if (kind.Equals("block", StringComparison.OrdinalIgnoreCase) || kind.Equals("block-beta", StringComparison.OrdinalIgnoreCase))
            return DiagramKind.Block;
        if (kind.Equals("treeView", StringComparison.OrdinalIgnoreCase) || kind.Equals("treeView-beta", StringComparison.OrdinalIgnoreCase))
            return DiagramKind.TreeView;
        if (kind.Equals("gantt", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("timeline", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("journey", StringComparison.OrdinalIgnoreCase))
            return DiagramKind.ColonDelimitedLabels;
        return DiagramKind.Other;
    }

    private static bool IsProtectedLine(ReadOnlySpan<char> content, DiagramKind kind, ref string? labelEnd, ref bool insideDescription)
    {
        if (labelEnd is not null)
        {
            var closesLabel = kind switch
            {
                DiagramKind.Block => content.Count('"') % 2 != 0,
                DiagramKind.TreeView => content.Contains('"'),
                DiagramKind.Flowchart => FlowchartLabelEnd().IsMatch(content),
                _ => content.EndsWith(labelEnd, StringComparison.Ordinal)
            };
            if (closesLabel)
                labelEnd = null;
            return true;
        }
        return MermaidAccessibilitySyntax.TryReadDirective(content, ref insideDescription, out _, out _);
    }

    private static string? GetLabelEnd(ReadOnlySpan<char> content, DiagramKind kind)
    {
        if (kind == DiagramKind.Flowchart)
            return content.Contains("[\"", StringComparison.Ordinal) && !FlowchartLabelEnd().IsMatch(content) ? "\"]" : null;
        if (kind == DiagramKind.TreeView)
        {
            // A quoted name may be preceded by the tree's box-drawing characters.
            var name = content.TrimStart("├└│─┣┗┃━┬┳ \t");
            return name.StartsWith('"') && !name[1..].Contains('"') ? "\"" : null;
        }
        if (kind == DiagramKind.Block)
        {
            return FirstWord(content) is not ("title" or "class" or "classDef" or "style") &&
                content.Count('"') % 2 != 0 ? "\"" : null;
        }
        if (kind != DiagramKind.Mindmap)
            return null;

        var opening = content.IndexOfAny("([{)");
        if (opening < 0 || content[..opening].ContainsAny('"', '`'))
            return null;
        var closing = content[opening] switch
        {
            '[' => "]",
            '(' => ")",
            '{' => "}}",
            _ => "("
        };
        return content.EndsWith(closing, StringComparison.Ordinal) ? null : closing;
    }

    private static bool StartsWithScoreOption(ReadOnlySpan<char> suffix, DiagramKind kind)
    {
        if (kind == DiagramKind.ColonDelimitedLabels && suffix.Contains(':'))
            return false;
        return IsScoreOption(FirstWord(suffix).Trim('"'));
    }

    private static ReadOnlySpan<char> GetHeaderOptions(ReadOnlySpan<char> suffix, DiagramKind kind, out bool hasLabelAnnotation)
    {
        hasLabelAnnotation = false;
        if (kind != DiagramKind.TreeView && !suffix.Contains("%%", StringComparison.Ordinal))
            return suffix;

        var quoted = false;
        for (var index = 0; index < suffix.Length; index++)
        {
            var current = suffix[index];
            if (quoted && current == '\\')
            {
                index++;
                continue;
            }
            if (current == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted)
                continue;
            if (current == '%' && index + 1 < suffix.Length && suffix[index + 1] == '%' &&
                (index == 0 || char.IsWhiteSpace(suffix[index - 1]) || suffix[index - 1] == '"'))
                return suffix[..index].TrimEnd();

            if (kind == DiagramKind.TreeView &&
                ((current == '#' && suffix[index..].StartsWith("##") && !suffix[(index + 2)..].Trim().IsEmpty) ||
                 (current == ':' && TreeClassAnnotation().IsMatch(suffix[index..])) ||
                 (current == 'i' && TreeIconAnnotation().IsMatch(suffix[index..]))))
            {
                hasLabelAnnotation = true;
                return suffix;
            }
        }
        return suffix;
    }

    private static bool HasOnlyScoreOptions(ReadOnlySpan<char> suffix)
    {
        while (!suffix.IsEmpty)
        {
            var word = FirstWord(suffix);
            suffix = suffix[word.Length..].TrimStart();
            if (word.Length >= 2 && word[0] == '"' && word[^1] == '"')
                word = word[1..^1];
            if (!IsScoreOption(word))
                return false;
        }
        return true;
    }

    private static bool IsScoreOption(ReadOnlySpan<char> word) =>
        word is "loop" or "once" or "scrub" or "calm" or "crisp" or "still" or "contents" or "compact";

    private static ReadOnlySpan<char> FirstWord(ReadOnlySpan<char> text)
    {
        var length = 0;
        while (length < text.Length && !char.IsWhiteSpace(text[length]))
            length++;
        return text[..length];
    }

    private enum DiagramKind
    {
        Other,
        Flowchart,
        Mindmap,
        Block,
        TreeView,
        ColonDelimitedLabels
    }

    internal enum Fallback
    {
        None,
        Diagram,
        UnrecognizedBody
    }

    internal readonly record struct Header(int Start, int Line, Fallback Fallback, bool HasOnlyOptions = false, int BodyLineCount = 0)
    {
        public bool IsEmptyLabel => HasOnlyOptions && BodyLineCount == 0;
    }

    private struct DiagnosticEvidence
    {
        public bool HeaderWasUnrecognized;
        public bool HasHeaderError;
        public bool HasRecognizedBodyError;
        public int UnrecognizedBodyLines;
        public int RejectedBodyLines;

        public readonly bool IsUnrecognizedBody(int bodyLineCount) =>
            !HasRecognizedBodyError && UnrecognizedBodyLines == bodyLineCount;
    }
}
