using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ProMarkdown.Plugin.SyntaxHighlighting;

internal static class MarkdownBuiltInSyntaxHighlighting
{
    private static readonly IBrush CodeKeywordForeground = new SolidColorBrush(Color.Parse("#CF222E"));
    private static readonly IBrush CodeTypeForeground = new SolidColorBrush(Color.Parse("#8250DF"));
    private static readonly IBrush CodeStringForeground = new SolidColorBrush(Color.Parse("#0A3069"));
    private static readonly IBrush CodeCommentForeground = new SolidColorBrush(Color.Parse("#6E7781"));
    private static readonly IBrush CodeNumberForeground = new SolidColorBrush(Color.Parse("#0550AE"));
    private static readonly IBrush CodePropertyForeground = new SolidColorBrush(Color.Parse("#953800"));
    private static readonly IBrush CodeTagForeground = new SolidColorBrush(Color.Parse("#1A7F37"));
    private static readonly IBrush CodeAttributeForeground = new SolidColorBrush(Color.Parse("#9A6700"));
    private static readonly IBrush CodePunctuationForeground = new SolidColorBrush(Color.Parse("#57606A"));
    private static readonly HashSet<string> CommonCodeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "abstract", "as", "async", "await", "base", "break", "case", "catch", "class", "const", "continue",
        "default", "delegate", "do", "else", "enum", "event", "explicit", "export", "extends", "extern",
        "false", "finally", "fixed", "for", "foreach", "from", "function", "goto", "if", "implicit",
        "implements", "import", "in", "interface", "internal", "is", "let", "lock", "namespace", "new",
        "null", "operator", "out", "override", "package", "params", "private", "protected", "public",
        "readonly", "record", "ref", "return", "sealed", "static", "struct", "super", "switch", "this",
        "throw", "true", "try", "typeof", "unchecked", "unsafe", "using", "var", "virtual", "void",
        "volatile", "while", "with", "yield"
    };
    private static readonly HashSet<string> TypeLikeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "bool", "byte", "char", "date", "datetime", "decimal", "double", "dynamic", "float", "guid", "int",
        "long", "nint", "nuint", "object", "sbyte", "short", "string", "task", "uint", "ulong", "ushort"
    };
    private static readonly HashSet<string> LiteralWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "false", "null", "true", "undefined"
    };
    private static readonly HashSet<string> ShellKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "case", "do", "done", "echo", "elif", "else", "esac", "export", "fi", "for", "function", "if",
        "in", "local", "readonly", "return", "select", "set", "then", "until", "while"
    };
    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "as", "asc", "by", "case", "create", "delete", "desc", "distinct", "drop", "else", "end",
        "from", "group", "having", "inner", "insert", "into", "join", "left", "like", "limit", "not",
        "null", "on", "or", "order", "outer", "right", "select", "set", "table", "then", "union", "update",
        "values", "when", "where"
    };

    public static InlineCollection CreateHighlightedInlines(string code, string? languageHint)
    {
        var inlines = new InlineCollection();
        var highlightedLines = HighlightCode(code, languageHint);
        for (var lineIndex = 0; lineIndex < highlightedLines.Count; lineIndex++)
        {
            foreach (var span in highlightedLines[lineIndex].Spans)
            {
                inlines.Add(CreateRun(span));
            }

            if (lineIndex < highlightedLines.Count - 1)
            {
                inlines.Add(new LineBreak());
            }
        }

        return inlines;
    }

    private static Run CreateRun(HighlightedCodeSpan span)
    {
        var run = new Run(span.Text);
        if (span.Foreground is not null)
        {
            run.Foreground = span.Foreground;
        }

        if (span.FontWeight.HasValue)
        {
            run.FontWeight = span.FontWeight.Value;
        }

        return run;
    }

    private static List<HighlightedCodeLine> HighlightCode(string code, string? languageHint)
    {
        var normalized = ProMarkdown.Services.MarkdownCodeBlockRendering.NormalizeLanguageHint(languageHint);
        var lines = code.Split('\n', StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return [new HighlightedCodeLine([])];
        }

        return ResolveCodeLanguageFamily(normalized) switch
        {
            CodeLanguageFamily.Json => HighlightJson(lines),
            CodeLanguageFamily.Markup => HighlightMarkup(lines),
            CodeLanguageFamily.Shell => HighlightShell(lines),
            CodeLanguageFamily.Sql => HighlightSql(lines),
            CodeLanguageFamily.CStyle => HighlightCStyle(lines),
            _ => HighlightPlainText(lines)
        };
    }

    private static List<HighlightedCodeLine> HighlightPlainText(IEnumerable<string> lines)
    {
        return lines
            .Select(static line => new HighlightedCodeLine([new HighlightedCodeSpan(line, null, null)]))
            .ToList();
    }

    private static List<HighlightedCodeLine> HighlightCStyle(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);
        var inBlockComment = false;

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (inBlockComment)
                {
                    var end = line.IndexOf("*/", index, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        index = line.Length;
                        continue;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 2)], CodeCommentForeground);
                    index = end + 2;
                    inBlockComment = false;
                    continue;
                }

                if (line[index] == '#' && line[..index].All(char.IsWhiteSpace))
                {
                    AddHighlightedSpan(spans, line[index..], CodeKeywordForeground, FontWeight.SemiBold);
                    break;
                }

                if (MatchesToken(line, index, "//"))
                {
                    AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                    break;
                }

                if (MatchesToken(line, index, "/*"))
                {
                    var end = line.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        inBlockComment = true;
                        break;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 2)], CodeCommentForeground);
                    index = end + 2;
                    continue;
                }

                if (line[index] == '@' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    var end = FindVerbatimStringEnd(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeStringForeground);
                    index = end;
                    continue;
                }

                if (line[index] is '"' or '\'')
                {
                    var end = FindQuotedStringEnd(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeStringForeground);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = ConsumeNumber(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeNumberForeground);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(line[index]))
                {
                    var end = ConsumeIdentifier(line, index);
                    var word = line[index..end];
                    var (foreground, fontWeight) = ClassifyCodeIdentifier(word);
                    AddHighlightedSpan(spans, word, foreground, fontWeight);
                    index = end;
                    continue;
                }

                AddHighlightedSpan(
                    spans,
                    line[index].ToString(),
                    char.IsPunctuation(line[index]) && !char.IsWhiteSpace(line[index]) ? CodePunctuationForeground : null);
                index++;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static List<HighlightedCodeLine> HighlightJson(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (MatchesToken(line, index, "//"))
                {
                    AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                    break;
                }

                if (line[index] == '"')
                {
                    var end = FindQuotedStringEnd(line, index);
                    var lookahead = SkipWhitespace(line, end);
                    var brush = lookahead < line.Length && line[lookahead] == ':' ? CodePropertyForeground : CodeStringForeground;
                    AddHighlightedSpan(spans, line[index..end], brush);
                    index = end;
                    continue;
                }

                if (line[index] == '-' || char.IsDigit(line[index]))
                {
                    var end = ConsumeJsonNumber(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeNumberForeground);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(line[index]))
                {
                    var end = ConsumeIdentifier(line, index);
                    var word = line[index..end];
                    var isLiteral = LiteralWords.Contains(word);
                    AddHighlightedSpan(spans, word, isLiteral ? CodeKeywordForeground : null, isLiteral ? FontWeight.SemiBold : null);
                    index = end;
                    continue;
                }

                AddHighlightedSpan(
                    spans,
                    line[index].ToString(),
                    "{}[]:,".Contains(line[index], StringComparison.Ordinal) ? CodePunctuationForeground : null);
                index++;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static List<HighlightedCodeLine> HighlightMarkup(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);
        var inComment = false;

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (inComment)
                {
                    var end = line.IndexOf("-->", index, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        index = line.Length;
                        continue;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 3)], CodeCommentForeground);
                    index = end + 3;
                    inComment = false;
                    continue;
                }

                if (MatchesToken(line, index, "<!--"))
                {
                    var end = line.IndexOf("-->", index + 4, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        inComment = true;
                        break;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 3)], CodeCommentForeground);
                    index = end + 3;
                    continue;
                }

                if (line[index] == '<')
                {
                    AddHighlightedSpan(spans, "<", CodePunctuationForeground);
                    index++;

                    if (index < line.Length && line[index] == '/')
                    {
                        AddHighlightedSpan(spans, "/", CodePunctuationForeground);
                        index++;
                    }

                    var tagEnd = index;
                    while (tagEnd < line.Length && (char.IsLetterOrDigit(line[tagEnd]) || line[tagEnd] is ':' or '-' or '_'))
                    {
                        tagEnd++;
                    }

                    if (tagEnd > index)
                    {
                        AddHighlightedSpan(spans, line[index..tagEnd], CodeTagForeground, FontWeight.SemiBold);
                        index = tagEnd;
                    }

                    while (index < line.Length && line[index] != '>')
                    {
                        if (char.IsWhiteSpace(line[index]))
                        {
                            AddHighlightedSpan(spans, line[index].ToString());
                            index++;
                            continue;
                        }

                        if (line[index] == '/')
                        {
                            AddHighlightedSpan(spans, "/", CodePunctuationForeground);
                            index++;
                            continue;
                        }

                        var attributeStart = index;
                        while (index < line.Length && (char.IsLetterOrDigit(line[index]) || line[index] is ':' or '-' or '_'))
                        {
                            index++;
                        }

                        if (index > attributeStart)
                        {
                            AddHighlightedSpan(spans, line[attributeStart..index], CodeAttributeForeground);
                        }

                        var whitespaceStart = index;
                        index = SkipWhitespace(line, index);
                        if (index > whitespaceStart)
                        {
                            AddHighlightedSpan(spans, line[whitespaceStart..index]);
                        }

                        if (index < line.Length && line[index] == '=')
                        {
                            AddHighlightedSpan(spans, "=", CodePunctuationForeground);
                            index++;
                            var valueWhitespaceStart = index;
                            index = SkipWhitespace(line, index);
                            if (index > valueWhitespaceStart)
                            {
                                AddHighlightedSpan(spans, line[valueWhitespaceStart..index]);
                            }

                            if (index < line.Length && line[index] is '"' or '\'')
                            {
                                var valueEnd = FindQuotedStringEnd(line, index);
                                AddHighlightedSpan(spans, line[index..valueEnd], CodeStringForeground);
                                index = valueEnd;
                            }
                        }

                        if (index == attributeStart)
                        {
                            AddHighlightedSpan(spans, line[index].ToString(), CodePunctuationForeground);
                            index++;
                        }
                    }

                    if (index < line.Length && line[index] == '>')
                    {
                        AddHighlightedSpan(spans, ">", CodePunctuationForeground);
                        index++;
                    }

                    continue;
                }

                var nextTag = line.IndexOf('<', index);
                if (nextTag < 0)
                {
                    AddHighlightedSpan(spans, line[index..]);
                    break;
                }

                AddHighlightedSpan(spans, line[index..nextTag]);
                index = nextTag;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static List<HighlightedCodeLine> HighlightShell(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (line[index] == '#' && line[..index].All(static ch => char.IsWhiteSpace(ch)))
                {
                    AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                    break;
                }

                if (line[index] is '"' or '\'')
                {
                    var end = FindQuotedStringEnd(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeStringForeground);
                    index = end;
                    continue;
                }

                if (line[index] == '$')
                {
                    var end = index + 1;
                    while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '_' or '{' or '}'))
                    {
                        end++;
                    }

                    AddHighlightedSpan(spans, line[index..end], CodePropertyForeground);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = ConsumeNumber(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeNumberForeground);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(line[index]))
                {
                    var end = ConsumeIdentifier(line, index);
                    var word = line[index..end];
                    var isKeyword = ShellKeywords.Contains(word);
                    AddHighlightedSpan(spans, word, isKeyword ? CodeKeywordForeground : null, isKeyword ? FontWeight.SemiBold : null);
                    index = end;
                    continue;
                }

                AddHighlightedSpan(spans, line[index].ToString(), char.IsPunctuation(line[index]) && !char.IsWhiteSpace(line[index]) ? CodePunctuationForeground : null);
                index++;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static List<HighlightedCodeLine> HighlightSql(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);
        var inBlockComment = false;

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (inBlockComment)
                {
                    var end = line.IndexOf("*/", index, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        index = line.Length;
                        continue;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 2)], CodeCommentForeground);
                    index = end + 2;
                    inBlockComment = false;
                    continue;
                }

                if (MatchesToken(line, index, "--"))
                {
                    AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                    break;
                }

                if (MatchesToken(line, index, "/*"))
                {
                    var end = line.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        inBlockComment = true;
                        break;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 2)], CodeCommentForeground);
                    index = end + 2;
                    continue;
                }

                if (line[index] == '\'')
                {
                    var end = FindQuotedStringEnd(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeStringForeground);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = ConsumeNumber(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeNumberForeground);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(line[index]))
                {
                    var end = ConsumeIdentifier(line, index);
                    var word = line[index..end];
                    var isKeyword = SqlKeywords.Contains(word);
                    AddHighlightedSpan(spans, word, isKeyword ? CodeKeywordForeground : null, isKeyword ? FontWeight.SemiBold : null);
                    index = end;
                    continue;
                }

                AddHighlightedSpan(spans, line[index].ToString(), char.IsPunctuation(line[index]) && !char.IsWhiteSpace(line[index]) ? CodePunctuationForeground : null);
                index++;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static (IBrush? Foreground, FontWeight? FontWeight) ClassifyCodeIdentifier(string word)
    {
        if (LiteralWords.Contains(word) || CommonCodeKeywords.Contains(word))
        {
            return (CodeKeywordForeground, FontWeight.SemiBold);
        }

        return TypeLikeWords.Contains(word) || char.IsUpper(word[0])
            ? (CodeTypeForeground, null)
            : (null, null);
    }

    private static void AddHighlightedSpan(List<HighlightedCodeSpan> spans, string text, IBrush? foreground = null, FontWeight? fontWeight = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (spans.Count > 0)
        {
            var previous = spans[^1];
            if (ReferenceEquals(previous.Foreground, foreground) && previous.FontWeight == fontWeight)
            {
                spans[^1] = previous with { Text = previous.Text + text };
                return;
            }
        }

        spans.Add(new HighlightedCodeSpan(text, foreground, fontWeight));
    }

    private static bool MatchesToken(string line, int index, string token)
    {
        return index + token.Length <= line.Length &&
               string.Compare(line, index, token, 0, token.Length, StringComparison.Ordinal) == 0;
    }

    private static int SkipWhitespace(string line, int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsIdentifierStart(char ch)
    {
        return char.IsLetter(ch) || ch is '_' or '$';
    }

    private static int ConsumeIdentifier(string line, int index)
    {
        var end = index + 1;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '_' or '$'))
        {
            end++;
        }

        return end;
    }

    private static int ConsumeNumber(string line, int index)
    {
        var end = index + 1;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '.' or '_' or 'x' or 'X'))
        {
            end++;
        }

        return end;
    }

    private static int ConsumeJsonNumber(string line, int index)
    {
        var end = index + 1;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] is '.' or 'e' or 'E' or '+' or '-'))
        {
            end++;
        }

        return end;
    }

    private static int FindQuotedStringEnd(string line, int startIndex)
    {
        var delimiter = line[startIndex];
        var end = startIndex + 1;

        while (end < line.Length)
        {
            if (line[end] == '\\')
            {
                end = Math.Min(end + 2, line.Length);
                continue;
            }

            end++;
            if (line[end - 1] == delimiter)
            {
                break;
            }
        }

        return end;
    }

    private static int FindVerbatimStringEnd(string line, int startIndex)
    {
        var end = startIndex + 2;
        while (end < line.Length)
        {
            if (line[end] == '"')
            {
                if (end + 1 < line.Length && line[end + 1] == '"')
                {
                    end += 2;
                    continue;
                }

                end++;
                break;
            }

            end++;
        }

        return end;
    }

    private static CodeLanguageFamily ResolveCodeLanguageFamily(string normalizedLanguageHint)
    {
        return normalizedLanguageHint switch
        {
            "text" or "plain" or "plaintext" or "md" or "markdown" => CodeLanguageFamily.PlainText,
            "json" or "jsonc" => CodeLanguageFamily.Json,
            "xml" or "xaml" or "axaml" or "html" or "htm" or "svg" => CodeLanguageFamily.Markup,
            "bash" or "sh" or "shell" or "zsh" or "pwsh" or "powershell" or "ps1" => CodeLanguageFamily.Shell,
            "sql" or "sqlite" or "postgres" or "postgresql" or "mysql" or "tsql" => CodeLanguageFamily.Sql,
            "cs" or "csharp" or "c#" or "js" or "javascript" or "ts" or "typescript" or "tsx" or "jsx" or
            "java" or "go" or "rust" or "rs" or "cpp" or "c++" or "c" or "h" or "hpp" or "swift" or "kotlin" =>
                CodeLanguageFamily.CStyle,
            _ => CodeLanguageFamily.CStyle
        };
    }

    private sealed record HighlightedCodeLine(List<HighlightedCodeSpan> Spans);

    private sealed record HighlightedCodeSpan(string Text, IBrush? Foreground, FontWeight? FontWeight);

    private enum CodeLanguageFamily
    {
        PlainText,
        CStyle,
        Json,
        Markup,
        Shell,
        Sql
    }
}
