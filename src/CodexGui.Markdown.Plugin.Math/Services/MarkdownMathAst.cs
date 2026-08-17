using SystemMath = System.Math;

namespace CodexGui.Markdown.Plugin.Math;

internal enum MarkdownMathDisplayMode
{
    Inline,
    Block
}

internal enum MarkdownMathTextStyle
{
    Normal,
    Roman,
    Italic,
    Bold,
    SansSerif,
    Monospace,
    Script,
    Blackboard,
    Fraktur,
    Operator
}

internal readonly record struct MarkdownMathSourceSpan(int Start, int Length)
{
    public static MarkdownMathSourceSpan Empty { get; } = new(-1, 0);

    public int EndExclusive => Length <= 0 ? Start : Start + Length;

    public bool IsEmpty => Start < 0 || Length <= 0;

    public string Slice(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (IsEmpty || Start >= source.Length)
        {
            return string.Empty;
        }

        var start = SystemMath.Max(Start, 0);
        var length = SystemMath.Min(Length, source.Length - start);
        return length <= 0 ? string.Empty : source.Substring(start, length);
    }
}

internal sealed record MarkdownMathDiagnostic(string Message, MarkdownMathSourceSpan Span);

internal abstract record MarkdownMathNode(MarkdownMathSourceSpan Span);

internal sealed record MarkdownMathExpression(IReadOnlyList<MarkdownMathNode> Children, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span)
{
    public static MarkdownMathExpression Empty { get; } = new(Array.Empty<MarkdownMathNode>(), MarkdownMathSourceSpan.Empty);
}

internal sealed record MarkdownMathGroupedExpression(MarkdownMathExpression Content, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathIdentifier(string Text, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathNumber(string Text, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathOperator(string Text, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathSpace(double WidthEm, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathTextRun(string Text, MarkdownMathTextStyle Style, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathSymbol(string Command, string RenderText, bool IsLargeOperator, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathCommand(string Name, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathStyledExpression(MarkdownMathTextStyle Style, MarkdownMathExpression Content, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathFraction(MarkdownMathExpression Numerator, MarkdownMathExpression Denominator, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathRoot(MarkdownMathExpression Radicand, MarkdownMathExpression? Degree, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathScript(MarkdownMathNode Base, MarkdownMathExpression? Subscript, MarkdownMathExpression? Superscript, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathDelimited(string LeftDelimiter, MarkdownMathExpression Content, string RightDelimiter, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathAccent(string AccentText, MarkdownMathNode Base, bool Underline, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathEnvironment(string Name, IReadOnlyList<IReadOnlyList<MarkdownMathExpression>> Rows, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed record MarkdownMathError(string Text, string Message, MarkdownMathSourceSpan Span)
    : MarkdownMathNode(Span);

internal sealed class MarkdownMathDocument(
    string source,
    MarkdownMathDisplayMode displayMode,
    MarkdownMathExpression root,
    IReadOnlyList<MarkdownMathDiagnostic> diagnostics)
{
    public string Source { get; } = source;

    public MarkdownMathDisplayMode DisplayMode { get; } = displayMode;

    public MarkdownMathExpression Root { get; } = root;

    public IReadOnlyList<MarkdownMathDiagnostic> Diagnostics { get; } = diagnostics;

    public bool HasDiagnostics => Diagnostics.Count > 0;
}
