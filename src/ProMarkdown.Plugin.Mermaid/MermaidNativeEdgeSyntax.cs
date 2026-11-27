using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;

namespace ProMarkdown.Plugin.Mermaid;

/// <summary>Extracts Mermaid edge IDs and animation properties before handing the flowchart to Mermaider.</summary>
internal sealed partial class MermaidNativeEdgeSyntax
{
    internal const string EdgeIdentifier = @"[\w.][\w.-]*";
    private const string MarkerPrefix = "__promarkdown_edge_";
    private const string InlineEdgeLabel = """
        (?>[ \t]*)(?<inlineLabel>
            (?<quotedLabel>"[^"]*")[ \t]*
          | (?>\#(?:[A-Za-z][A-Za-z0-9]*|[0-9]+);|[^;\r\n])+?
        )
        """;

    [GeneratedRegex(@"@[ \t]*(?:[ox<]?(?:--|==|-\.)|~{3})")]
    internal static partial Regex EdgeIdCandidate();

    [GeneratedRegex(@"\G[ \t]*(?<id>" + EdgeIdentifier + ")@")]
    private static partial Regex EdgeIdAfterNode();

    [GeneratedRegex(@"\G\s*(?<id>[\w.]+(?:-\w[\w.]*)*)")]
    private static partial Regex NodeReference();

    [GeneratedRegex(@"\G:::[\w][\w-]*")]
    private static partial Regex NodeClass();

