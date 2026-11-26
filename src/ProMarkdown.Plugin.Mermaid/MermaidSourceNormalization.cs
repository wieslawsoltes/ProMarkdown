using System.Text;
using Mermaider;

namespace ProMarkdown.Plugin.Mermaid;

internal readonly record struct MermaidSourceNormalization(
    string Source, string? Original = null, IReadOnlyList<MermaidSourceChange>? Changes = null,
    string? DescriptionPercentMarker = null)
{
    public bool HasChanges => Original is not null;

    public int LineOffset => Original is { } original
        ? Source.AsSpan().Count('\n') - original.AsSpan().Count('\n')
        : 0;

    public static MermaidSourceNormalization Apply(
        string source, IReadOnlyList<MermaidSourceReplacement> replacements, string? descriptionPercentMarker)
    {
        var result = new StringBuilder(source.Length);
        var changes = new MermaidSourceChange[replacements.Count];
        var offset = 0;
        for (var index = 0; index < replacements.Count; index++)
        {
            var replacement = replacements[index];
            result.Append(source.AsSpan(offset, replacement.Start - offset)).Append(replacement.Text);
            offset = replacement.Start + replacement.Length;
            changes[index] = new(replacement.Start, replacement.Length, replacement.Text.Length);
        }
        return new(result.Append(source.AsSpan(offset)).ToString(), source, changes, descriptionPercentMarker);
    }

    public MermaidParseException MapParseError(MermaidParseException error)
    {
        if (error.Line is not > 0 || error.Problem is not { } problem ||
            (!HasChanges && !MermaidNativeEdgeSyntax.IsFlowchartHeader(MermaidNativeEdgeSyntax.ReadDiagramHeader(Source, out _))))
            return error;

        var generatedLineStart = 0;
        for (var line = 1; line < error.Line.Value; line++)
        {
            var newline = Source.IndexOf('\n', generatedLineStart);
            if (newline < 0)
                return error;
            generatedLineStart = newline + 1;
        }

        var remaining = Source.AsSpan(generatedLineStart);
        var generatedLineEnd = remaining.IndexOf('\n');
        var generatedLine = generatedLineEnd < 0 ? remaining : remaining[..generatedLineEnd];
        var statement = error.SourceLine.AsSpan().Trim();
        var firstLineEnd = statement.IndexOf('\n');
        var firstLine = firstLineEnd < 0 ? statement : statement[..firstLineEnd].TrimEnd();
        var statementStart = generatedLine.IndexOf(firstLine, StringComparison.Ordinal);
        if (statementStart < 0)
            statementStart = generatedLine.Length - generatedLine.TrimStart().Length;
        var generatedStart = MermaidNativeEdgeSyntax.FindDiagramErrorStart(
            Source, generatedLineStart + statementStart, statement);
        if (generatedStart < 0)
            return new MermaidParseException(problem, error.SourceLine ?? string.Empty, error.Expected);

        var reportedColumn = error.Column.GetValueOrDefault(1) - 1;
        var columnOffset = Math.Clamp(reportedColumn, 0, generatedLine.Length - statementStart);
        if (firstLineEnd >= 0 && TryMapMultilineColumn(Source.AsSpan(generatedStart), statement, reportedColumn, out var multilineOffset))
            columnOffset = multilineOffset;
        var generatedOffset = Math.Min(Source.Length, generatedStart + columnOffset);
        var original = Original ?? Source;
        var authoredOffset = generatedOffset;
        var authoredProblem = problem;
        if (Changes is { } changes)
        {
            authoredOffset = ToOriginalOffset(generatedOffset, changes, out _);
            authoredProblem = RestoreAuthoredOperator(problem, generatedStart, generatedOffset, original, changes);
        }
        var before = original.AsSpan(0, authoredOffset);
        var lineStart = before.LastIndexOf('\n') + 1;
        var authoredLine = original.AsSpan(lineStart);
        var lineEnd = authoredLine.IndexOf('\n');
        if (lineEnd >= 0)
            authoredLine = authoredLine[..lineEnd];
        var trimmedLine = authoredLine.TrimStart();
        int? column = error.Column.HasValue
            ? Math.Max(1, authoredOffset - lineStart - (authoredLine.Length - trimmedLine.Length) + 1)
            : null;
        var authoredLineNumber = before.Count('\n') + 1;
        trimmedLine = trimmedLine.TrimEnd();
        if (authoredLineNumber == error.Line && column == error.Column && authoredProblem == problem &&
            trimmedLine.SequenceEqual(error.SourceLine.AsSpan()))
            return error;
        return new MermaidParseException(authoredProblem, trimmedLine.ToString(), error.Expected, authoredLineNumber, column);
    }

    private string RestoreAuthoredOperator(string problem, int statementStart, int errorOffset, string original,
        IReadOnlyList<MermaidSourceChange> changes)
    {
        const string prefix = "link '";
        if (!problem.StartsWith(prefix, StringComparison.Ordinal))
            return problem;
        var tokenEnd = problem.IndexOf('\'', prefix.Length);
        if (tokenEnd <= prefix.Length)
            return problem;

        var token = problem.AsSpan(prefix.Length, tokenEnd - prefix.Length);
        var lineEnd = Source.IndexOf('\n', errorOffset);
        var statement = Source[statementStart..(lineEnd < 0 ? Source.Length : lineEnd)];
        var operatorOffset = MermaidNativeEdgeSyntax.FindDiagnosticOperator(statement, token, errorOffset - statementStart);
        if (operatorOffset < 0)
            return problem;

        var authoredStart = ToOriginalOffset(statementStart + operatorOffset, changes, out var rewritten);
        if (!rewritten)
            return problem;
        var authoredToken = MermaidNativeEdgeSyntax.ReadAuthoredOperator(original, authoredStart);
        return authoredToken.IsEmpty || authoredToken.SequenceEqual(token) ? problem :
            string.Concat(prefix, authoredToken, problem.AsSpan(tokenEnd));
    }

    internal static bool TryMapMultilineColumn(ReadOnlySpan<char> source, ReadOnlySpan<char> statement, int column, out int offset)
    {
        // Mermaider joins trimmed physical lines into the diagnostic statement and skips blank and comment lines.
        offset = 0;
        column = Math.Clamp(column, 0, statement.Length);
        var sourceLines = source.Split('\n');
        foreach (var range in statement.Split('\n'))
        {
            var reportedLine = statement[range];
            var reportedText = reportedLine.Trim();
            ReadOnlySpan<char> sourceLine;
            while (true)
            {
                if (!sourceLines.MoveNext())
                    return false;
                sourceLine = source[sourceLines.Current];
                var sourceText = sourceLine.Trim();
                if (sourceText.SequenceEqual(reportedText))
                    break;
                if (!sourceText.IsEmpty && !sourceText.StartsWith("%%", StringComparison.Ordinal))
                    return false;
            }

            var columnInLine = column - range.Start.Value;
            if (columnInLine >= 0 && columnInLine <= reportedLine.Length)
            {
                var sourceIndent = sourceLine.Length - sourceLine.TrimStart().Length;
                var reportedIndent = reportedLine.Length - reportedLine.TrimStart().Length;
                offset = sourceLines.Current.Start.Value + sourceIndent +
                    Math.Clamp(columnInLine - reportedIndent, 0, sourceLine.Length - sourceIndent);
            }
        }
        return true;
    }

    private static int ToOriginalOffset(int generatedOffset, IReadOnlyList<MermaidSourceChange> changes, out bool rewritten)
    {
        rewritten = false;
        var offset = 0;
        foreach (var change in changes)
        {
            var generatedStart = change.Start + offset;
            if (generatedOffset < generatedStart)
                break;
            if (generatedOffset < generatedStart + change.GeneratedLength)
            {
                rewritten = change.AuthoredAnchor >= 0;
                return rewritten ? change.AuthoredAnchor :
                    change.Start + Math.Min(generatedOffset - generatedStart, Math.Max(0, change.Length - 1));
            }
            offset += change.GeneratedLength - change.Length;
        }
        return generatedOffset - offset;
    }
}

internal readonly record struct MermaidSourceChange(int Start, int Length, int GeneratedLength, int AuthoredAnchor = -1);

internal readonly record struct MermaidSourceReplacement(int Start, int Length, string Text);
