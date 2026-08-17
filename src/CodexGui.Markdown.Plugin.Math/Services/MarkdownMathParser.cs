using System.Collections.Frozen;
using System.Text;
using SystemMath = System.Math;

namespace CodexGui.Markdown.Plugin.Math;

internal static class MarkdownMathParser
{
    private static readonly FrozenDictionary<string, (string RenderText, bool IsLargeOperator)> SymbolCommands =
        new Dictionary<string, (string RenderText, bool IsLargeOperator)>(StringComparer.Ordinal)
        {
            ["alpha"] = ("α", false),
            ["beta"] = ("β", false),
            ["gamma"] = ("γ", false),
            ["delta"] = ("δ", false),
            ["epsilon"] = ("ϵ", false),
            ["varepsilon"] = ("ε", false),
            ["zeta"] = ("ζ", false),
            ["eta"] = ("η", false),
            ["theta"] = ("θ", false),
            ["vartheta"] = ("ϑ", false),
            ["iota"] = ("ι", false),
            ["kappa"] = ("κ", false),
            ["lambda"] = ("λ", false),
            ["mu"] = ("μ", false),
            ["nu"] = ("ν", false),
            ["xi"] = ("ξ", false),
            ["pi"] = ("π", false),
            ["varpi"] = ("ϖ", false),
            ["rho"] = ("ρ", false),
            ["varrho"] = ("ϱ", false),
            ["sigma"] = ("σ", false),
            ["varsigma"] = ("ς", false),
            ["tau"] = ("τ", false),
            ["upsilon"] = ("υ", false),
            ["phi"] = ("ϕ", false),
            ["varphi"] = ("φ", false),
            ["chi"] = ("χ", false),
            ["psi"] = ("ψ", false),
            ["omega"] = ("ω", false),
            ["Gamma"] = ("Γ", false),
            ["Delta"] = ("Δ", false),
            ["Theta"] = ("Θ", false),
            ["Lambda"] = ("Λ", false),
            ["Xi"] = ("Ξ", false),
            ["Pi"] = ("Π", false),
            ["Sigma"] = ("Σ", true),
            ["Upsilon"] = ("Υ", false),
            ["Phi"] = ("Φ", false),
            ["Psi"] = ("Ψ", false),
            ["Omega"] = ("Ω", false),
            ["times"] = ("×", false),
            ["cdot"] = ("·", false),
            ["pm"] = ("±", false),
            ["mp"] = ("∓", false),
            ["div"] = ("÷", false),
            ["ast"] = ("∗", false),
            ["star"] = ("⋆", false),
            ["circ"] = ("∘", false),
            ["bullet"] = ("•", false),
            ["oplus"] = ("⊕", false),
            ["ominus"] = ("⊖", false),
            ["otimes"] = ("⊗", false),
            ["leq"] = ("≤", false),
            ["geq"] = ("≥", false),
            ["neq"] = ("≠", false),
            ["approx"] = ("≈", false),
            ["sim"] = ("∼", false),
            ["to"] = ("→", false),
            ["rightarrow"] = ("→", false),
            ["leftarrow"] = ("←", false),
            ["leftrightarrow"] = ("↔", false),
            ["Rightarrow"] = ("⇒", false),
            ["Leftarrow"] = ("⇐", false),
            ["Leftrightarrow"] = ("⇔", false),
            ["mapsto"] = ("↦", false),
            ["infty"] = ("∞", false),
            ["partial"] = ("∂", false),
            ["nabla"] = ("∇", false),
            ["forall"] = ("∀", false),
            ["exists"] = ("∃", false),
            ["neg"] = ("¬", false),
            ["in"] = ("∈", false),
            ["notin"] = ("∉", false),
            ["subset"] = ("⊂", false),
            ["subseteq"] = ("⊆", false),
            ["supset"] = ("⊃", false),
            ["supseteq"] = ("⊇", false),
            ["cup"] = ("∪", false),
            ["cap"] = ("∩", false),
            ["land"] = ("∧", false),
            ["lor"] = ("∨", false),
            ["sum"] = ("∑", true),
            ["prod"] = ("∏", true),
            ["coprod"] = ("∐", true),
            ["int"] = ("∫", true),
            ["iint"] = ("∬", true),
            ["iiint"] = ("∭", true),
            ["oint"] = ("∮", true),
            ["ldots"] = ("…", false),
            ["cdots"] = ("⋯", false),
            ["vdots"] = ("⋮", false),
            ["ddots"] = ("⋱", false),
            ["lbrace"] = ("{", false),
            ["rbrace"] = ("}", false),
            ["langle"] = ("⟨", false),
            ["rangle"] = ("⟩", false),
            ["lvert"] = ("|", false),
            ["rvert"] = ("|", false),
            ["lVert"] = ("‖", false),
            ["rVert"] = ("‖", false)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> FunctionCommands =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "arccos", "arcsin", "arctan", "arg", "cos", "cosh", "cot", "coth", "csc", "deg", "det",
            "dim", "exp", "gcd", "hom", "inf", "ker", "lg", "lim", "liminf", "limsup", "ln", "log",
            "max", "min", "Pr", "sec", "sin", "sinh", "sup", "tan", "tanh"
        }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, MarkdownMathTextStyle> StyledCommands =
        new Dictionary<string, MarkdownMathTextStyle>(StringComparer.Ordinal)
        {
            ["mathbf"] = MarkdownMathTextStyle.Bold,
            ["mathit"] = MarkdownMathTextStyle.Italic,
            ["mathrm"] = MarkdownMathTextStyle.Roman,
            ["mathsf"] = MarkdownMathTextStyle.SansSerif,
            ["mathtt"] = MarkdownMathTextStyle.Monospace,
            ["mathcal"] = MarkdownMathTextStyle.Script,
            ["mathbb"] = MarkdownMathTextStyle.Blackboard,
            ["mathfrak"] = MarkdownMathTextStyle.Fraktur,
            ["operatorname"] = MarkdownMathTextStyle.Operator
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, (string AccentText, bool Underline)> AccentCommands =
        new Dictionary<string, (string AccentText, bool Underline)>(StringComparer.Ordinal)
        {
            ["hat"] = ("^", false),
            ["bar"] = ("¯", false),
            ["vec"] = ("→", false),
            ["dot"] = ("˙", false),
            ["ddot"] = ("¨", false),
            ["tilde"] = ("˜", false),
            ["overline"] = ("¯", false),
            ["underline"] = ("_", true)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, double> SpacingCommands =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [","] = 0.2,
            [":"] = 0.3,
            [";"] = 0.45,
            ["!"] = 0.12,
            [" "] = 0.33,
            ["quad"] = 1.0,
            ["qquad"] = 2.0
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, string> EscapedCharacterCommands =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{"] = "{",
            ["}"] = "}",
            ["$"] = "$",
            ["%"] = "%",
            ["&"] = "&",
            ["#"] = "#",
            ["_"] = "_",
            ["^"] = "^"
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static MarkdownMathDocument ParseInline(string? source)
    {
        return new Parser(source ?? string.Empty, MarkdownMathDisplayMode.Inline).Parse();
    }

    public static MarkdownMathDocument ParseBlock(string? source)
    {
        return new Parser(source ?? string.Empty, MarkdownMathDisplayMode.Block).Parse();
    }

    private sealed class Parser(string source, MarkdownMathDisplayMode displayMode)
    {
        private readonly string _source = MathMarkdownSyntax.NormalizeLineEndings(source);
        private readonly MarkdownMathDisplayMode _displayMode = displayMode;
        private readonly List<MarkdownMathDiagnostic> _diagnostics = [];
        private int _position;

        public MarkdownMathDocument Parse()
        {
            var root = ParseExpression(static _ => false);
            if (!IsAtEnd)
            {
                AddDiagnostic("Unexpected trailing math content.", CreateSpan(_position, _source.Length));
            }

            return new MarkdownMathDocument(_source, _displayMode, root, _diagnostics);
        }

        private MarkdownMathExpression ParseExpression(Func<Parser, bool> shouldStop)
        {
            var start = _position;
            List<MarkdownMathNode> children = [];

            while (!IsAtEnd && !shouldStop(this))
            {
                var next = ParseNextNode(shouldStop);
                if (next is null)
                {
                    continue;
                }

                if (next is MarkdownMathSpace && children.Count == 0)
                {
                    continue;
                }

                children.Add(next);
            }

            while (children.Count > 0 && children[^1] is MarkdownMathSpace)
            {
                children.RemoveAt(children.Count - 1);
            }

            return new MarkdownMathExpression(children, CreateSpan(start, _position));
        }

        private MarkdownMathNode? ParseNextNode(Func<Parser, bool> shouldStop)
        {
            if (IsAtEnd || shouldStop(this))
            {
                return null;
            }

            var node = ParsePrimary(shouldStop);
            return node is null ? null : AttachScripts(node, shouldStop);
        }

        private MarkdownMathNode? ParsePrimary(Func<Parser, bool> shouldStop)
        {
            if (IsAtEnd || shouldStop(this))
            {
                return null;
            }

            var current = Peek();
            if (char.IsWhiteSpace(current))
            {
                return ParseWhitespace();
            }

            return current switch
            {
                '{' => ParseGroupedExpression('{', '}'),
                '(' or ')' or '[' or ']' => ParseSingleCharacterOperator(),
                '^' or '_' => ParseUnexpectedScript(),
                '\\' => ParseCommand(shouldStop),
                _ when char.IsDigit(current) => ParseNumber(),
                _ when char.IsLetter(current) => ParseIdentifier(),
                _ => ParseOperatorOrText()
            };
        }

        private MarkdownMathNode AttachScripts(MarkdownMathNode baseNode, Func<Parser, bool> shouldStop)
        {
            MarkdownMathExpression? subscript = null;
            MarkdownMathExpression? superscript = null;
            var start = baseNode.Span.Start;

            while (!IsAtEnd && !shouldStop(this))
            {
                var nextPosition = _position;
                SkipOptionalWhitespace(ref nextPosition);
                if (nextPosition >= _source.Length)
                {
                    break;
                }

                var marker = _source[nextPosition];
                if (marker is not ('^' or '_'))
                {
                    break;
                }

                _position = nextPosition;
                Advance();
                var target = ParseScriptArgument(shouldStop);
                if (marker == '^')
                {
                    superscript = target;
                }
                else
                {
                    subscript = target;
                }
            }

            return superscript is null && subscript is null
                ? baseNode
                : new MarkdownMathScript(baseNode, subscript, superscript, CreateSpan(start, _position));
        }

        private MarkdownMathExpression ParseScriptArgument(Func<Parser, bool> shouldStop)
        {
            if (IsAtEnd || shouldStop(this))
            {
                AddDiagnostic("Expected a superscript or subscript expression.", CreateSpan(_position, _position));
                return MarkdownMathExpression.Empty;
            }

            if (Peek() == '{')
            {
                var grouped = ParseGroupedExpression('{', '}');
                return grouped.Content;
            }

            var start = _position;
            var primary = ParsePrimary(shouldStop);
            return primary is null
                ? MarkdownMathExpression.Empty
                : new MarkdownMathExpression([primary], CreateSpan(start, _position));
        }

        private MarkdownMathGroupedExpression ParseGroupedExpression(char open, char close)
        {
            var start = _position;
            Advance();
            var content = ParseExpression(parser => parser.IsAtEnd || parser.Peek() == close);
            if (IsAtEnd || Peek() != close)
            {
                AddDiagnostic($"Expected '{close}' to close the math group.", CreateSpan(start, _position));
                return new MarkdownMathGroupedExpression(content, CreateSpan(start, _position));
            }

            Advance();
            return new MarkdownMathGroupedExpression(content, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseWhitespace()
        {
            var start = _position;
            var sawLineBreak = false;
            while (!IsAtEnd && char.IsWhiteSpace(Peek()))
            {
                sawLineBreak |= Peek() == '\n';
                Advance();
            }

            var length = SystemMath.Max(_position - start, 1);
            var width = sawLineBreak
                ? 0.8
                : SystemMath.Clamp(length * 0.18, 0.18, 1.5);
            return new MarkdownMathSpace(width, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseNumber()
        {
            var start = _position;
            while (!IsAtEnd && (char.IsDigit(Peek()) || Peek() == '.'))
            {
                Advance();
            }

            return new MarkdownMathNumber(_source[start.._position], CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseIdentifier()
        {
            var start = _position;
            while (!IsAtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '\''))
            {
                Advance();
            }

            return new MarkdownMathIdentifier(_source[start.._position], CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseSingleCharacterOperator()
        {
            var start = _position;
            var character = Advance();
            return new MarkdownMathOperator(character.ToString(), CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseUnexpectedScript()
        {
            var start = _position;
            var character = Advance();
            AddDiagnostic($"Unexpected '{character}' without a base expression.", CreateSpan(start, _position));
            return new MarkdownMathError(character.ToString(), $"Unexpected '{character}' without a base expression.", CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseOperatorOrText()
        {
            var start = _position;
            var character = Advance();
            return new MarkdownMathOperator(character.ToString(), CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseCommand(Func<Parser, bool> shouldStop)
        {
            var start = _position;
            Advance();

            if (IsAtEnd)
            {
                AddDiagnostic("Unexpected end of math content after '\\'.", CreateSpan(start, _position));
                return new MarkdownMathError("\\", "Unexpected end of math content after '\\'.", CreateSpan(start, _position));
            }

            if (Peek() == '\\')
            {
                Advance();
                return new MarkdownMathSpace(0.8, CreateSpan(start, _position));
            }

            var commandName = ReadCommandName();
            if (SpacingCommands.TryGetValue(commandName, out var emWidth))
            {
                return new MarkdownMathSpace(emWidth, CreateSpan(start, _position));
            }

            return commandName switch
            {
                _ when EscapedCharacterCommands.TryGetValue(commandName, out var escapedCharacter) => new MarkdownMathTextRun(escapedCharacter, MarkdownMathTextStyle.Normal, CreateSpan(start, _position)),
                "frac" => ParseFraction(start, shouldStop),
                "sqrt" => ParseRoot(start, shouldStop),
                "left" => ParseDelimited(start),
                "begin" => ParseEnvironment(start),
                "text" => ParseTextCommand(start, MarkdownMathTextStyle.Normal),
                "label" => ParseHiddenAnnotation(start),
                "tag" => ParseTag(start),
                "limits" or "nolimits" or "displaystyle" or "textstyle" or "scriptstyle" or "scriptscriptstyle" => new MarkdownMathTextRun(string.Empty, MarkdownMathTextStyle.Normal, CreateSpan(start, _position)),
                _ when StyledCommands.TryGetValue(commandName, out var style) => ParseStyledCommand(start, commandName, style, shouldStop),
                _ when AccentCommands.TryGetValue(commandName, out var accent) => ParseAccent(start, accent, shouldStop),
                _ when SymbolCommands.TryGetValue(commandName, out var symbol) => new MarkdownMathSymbol(commandName, symbol.RenderText, symbol.IsLargeOperator, CreateSpan(start, _position)),
                _ when FunctionCommands.Contains(commandName) => new MarkdownMathTextRun(commandName, MarkdownMathTextStyle.Operator, CreateSpan(start, _position)),
                _ => new MarkdownMathCommand(commandName, CreateSpan(start, _position))
            };
        }

        private MarkdownMathNode ParseFraction(int start, Func<Parser, bool> shouldStop)
        {
            var numerator = ParseRequiredArgumentExpression("numerator", shouldStop);
            var denominator = ParseRequiredArgumentExpression("denominator", shouldStop);
            return new MarkdownMathFraction(numerator, denominator, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseRoot(int start, Func<Parser, bool> shouldStop)
        {
            MarkdownMathExpression? degree = null;
            SkipOptionalWhitespace();
            if (!IsAtEnd && Peek() == '[')
            {
                var grouped = ParseGroupedExpression('[', ']');
                degree = grouped.Content;
            }

            var radicand = ParseRequiredArgumentExpression("radicand", shouldStop);
            return new MarkdownMathRoot(radicand, degree, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseDelimited(int start)
        {
            SkipOptionalWhitespace();
            var leftDelimiter = ParseDelimiterToken();
            var content = ParseExpression(parser => parser.IsAtEnd || parser.IsRightDelimiterCommand());

            if (!IsRightDelimiterCommand())
            {
                AddDiagnostic("Expected '\\right' to close the delimited math group.", CreateSpan(start, _position));
                return new MarkdownMathDelimited(leftDelimiter, content, ".", CreateSpan(start, _position));
            }

            ConsumeKnownCommand("right");
            SkipOptionalWhitespace();
            var rightDelimiter = ParseDelimiterToken();
            return new MarkdownMathDelimited(leftDelimiter, content, rightDelimiter, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseEnvironment(int start)
        {
            var environmentName = ParseCommandGroupText("environment name");
            if (string.IsNullOrWhiteSpace(environmentName))
            {
                return new MarkdownMathError("\\begin", "Expected an environment name after \\begin.", CreateSpan(start, _position));
            }

            List<IReadOnlyList<MarkdownMathExpression>> rows = [];
            List<MarkdownMathExpression> currentRow = [];

            while (!IsAtEnd)
            {
                if (TryPeekEnvironmentEnd(environmentName, out _))
                {
                    if (currentRow.Count > 0 || rows.Count == 0)
                    {
                        rows.Add(currentRow.ToArray());
                    }

                    ConsumeEnvironmentEnd(environmentName);
                    return new MarkdownMathEnvironment(environmentName, rows, CreateSpan(start, _position));
                }

                var cell = ParseExpression(parser =>
                    parser.IsAtEnd ||
                    parser.Peek() == '&' ||
                    parser.IsEnvironmentRowBreak() ||
                    parser.IsEnvironmentEnd(environmentName));
                currentRow.Add(cell);

                if (IsAtEnd)
                {
                    break;
                }

                if (Peek() == '&')
                {
                    Advance();
                    continue;
                }

                if (IsEnvironmentRowBreak())
                {
                    ConsumeEnvironmentRowBreak();
                    rows.Add(currentRow.ToArray());
                    currentRow = [];
                    continue;
                }
            }

            if (currentRow.Count > 0 || rows.Count == 0)
            {
                rows.Add(currentRow.ToArray());
            }

            AddDiagnostic($"Expected '\\end{{{environmentName}}}' to close the math environment.", CreateSpan(start, _position));
            return new MarkdownMathEnvironment(environmentName, rows, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseTextCommand(int start, MarkdownMathTextStyle style)
        {
            var text = ParseCommandGroupText("text");
            return new MarkdownMathTextRun(text, style, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseHiddenAnnotation(int start)
        {
            _ = ParseCommandGroupText("label");
            return new MarkdownMathTextRun(string.Empty, MarkdownMathTextStyle.Normal, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseTag(int start)
        {
            var text = ParseCommandGroupText("tag");
            return new MarkdownMathTextRun($"({text})", MarkdownMathTextStyle.Operator, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseStyledCommand(int start, string commandName, MarkdownMathTextStyle style, Func<Parser, bool> shouldStop)
        {
            if (style == MarkdownMathTextStyle.Operator)
            {
                var operatorText = ParseCommandGroupText(commandName);
                return new MarkdownMathTextRun(operatorText, style, CreateSpan(start, _position));
            }

            var content = ParseRequiredArgumentExpression(commandName, shouldStop);
            return new MarkdownMathStyledExpression(style, content, CreateSpan(start, _position));
        }

        private MarkdownMathNode ParseAccent(int start, (string AccentText, bool Underline) accent, Func<Parser, bool> shouldStop)
        {
            var baseExpression = ParseRequiredArgumentExpression("accented expression", shouldStop);
            MarkdownMathNode baseNode = baseExpression.Children.Count == 1
                ? baseExpression.Children[0]
                : new MarkdownMathGroupedExpression(baseExpression, baseExpression.Span);
            return new MarkdownMathAccent(accent.AccentText, baseNode, accent.Underline, CreateSpan(start, _position));
        }

        private MarkdownMathExpression ParseRequiredArgumentExpression(string argumentName, Func<Parser, bool> shouldStop)
        {
            SkipOptionalWhitespace();
            if (IsAtEnd)
            {
                AddDiagnostic($"Expected a {argumentName} expression.", CreateSpan(_position, _position));
                return MarkdownMathExpression.Empty;
            }

            if (Peek() == '{')
            {
                var grouped = ParseGroupedExpression('{', '}');
                return grouped.Content;
            }

            var start = _position;
            var next = ParseNextNode(shouldStop);
            return next is null
                ? MarkdownMathExpression.Empty
                : new MarkdownMathExpression([next], CreateSpan(start, _position));
        }

        private string ParseCommandGroupText(string subject)
        {
            SkipOptionalWhitespace();
            if (IsAtEnd || Peek() != '{')
            {
                AddDiagnostic($"Expected '{{...}}' for the {subject}.", CreateSpan(_position, _position));
                return string.Empty;
            }

            Advance();
            var builder = new StringBuilder();
            var depth = 1;
            while (!IsAtEnd && depth > 0)
            {
                var current = Advance();
                if (current == '{')
                {
                    depth++;
                    if (depth > 1)
                    {
                        builder.Append(current);
                    }
                    continue;
                }

                if (current == '}')
                {
                    depth--;
                    if (depth > 0)
                    {
                        builder.Append(current);
                    }
                    continue;
                }

                builder.Append(current);
            }

            if (depth != 0)
            {
                AddDiagnostic($"Expected '}}' to close the {subject}.", CreateSpan(_position, _position));
            }

            return builder.ToString().Trim();
        }

        private string ParseDelimiterToken()
        {
            if (IsAtEnd)
            {
                return ".";
            }

            if (Peek() == '.')
            {
                Advance();
                return ".";
            }

            if (Peek() != '\\')
            {
                return Advance().ToString();
            }

            var start = _position;
            Advance();
            var name = ReadCommandName();
            return SymbolCommands.TryGetValue(name, out var symbol)
                ? symbol.RenderText
                : _source[start.._position];
        }

        private bool IsRightDelimiterCommand()
        {
            return TryPeekCommand("right");
        }

        private bool IsEnvironmentRowBreak()
        {
            return Peek() == '\\' && Peek(1) == '\\';
        }

        private void ConsumeEnvironmentRowBreak()
        {
            if (IsEnvironmentRowBreak())
            {
                Advance();
                Advance();
            }
        }

        private bool IsEnvironmentEnd(string environmentName)
        {
            return TryPeekEnvironmentEnd(environmentName, out _);
        }

        private bool TryPeekEnvironmentEnd(string environmentName, out int endPosition)
        {
            endPosition = _position;
            var position = _position;
            if (!TryReadCommand(position, out var commandName, out position) ||
                !string.Equals(commandName, "end", StringComparison.Ordinal))
            {
                return false;
            }

            SkipOptionalWhitespace(ref position);
            if (position >= _source.Length || _source[position] != '{')
            {
                return false;
            }

            position++;
            var nameStart = position;
            while (position < _source.Length && _source[position] != '}')
            {
                position++;
            }

            if (position >= _source.Length)
            {
                return false;
            }

            var candidate = _source[nameStart..position].Trim();
            if (!string.Equals(candidate, environmentName, StringComparison.Ordinal))
            {
                return false;
            }

            endPosition = position + 1;
            return true;
        }

        private void ConsumeEnvironmentEnd(string environmentName)
        {
            if (!TryPeekEnvironmentEnd(environmentName, out var endPosition))
            {
                return;
            }

            _position = endPosition;
        }

        private bool TryPeekCommand(string commandName)
        {
            return TryReadCommand(_position, out var candidate, out _) &&
                   string.Equals(candidate, commandName, StringComparison.Ordinal);
        }

        private void ConsumeKnownCommand(string commandName)
        {
            if (TryPeekCommand(commandName))
            {
                Advance();
                ReadCommandName();
            }
        }

        private bool TryReadCommand(int position, out string commandName, out int nextPosition)
        {
            commandName = string.Empty;
            nextPosition = position;
            if (position >= _source.Length || _source[position] != '\\')
            {
                return false;
            }

            position++;
            if (position >= _source.Length)
            {
                return false;
            }

            if (!char.IsLetter(_source[position]))
            {
                commandName = _source[position].ToString();
                nextPosition = position + 1;
                return true;
            }

            var start = position;
            while (position < _source.Length && char.IsLetter(_source[position]))
            {
                position++;
            }

            commandName = _source[start..position];
            nextPosition = position;
            return true;
        }

        private string ReadCommandName()
        {
            if (IsAtEnd)
            {
                return string.Empty;
            }

            if (!char.IsLetter(Peek()))
            {
                return Advance().ToString();
            }

            var start = _position;
            while (!IsAtEnd && char.IsLetter(Peek()))
            {
                Advance();
            }

            return _source[start.._position];
        }

        private void SkipOptionalWhitespace()
        {
            SkipOptionalWhitespace(ref _position);
        }

        private void SkipOptionalWhitespace(ref int position)
        {
            while (position < _source.Length && char.IsWhiteSpace(_source[position]))
            {
                position++;
            }
        }

        private void AddDiagnostic(string message, MarkdownMathSourceSpan span)
        {
            _diagnostics.Add(new MarkdownMathDiagnostic(message, span));
        }

        private MarkdownMathSourceSpan CreateSpan(int start, int end)
        {
            var normalizedStart = SystemMath.Clamp(start, 0, _source.Length);
            var normalizedEnd = SystemMath.Clamp(end, normalizedStart, _source.Length);
            return new MarkdownMathSourceSpan(normalizedStart, normalizedEnd - normalizedStart);
        }

        public bool IsAtEnd => _position >= _source.Length;

        private char Peek(int offset = 0)
        {
            var index = _position + offset;
            return index >= 0 && index < _source.Length ? _source[index] : '\0';
        }

        private char Advance()
        {
            return _source[_position++];
        }
    }
}