    [GeneratedRegex(@"\A(?:flowchart|graph)(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FlowchartHeader();

    [GeneratedRegex(@"\A(?<id>" + EdgeIdentifier + @")@\s*\{")]
    private static partial Regex PropertyBlockStart();

    // Prefer a complete operator so a node or a pipe label cannot become an inline label in a chain.
    [GeneratedRegex($$"""
        \G\s*(?:
            (?<operator>[ox<]?(?:-{2,}[>ox]|-{3,}|-\.+-[>ox]?|={2,}[>ox]|={3,}|~{3,}))
            (?<pipeLabel>\s*\|(?<pipeText>(?<quotedPipeText>"[^"]*")|[^|]*)\|)?
              | (?<prefix>[ox<]?--){{InlineEdgeLabel}}(?<suffix>-{2,}[-ox>])
              | (?<prefix>[ox<]?==){{InlineEdgeLabel}}(?<suffix>={2,}[=ox>])
              | (?<prefix>[ox<]?\-\.){{InlineEdgeLabel}}(?<suffix>\.+-[>ox]?)
        )
        """,
        RegexOptions.IgnorePatternWhitespace, 500)]
    private static partial Regex EdgeOperator();

    private MermaidNativeEdgeSyntax(
        MermaidSourceNormalization renderSource,
        MermaidSourceNormalization? structuralSource,
        MermaidSourceNormalization? probeSource,
        Dictionary<string, EdgeAnimation> animations,
        double stepSeconds,
        bool showStepNumbers)
    {
        RenderSource = renderSource;
        StructuralSource = structuralSource ?? renderSource;
        ProbeSource = probeSource;
        Animations = animations;
        StepSeconds = stepSeconds;
        ShowStepNumbers = showStepNumbers;
    }

    public string Source => RenderSource.Source;
    public MermaidSourceNormalization RenderSource { get; }
    public MermaidSourceNormalization StructuralSource { get; }
    public MermaidSourceNormalization? ProbeSource { get; }
    public IReadOnlyDictionary<string, EdgeAnimation> Animations { get; }
    public double StepSeconds { get; }
    public bool ShowStepNumbers { get; }

    public static MermaidNativeEdgeSyntax? Parse(string source, bool enableAnimation, CancellationToken cancellationToken)
    {
        var header = ReadDiagramHeader(source, out var bodyStart, out var frontMatter);
        if (!IsFlowchartHeader(header))
            return null;

        var (stepSeconds, showStepNumbers, title) = frontMatter.Read(source);
        var markerPrefix = source.Contains(MarkerPrefix, StringComparison.Ordinal)
            ? MarkerPrefix + Guid.NewGuid().ToString("N")
            : MarkerPrefix;

        var edits = new List<SourceEdit>();
        if (frontMatter.HasIndentedDelimiter)
        {
            edits.Add(new SourceEdit(frontMatter.Start, frontMatter.Length, frontMatter.Length,
                frontMatter.FormatForMermaider(source, title)));
        }
        if (bodyStart > 0 && source[bodyStart - 1] == ';')
            edits.Add(new SourceEdit(bodyStart - 1, 1, 1, "\n"));

        var definitions = new List<(int Start, int Length, string Id, string Properties)>();
        var edgeDeclarations = new Dictionary<string, int>(StringComparer.Ordinal);
        Stack<string?>? openSubgraphs = null;
        Dictionary<string, int>? subgraphDeclarations = null;
        MermaidNativeEdgeClasses? classes = null;
        var hasAccessibility = false;
        var hasCustomEndings = false;
        string? descriptionPercentMarker = null;
        foreach (var (start, length, isAccessibility, bracedContentStart) in GetStatements(source, bodyStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isAccessibility)
            {
                hasAccessibility = true;
                var directive = source.AsSpan(start, length);
                // Mermaider expects braced descriptions to open and close on their own lines.
                string? replacement = null;
                if (bracedContentStart > 0 && directive.EndsWith('}'))
                {
                    var content = directive[bracedContentStart..^1];
                    var percentMarker = MermaidAccessibilitySyntax.GetPercentMarker(content, source, ref descriptionPercentMarker);
                    replacement = MermaidAccessibilitySyntax.FormatBracedDescription(content, percentMarker);
                }
                edits.Add(new SourceEdit(start, length, length, replacement, IsAccessibility: true));
                continue;
            }

            // Keep statement boundaries visible to Mermaider even when a statement is removed or rewritten.
            var statementEnd = start + length;
            if (statementEnd < source.Length && source[statementEnd] == ';')
                edits.Add(new SourceEdit(statementEnd, 1, 1, "\n"));

            var untrimmed = source.AsSpan(start, length).TrimStart();
            var statement = untrimmed.TrimEnd();
            if (statement.IsEmpty || statement.StartsWith("%%"))
                continue;
            const string subgraphKeyword = "subgraph";
            if (statement.StartsWith(subgraphKeyword, StringComparison.Ordinal) &&
                (statement.Length == subgraphKeyword.Length || char.IsWhiteSpace(statement[subgraphKeyword.Length])))
            {
                (openSubgraphs ??= new()).Push(ReadSubgraphId(statement[subgraphKeyword.Length..]));
                continue;
            }
            if (statement is "end" && openSubgraphs?.TryPop(out var subgraphId) == true)
            {
                if (subgraphId is not null)
                    (subgraphDeclarations ??= new(StringComparer.Ordinal)).TryAdd(subgraphId, start);
                continue;
            }
            if (statement.StartsWith("class ", StringComparison.Ordinal) ||
                statement.StartsWith("classDef ", StringComparison.Ordinal) ||
                statement.StartsWith("class\t", StringComparison.Ordinal) ||
                statement.StartsWith("classDef\t", StringComparison.Ordinal))
            {
                (classes ??= new MermaidNativeEdgeClasses()).Read(statement, start, length);
                continue;
            }
            var hasAtSign = statement.Contains('@');
            if (!hasAtSign)
            {
                if (statement.Contains(":::", StringComparison.Ordinal))
                    (classes ??= new MermaidNativeEdgeClasses()).Read(statement, start, length);
                if (!statement.ContainsAny('-', '=', '~'))
                    continue;
            }

            var statementStart = start + length - untrimmed.Length;
            var value = statement.ToString();
            if (hasAtSign && value.EndsWith('}') && PropertyBlockStart().Match(value) is { Success: true } properties &&
                MermaidFlowchartMetadata.FindBlockEnd(value.AsSpan(properties.Length)) == value.Length - properties.Length - 1)
            {
                definitions.Add((start, length, properties.Groups["id"].Value, value[properties.Length..^1]));
                continue;
            }

            foreach (var (_, _, arrow, namedLink) in ReadNodeReferences(value))
            {
                if (!arrow.Success)
                    continue;

                var (replacement, endings) = AdaptArrow(arrow);
                if (namedLink is null && replacement is null)
                    continue;

                var id = namedLink?.Value;
                var linkStart = namedLink?.Index ?? arrow.Index;
                var declaration = statementStart + linkStart;
                if (id is not null)
                    edgeDeclarations.TryAdd(id, declaration);

                var operatorToken = arrow.Groups["operator"];
                var operatorStart = statementStart + (operatorToken.Success ? operatorToken.Index : arrow.Groups["prefix"].Index);
                hasCustomEndings |= endings.HasMarkers;
                edits.Add(new SourceEdit(declaration, arrow.Index - linkStart, arrow.Index + arrow.Length - linkStart,
                    replacement, Link: new LinkEdit(id, endings, operatorStart)));
            }
        }

        if (edgeDeclarations.Count == 0)
            return null;

        var propertiesById = new Dictionary<string, EdgeAnimation>(StringComparer.Ordinal);
        Dictionary<string, AdaptedNode>? standaloneNodes = null;
        void RegisterStandaloneNode(string id, int start, int length)
        {
            standaloneNodes ??= new(StringComparer.Ordinal);
            if (standaloneNodes.TryGetValue(id, out var existing))
            {
                if (start < existing.Start)
                    standaloneNodes[id] = existing with { Start = start };
            }
            else
            {
                var nodeId = id.Contains('.')
                    ? markerPrefix + "node_" + standaloneNodes.Count.ToString(CultureInfo.InvariantCulture)
                    : id;
                standaloneNodes.Add(id, new AdaptedNode(nodeId, start));
            }
            edits.Add(new SourceEdit(start, length, length, null, StandaloneNodeId: id));
        }

        List<NodeAppearanceUpdate>? appearanceUpdates = null;
        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Mermaid registers a subgraph at its closing end and resolves its metadata first.
            if (subgraphDeclarations?.TryGetValue(definition.Id, out var subgraphEnd) == true && subgraphEnd < definition.Start)
            {
                edits.Add(new SourceEdit(definition.Start, definition.Length, definition.Length, null));
                continue;
            }
            if (!edgeDeclarations.TryGetValue(definition.Id, out var declaration))
                continue;

            if (definition.Start < declaration)
            {
                // Metadata for a future edge is a standalone node in Mermaid.
                RegisterStandaloneNode(definition.Id, definition.Start, definition.Length);
                var appearance = MermaidFlowchartMetadata.ReadNodeAppearance(definition.Properties);
                if (appearance != default)
                    (appearanceUpdates ??= []).Add(new NodeAppearanceUpdate(definition.Id, definition.Start, appearance));
                continue;
            }

            propertiesById.TryGetValue(definition.Id, out var previous);
            propertiesById[definition.Id] = ReadProperties(definition.Properties, previous);
            edits.Add(new SourceEdit(definition.Start, definition.Length, definition.Length, null));
        }
        if (classes is not null)
        {
            foreach (var edit in classes.Apply(edgeDeclarations, propertiesById))
            {
                if (edit.StandaloneNodeId is { } id)
                    RegisterStandaloneNode(id, edit.Start, edit.Length);
                else
                    edits.Add(new SourceEdit(edit.Start, edit.Length, edit.Length, edit.Replacement));
            }
        }
        var declarations = standaloneNodes is null ? null :
            AddNodeReferenceEdits(source, bodyStart, standaloneNodes, appearanceUpdates ?? [], edits, cancellationToken);

        edits.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        var stripped = new StringBuilder(source.Length);
        var needsProbe = enableAnimation || showStepNumbers || hasCustomEndings ||
            propertiesById.Values.Any(static edge => edge.ClassDash is not null);
        var probe = needsProbe ? new StringBuilder(source.Length) : null;
        // The SVG renderer consumes accessibility directives. Mermaider's structural parser still sees them.
        var structural = hasAccessibility && needsProbe ? new StringBuilder(source.Length) : null;
        var renderChanges = new List<MermaidSourceChange>(edits.Count);
        var probeChanges = probe is null ? null : new List<MermaidSourceChange>(edits.Count);
        var structuralChanges = structural is null ? null : new List<MermaidSourceChange>(edits.Count);
        var markers = new Dictionary<string, EdgeAnimation>(StringComparer.Ordinal);
        var offset = 0;
        foreach (var edit in edits)
        {
            var unedited = source.AsSpan(offset, edit.Start - offset);
            stripped.Append(unedited);
            probe?.Append(unedited);
            structural?.Append(unedited);
            var renderLength = stripped.Length;
            var probeLength = probe?.Length ?? 0;
            var structuralLength = structural?.Length ?? 0;
            var hasProbeMarker = false;
            if (edit.IsAccessibility)
            {
                if (edit.Replacement is { } replacementDirective)
                    stripped.Append(replacementDirective).Append('\n');
                else
                    stripped.Append(source.AsSpan(edit.Start, edit.Length));
            }
            else if (edit.Link is { } link)
            {
                var arrow = edit.Replacement is { } replacementArrow ? replacementArrow.AsSpan() :
                    source.AsSpan(edit.Start + edit.Length, edit.ProbeLength - edit.Length);
                stripped.Append(arrow);
                structural?.Append(arrow);
                var animation = link.Id is { } id && edgeDeclarations[id] == edit.Start
                    ? propertiesById.GetValueOrDefault(id) with { Id = id }
                    : default;
                if (probe is not null &&
                    (link.Endings.HasMarkers || animation.ClassDash is not null || animation.Step > 0 && (enableAnimation || showStepNumbers) ||
                     enableAnimation && animation.HasDashes))
                {
                    var marker = markerPrefix + markers.Count.ToString(CultureInfo.InvariantCulture);
                    markers.Add(marker, animation with { Endings = link.Endings });
                    // Mermaider's parser identifies the exact expanded edges in chains and node groups.
                    // The marker is only used in the parse probe, never in the laid-out diagram.
                    probe.Append(" -->|").Append(marker).Append("| ");
                    hasProbeMarker = true;
                }
                else
                {
                    probe?.Append(arrow);
                }
            }
            else
            {
                var replacement = edit.Replacement;
                if (edit.StandaloneNodeId is { } nodeId)
                {
                    var node = standaloneNodes![nodeId];
                    replacement = edit.Start == node.Start ? declarations![nodeId] : node.Id;
                }
                stripped.Append(replacement);
                probe?.Append(replacement);
                structural?.Append(replacement);
            }

            var preservesArrow = edit.Link is not null && edit.Replacement is null;
            RecordSourceChange(renderChanges, edit, stripped.Length - renderLength, preservesArrow);
            RecordSourceChange(structuralChanges, edit, (structural?.Length ?? 0) - structuralLength, preservesArrow);
            RecordSourceChange(probeChanges, edit, (probe?.Length ?? 0) - probeLength, preservesArrow && !hasProbeMarker);
            offset = edit.Start + edit.ProbeLength;
        }
        var tail = source.AsSpan(offset);
        stripped.Append(tail);
        probe?.Append(tail);
        structural?.Append(tail);
        return new MermaidNativeEdgeSyntax(
            new(stripped.ToString(), source, renderChanges, descriptionPercentMarker),
            markers.Count > 0 && structural is not null ? new(structural.ToString(), source, structuralChanges) : null,
            markers.Count > 0 ? new(probe!.ToString(), source, probeChanges) : null, markers, stepSeconds, showStepNumbers);
    }

    private static void RecordSourceChange(List<MermaidSourceChange>? changes, SourceEdit edit, int generatedLength, bool preservesArrow)
    {
        if (changes is null)
            return;

        if (edit.Link is not { } link)
        {
            changes.Add(new(edit.Start, edit.ProbeLength, generatedLength));
            return;
        }

        if (edit.Length > 0)
            changes.Add(new(edit.Start, edit.Length, 0));
        // Rewritten glyphs and labels can change order. Their diagnostics point to the authored operator.
        if (!preservesArrow)
            changes.Add(new(edit.Start + edit.Length, edit.ProbeLength - edit.Length, generatedLength, link.OperatorStart));
    }

    private static (string? Replacement, EdgeEndings Endings) AdaptArrow(Match arrow)
    {
        var inlineLabel = arrow.Groups["inlineLabel"];
        var op = arrow.Groups["operator"].ValueSpan;
        ReadOnlySpan<char> label = arrow.Groups["pipeLabel"].ValueSpan;
        var labelText = inlineLabel.Success ? inlineLabel.ValueSpan : arrow.Groups["pipeText"].ValueSpan;
        var isMultiline = labelText.ContainsAny('\r', '\n');
        if (inlineLabel.Success)
        {
            var prefix = arrow.Groups["prefix"].ValueSpan;
            var suffix = arrow.Groups["suffix"].ValueSpan;
            var lead = prefix[0] is 'o' or 'x' or '<' ? prefix[..1] : [];
            var stroke = prefix.EndsWith('.') ? "-" : "";
            op = string.Concat(lead, stroke, suffix);
        }

        if (inlineLabel.Success || isMultiline)
        {
            var quoted = arrow.Groups[inlineLabel.Success ? "quotedLabel" : "quotedPipeText"].Success;
            var trimmed = labelText.Trim();
            var text = isMultiline && quoted && trimmed.Length >= 4 && trimmed.StartsWith("\"`") && trimmed.EndsWith("`\"")
                ? string.Concat("\"", trimmed[2..^2], "\"")
                : trimmed.ToString();
            if (isMultiline)
                text = text.ReplaceLineEndings("<br/>");
            if (!quoted && text.AsSpan().ContainsAny('|', '"'))
                text = $"\"{text.Replace("\"", "#quot;", StringComparison.Ordinal)}\"";
            label = $"|{text}|";
        }

        var endings = new EdgeEndings(ReadEnding(op[0]), ReadEnding(op[^1]));
        if (!inlineLabel.Success && !isMultiline && !endings.HasMarkers)
            return default;

        var start = endings.Start == EdgeEnding.None ? 0 : 1;
        var end = endings.End == EdgeEnding.None ? op.Length : op.Length - 1;
        var body = op[start..end];
        // Mermaider uses at least three strokes for links with no arrowhead.
        var strokeWithoutArrow = body.StartsWith('<') ? body[1..] : body;
        var extraStroke = strokeWithoutArrow is "--" ? "-" : strokeWithoutArrow is "==" ? "=" : "";
        return ($" {body}{extraStroke}{label} ", endings);
    }

    internal static int FindDiagnosticOperator(string statement, ReadOnlySpan<char> token, int column)
    {
        var found = -1;
        foreach (var (_, _, arrow, _) in ReadNodeReferences(statement))
        {
            var op = arrow.Groups["operator"];
            if (op.Success && op.Index <= column && op.ValueSpan.SequenceEqual(token))
                found = op.Index;
        }
        return found;
    }

    internal static ReadOnlySpan<char> ReadAuthoredOperator(string source, int start)
    {
        var arrow = EdgeOperator().Match(source, start);
        var op = arrow.Groups["operator"];
        return op.Success ? op.ValueSpan : arrow.Groups["suffix"].ValueSpan;
    }

    private static EdgeEnding ReadEnding(char value) => value switch
    {
        'o' => EdgeEnding.Circle,
        'x' => EdgeEnding.Cross,
        _ => EdgeEnding.None
    };

    internal static bool HasNodeContinuation(ReadOnlySpan<char> line, int idLength)
    {
        var suffix = line[idLength..].TrimStart();
        if (suffix.IsEmpty)
            return false;
        if (suffix[0] is '[' or '(' or '{' or ':' or '&' or ';' || suffix.StartsWith("%%"))
            return true;

        var statement = line.ToString();
        var namedLink = EdgeIdAfterNode().Match(statement, idLength);
        var linkStart = namedLink.Success ? namedLink.Index + namedLink.Length : idLength;
        return EdgeOperator().IsMatch(statement, linkStart);
    }

    private static IEnumerable<(Group Node, int ShapeLength, Match Arrow, Group? EdgeId)> ReadNodeReferences(string statement)
    {
        var cursor = 0;
        while (cursor < statement.Length && NodeReference().Match(statement, cursor) is { Success: true } reference)
        {
            var node = reference.Groups["id"];
            var nodeEnd = node.Index + node.Length;
            cursor = SkipNodeShape(statement, nodeEnd);
            if (cursor < statement.Length && statement[cursor] == '@')
                yield break;

            var shapeLength = cursor - nodeEnd;
            var nodeClass = NodeClass().Match(statement, cursor);
            if (nodeClass.Success)
                cursor += nodeClass.Length;

            var namedLink = EdgeIdAfterNode().Match(statement, cursor);
            if (namedLink.Success)
                cursor += namedLink.Length;

            var arrow = EdgeOperator().Match(statement, cursor);
            yield return (node, shapeLength, arrow, namedLink.Success ? namedLink.Groups["id"] : null);
            if (arrow.Success)
            {
                cursor += arrow.Length;
                continue;
            }

            while (cursor < statement.Length && char.IsWhiteSpace(statement[cursor]))
                cursor++;
            if (cursor == statement.Length || statement[cursor++] != '&')
                yield break;
        }
    }

    private static string? ReadSubgraphId(ReadOnlySpan<char> heading)
    {
        heading = heading.Trim();
        var labelStart = heading.IndexOf('[');
        var id = (labelStart >= 0 ? heading[..labelStart].TrimEnd() : heading).Trim('"');
        return id.IsEmpty || id.ContainsAny(" \t\r\n") ? null : id.ToString();
    }

    private static Dictionary<string, string> AddNodeReferenceEdits(
        string source, int bodyStart, Dictionary<string, AdaptedNode> standaloneNodes,
        List<NodeAppearanceUpdate> appearanceUpdates, List<SourceEdit> edits, CancellationToken cancellationToken)
    {
        var rewrittenStatements = new HashSet<int>();
        var normalizedArrows = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (edit.Link is not { } link)
                rewrittenStatements.Add(edit.Start);
            else if (link.Id is null)
                normalizedArrows.Add(edit.Start);
        }
        var references = new List<AdaptedNodeReference>();
        foreach (var (start, length, isAccessibility, _) in GetStatements(source, bodyStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (length == 0 || isAccessibility || rewrittenStatements.Contains(start))
                continue;

            var statement = source.Substring(start, length);
            var compactArrowStart = -1;
            foreach (var (id, shapeLength, arrow, edgeId) in ReadNodeReferences(statement))
            {
                var authoredId = id.Value;
                if (standaloneNodes.TryGetValue(authoredId, out var node))
                {
                    var nodeStart = start + id.Index;
                    if (nodeStart < node.Start)
                        standaloneNodes[authoredId] = node with { Start = nodeStart };
                    references.Add(new AdaptedNodeReference(authoredId, nodeStart, shapeLength,
                        compactArrowStart));
                    if (shapeLength > 0)
                    {
                        var appearance = MermaidFlowchartMetadata.ReadClassicAppearance(statement.AsSpan(id.Index + id.Length, shapeLength));
                        if (appearance != default)
                            appearanceUpdates.Add(new NodeAppearanceUpdate(authoredId, nodeStart, appearance));
                    }
                }
                compactArrowStart = arrow.Success && edgeId is null && !char.IsWhiteSpace(statement[arrow.Index])
                    ? start + arrow.Index
                    : -1;
            }
        }

        // Mermaid applies updates in source order. Mermaider keeps the first rendered definition.
        appearanceUpdates.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        var appearances = new Dictionary<string, MermaidFlowchartMetadata.NodeAppearance>(StringComparer.Ordinal);
        foreach (var update in appearanceUpdates)
        {
            var previous = appearances.GetValueOrDefault(update.Id);
            appearances[update.Id] = new(update.Appearance.Label ?? previous.Label, update.Appearance.Shape ?? previous.Shape);
        }
        var declarations = new Dictionary<string, string>(standaloneNodes.Count, StringComparer.Ordinal);
        foreach (var (id, node) in standaloneNodes)
            declarations.Add(id, MermaidFlowchartMetadata.FormatNodeDeclaration(node.Id, id, appearances.GetValueOrDefault(id)));

        foreach (var reference in references)
        {
            var node = standaloneNodes[reference.Id];
            var length = reference.Id.Length + reference.ShapeLength;
            var replacement = node.Id;
            if (reference.Start == node.Start)
            {
                replacement = declarations[reference.Id];
                // A compact arrow followed by an inserted shape can look like an asymmetric node to Mermaider.
                if (replacement != node.Id && reference.CompactArrowStart >= 0 && !normalizedArrows.Contains(reference.CompactArrowStart))
                    edits.Add(new SourceEdit(reference.CompactArrowStart, 0, 0, " "));
            }
            if (!source.AsSpan(reference.Start, length).SequenceEqual(replacement))
                edits.Add(new SourceEdit(reference.Start, length, length, replacement));
        }
        return declarations;
    }

    private static int SkipNodeShape(string statement, int cursor)
    {
        if (cursor == statement.Length || statement[cursor] is not ('[' or '(' or '{' or '>'))
            return cursor;

        // The asymmetric shape opens with '>' and closes with ']'.
        var nesting = 0;
        if (statement[cursor] == '>')
        {
            nesting = 1;
            cursor++;
        }
        var quoted = false;
        var pipeLabel = false;
        while (cursor < statement.Length)
        {
            UpdateLabelState(statement[cursor++], ref nesting, ref quoted, ref pipeLabel);
            if (nesting == 0)
                break;
        }
        return cursor;
    }

    internal static bool IsFlowchartHeader(ReadOnlySpan<char> header) => FlowchartHeader().IsMatch(header);

    internal static MermaidSourceNormalization NormalizeForRenderer(string source, CancellationToken cancellationToken, int? diagramEnd = null)
    {
        var prefix = source.AsSpan(0, diagramEnd ?? source.Length);
        var hasAccessibilityDescription = prefix.Contains("accDescr", StringComparison.Ordinal);
        if (!hasAccessibilityDescription && !prefix.TrimStart().StartsWith("---", StringComparison.Ordinal))
            return new(source);

        var diagram = diagramEnd is { } endOffset ? source[..endOffset] : source;
        var header = ReadDiagramHeader(diagram, out var bodyStart, out var frontMatter);
        var normalizeDescription = hasAccessibilityDescription && IsFlowchartHeader(header);
        if (!frontMatter.HasIndentedDelimiter && !normalizeDescription)
            return new(source);

        var replacements = new List<MermaidSourceReplacement>();
        if (frontMatter.HasIndentedDelimiter)
        {
            var (_, _, title) = frontMatter.Read(diagram);
            replacements.Add(new(frontMatter.Start, frontMatter.Length, frontMatter.FormatForMermaider(diagram, title)));
        }
        if (!normalizeDescription)
            return MermaidSourceNormalization.Apply(diagram, replacements, null);

        var frontMatterReplacementCount = replacements.Count;
        if (bodyStart > 0 && diagram[bodyStart - 1] == ';')
            replacements.Add(new(bodyStart - 1, 1, "\n"));
        var needsDescriptionRewrite = false;
        string? descriptionPercentMarker = null;
        foreach (var (start, length, _, bracedContentStart) in GetStatements(diagram, bodyStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = start + length;
            if (bracedContentStart == 0)
            {
                if (end < diagram.Length && diagram[end] == ';')
                    replacements.Add(new(end, 1, "\n"));
                continue;
            }
            if (diagram[end - 1] != '}')
                continue;

            var content = diagram.AsSpan(start + bracedContentStart, length - bracedContentStart - 1);
            var openingLineEnd = content.IndexOfAny('\r', '\n');
            var closingLineStart = content.LastIndexOfAny('\r', '\n') + 1;
            var trailing = MermaidAccessibilitySyntax.TrimLeadingWhitespaceOnLine(diagram.AsSpan(end));
            var startsAfterSeparator = start > 0 && diagram[start - 1] == ';';
            var hasTrailingStatement = !trailing.IsEmpty && trailing[0] is not ('\r' or '\n');
            var percentMarker = MermaidAccessibilitySyntax.GetPercentMarker(content, diagram, ref descriptionPercentMarker);
            if (percentMarker is null && !startsAfterSeparator && !hasTrailingStatement && openingLineEnd >= 0 &&
                content[..openingLineEnd].Trim().IsEmpty && content[closingLineStart..].Trim().IsEmpty)
                continue;

            var replacement = MermaidAccessibilitySyntax.FormatBracedDescription(content, percentMarker);
            if (hasTrailingStatement && trailing[0] != ';')
                replacement += "\n";
            replacements.Add(new(start, length, replacement));
            needsDescriptionRewrite = true;
        }

        if (!needsDescriptionRewrite)
            replacements.RemoveRange(frontMatterReplacementCount, replacements.Count - frontMatterReplacementCount);
        return replacements.Count > 0 ? MermaidSourceNormalization.Apply(diagram, replacements, descriptionPercentMarker) : new(source);
    }

    internal static int FindDiagramErrorStart(string source, int reportedStart, ReadOnlySpan<char> statement)
    {
        var header = ReadDiagramHeader(source, out var bodyStart);
        if (statement.IsEmpty || !IsFlowchartHeader(header))
            return reportedStart;

        // Mermaider can match statement text inside front matter or accessibility descriptions.
        var reportedBeforeHeader = source.AsSpan().Overlaps(header, out var headerStart) && reportedStart < headerStart;

        var exactMatch = -1;
        var multiline = statement.Contains('\n');
        foreach (var (start, length, isAccessibility, _) in GetStatements(source, bodyStart))
        {
            if (isAccessibility)
            {
                if (reportedStart >= start && reportedStart < start + length)
                {
                    if (exactMatch >= 0)
                        return exactMatch;
                    reportedStart = -1;
                }
                continue;
            }

            var content = source.AsSpan(start, length).TrimStart();
            var trimmed = content.TrimEnd();
            if (!trimmed.SequenceEqual(statement) && (!multiline ||
                !MermaidSourceNormalization.TryMapMultilineColumn(trimmed, statement, 0, out _)))
                continue;
            if (reportedStart < 0 || reportedBeforeHeader)
                return start + length - content.Length;
            if (exactMatch < 0)
                exactMatch = start + length - content.Length;
        }
        return reportedStart;
    }

    internal static ReadOnlySpan<char> ReadDiagramHeader(string source, out int bodyStart) =>
        ReadDiagramHeader(source, out bodyStart, out _);

    private static ReadOnlySpan<char> ReadDiagramHeader(string source, out int bodyStart, out MermaidFrontMatter frontMatter)
    {
        bodyStart = 0;
        frontMatter = default;
        var insideFrontmatter = false;
        var frontmatterSeen = false;
        var frontMatterStart = 0;
        var delimiterIndent = ReadOnlySpan<char>.Empty;
        var indentStart = 0;
        var hasIndentedDelimiter = false;
        foreach (var line in source.AsSpan().EnumerateLines())
        {
            var start = bodyStart;
            bodyStart += line.Length;
            if (bodyStart < source.Length && source[bodyStart] == '\r')
                bodyStart++;
            if (bodyStart < source.Length && source[bodyStart] == '\n')
                bodyStart++;

            var text = line.Trim();
            if (text.SequenceEqual("---") && (!frontmatterSeen || insideFrontmatter))
            {
                var indent = line[..(line.Length - line.TrimStart().Length)];
                if (insideFrontmatter)
                {
                    if (!indent.SequenceEqual(delimiterIndent))
                    {
                        hasIndentedDelimiter = true;
                        continue;
                    }
                    frontMatter = new(frontMatterStart, start - frontMatterStart, hasIndentedDelimiter,
                        indentStart..(indentStart + delimiterIndent.Length));
                }
                else
                {
                    frontMatterStart = bodyStart;
                    delimiterIndent = indent;
                    indentStart = start;
                }
                insideFrontmatter = !insideFrontmatter;
                frontmatterSeen = true;
                continue;
            }
            if (insideFrontmatter || text.IsEmpty || text.StartsWith("%%"))
                continue;

            // Mermaid's flowchart header can end in a semicolon on the same line as an edge.
            var separator = line.IndexOf(';');
            if (separator >= 0 && IsFlowchartHeader(text))
                bodyStart = start + separator + 1;
            return text;
        }
        return [];
    }

    private static IEnumerable<(int Start, int Length, bool IsAccessibility, int BracedContentStart)> GetStatements(string source, int start)
    {
        var nesting = 0;
        var quoted = false;
        var pipeLabel = false;
        var insideDescription = false;
        for (var index = start; index < source.Length; index++)
        {
            if (index == start && MermaidAccessibilitySyntax.TryReadDirective(source.AsSpan(index), ref insideDescription,
                    out var length, out var bracedContentStart))
            {
                yield return (start, length, true, bracedContentStart);
                start += length;
                index = start - 1;
                continue;
            }
            var current = source[index];
            if (nesting == 0 && !quoted && !pipeLabel && index + 1 < source.Length &&
                (current == '-' && source[index + 1] is '-' or '.' || current == '=' && source[index + 1] == '='))
            {
                // An inline label can contain characters that would otherwise change the statement's delimiter state.
                var arrows = EdgeOperator().EnumerateMatches(source.AsSpan(index));
                if (arrows.MoveNext())
                {
                    index += arrows.Current.Length - 1;
                    continue;
                }
            }
            if (nesting == 0 && !quoted && !pipeLabel && current == '{' && source.AsSpan(start, index - start).TrimEnd().EndsWith('@'))
            {
                var blockEnd = MermaidFlowchartMetadata.FindBlockEnd(source.AsSpan(index + 1));
                if (blockEnd >= 0)
                {
                    index += blockEnd + 1;
                    continue;
                }
            }
            if (nesting == 0 && !quoted && !pipeLabel && current == '%' &&
                index + 1 < source.Length && source[index + 1] == '%')
            {
                yield return (start, index - start, false, 0);
                var end = source.IndexOf('\n', index);
                if (end < 0)
                    yield break;
                start = end + 1;
                index = end;
                continue;
            }
            if (nesting == 0 && !quoted && !pipeLabel && current is ';' or '\n' or '\r')
            {
                yield return (start, index - start, false, 0);
                start = index + 1;
                continue;
            }
            UpdateLabelState(current, ref nesting, ref quoted, ref pipeLabel);
        }
        if (start < source.Length)
            yield return (start, source.Length - start, false, 0);
    }

    private static void UpdateLabelState(char current, ref int nesting, ref bool quoted, ref bool pipeLabel)
    {
        if (current == '"')
            quoted = !quoted;
        if (quoted)
            return;
        if (nesting == 0 && current == '|')
            pipeLabel = !pipeLabel;
        if (pipeLabel)
            return;
        if (current is '[' or '(' or '{')
            nesting++;
        else if (nesting > 0 && current is ']' or ')' or '}')
            nesting--;
    }

    private static EdgeAnimation ReadProperties(string properties, EdgeAnimation animation)
    {
        try
        {
            // Mermaid uses a flow mapping for single-line properties and a YAML block for multiline properties.
            var yaml = properties.AsSpan().ContainsAny('\r', '\n') ? properties : string.Concat("{", properties, "\n}");
            var mapping = MermaidYamlMapping.Parse(yaml);
            var animate = mapping.ReadBoolean("animate");
            if (animate is { } isAnimated)
                animation = animation with { Animate = isAnimated };

            if (mapping.TryReadNullableString("animation", out var speed))
            {
                animation = speed switch
                {
                    "fast" => animation with { Speed = DashSpeed.Fast },
                    "slow" => animation with { Speed = DashSpeed.Slow },
                    null => animation with { Speed = DashSpeed.Unspecified },
                    _ => animation
                };
            }

            if (mapping.ReadNumber("step") is > 0 and <= int.MaxValue and var step && step == Math.Truncate(step))
                animation = animation with { Step = (int)step };
        }
        catch (YamlException)
        {
            Trace.TraceWarning("Could not read Mermaid native edge properties from YAML.");
        }
        return animation;
    }

    private readonly record struct SourceEdit(
        int Start, int Length, int ProbeLength, string? Replacement = null, bool IsAccessibility = false,
        string? StandaloneNodeId = null, LinkEdit? Link = null);

    private readonly record struct LinkEdit(string? Id, EdgeEndings Endings, int OperatorStart);

    private readonly record struct AdaptedNode(string Id, int Start);

    private readonly record struct AdaptedNodeReference(string Id, int Start, int ShapeLength, int CompactArrowStart);

    private readonly record struct NodeAppearanceUpdate(string Id, int Start, MermaidFlowchartMetadata.NodeAppearance Appearance);

    internal enum DashSpeed
    {
        Unspecified,
        Slow,
        Fast
    }

    internal enum EdgeEnding { None, Circle, Cross }

    internal readonly record struct EdgeEndings(EdgeEnding Start, EdgeEnding End)
    {
        public bool HasMarkers => Start != EdgeEnding.None || End != EdgeEnding.None;
    }

    internal readonly record struct EdgeAnimation(
        string? Id, int Step, bool Animate, DashSpeed Speed, MermaidEdgeDash? ClassDash = null, EdgeEndings Endings = default)
    {
        public bool HasDashes => Animate || Speed != DashSpeed.Unspecified;
    }
}
