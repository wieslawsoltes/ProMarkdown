using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using CodexGui.Markdown.Services;
using Markdig.Syntax;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;
using TextMateSharp.Themes;
using TextMateFontStyle = TextMateSharp.Themes.FontStyle;

namespace CodexGui.Markdown.Plugin.TextMate;

public sealed class TextMateMarkdownPlugin : IMarkdownPlugin
{
    public const string TextMateCodeEditorId = "textmate-code-editor";

    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new TextMateCodeBlockRenderingPlugin())
            .AddEditorPlugin(new TextMateCodeBlockEditorPlugin());
    }
}

internal sealed class TextMateCodeBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly RegistryOptions RegistryOptions = new(ThemeName.LightPlus);
    private static readonly Theme Theme = Theme.CreateFromRawTheme(RegistryOptions.LoadTheme(ThemeName.LightPlus), RegistryOptions);
    private static readonly BalancedBracketSelectors EmptyBalancedBracketSelectors = new([], []);
    private static readonly IBrush SurfaceBackground = new SolidColorBrush(Color.Parse("#F6F8FA"));
    private static readonly IBrush SurfaceBorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush HeaderBackground = new SolidColorBrush(Color.Parse("#EAEEF2"));
    private static readonly IBrush MetaForeground = new SolidColorBrush(Color.Parse("#6E7781"));
    private static readonly HashSet<string> MermaidLanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "diagram-mermaid",
        "mmd",
        "mermaid",
        "mermaidjs"
    };
    private static readonly Dictionary<string, string> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["axaml"] = ".axaml",
        ["bash"] = ".sh",
        ["c#"] = ".cs",
        ["cs"] = ".cs",
        ["csharp"] = ".cs",
        ["css"] = ".css",
        ["go"] = ".go",
        ["html"] = ".html",
        ["htm"] = ".html",
        ["java"] = ".java",
        ["javascript"] = ".js",
        ["js"] = ".js",
        ["json"] = ".json",
        ["jsonc"] = ".json",
        ["markdown"] = ".md",
        ["md"] = ".md",
        ["powershell"] = ".ps1",
        ["ps1"] = ".ps1",
        ["pwsh"] = ".ps1",
        ["python"] = ".py",
        ["py"] = ".py",
        ["rs"] = ".rs",
        ["rust"] = ".rs",
        ["shell"] = ".sh",
        ["sh"] = ".sh",
        ["sql"] = ".sql",
        ["ts"] = ".ts",
        ["tsx"] = ".tsx",
        ["typescript"] = ".ts",
        ["xaml"] = ".xaml",
        ["xml"] = ".xml",
        ["yml"] = ".yml",
        ["yaml"] = ".yml"
    };
    private static readonly Dictionary<string, IBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public int Order => -10;

    public bool CanRender(Block block) => block is CodeBlock;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        if (context.Block is not CodeBlock codeBlock)
        {
            return false;
        }

        var languageHint = codeBlock is FencedCodeBlock fencedCode ? fencedCode.Info : null;
        if (IsMermaidLanguage(languageHint))
        {
            return false;
        }

        var grammarScope = ResolveScopeName(languageHint);
        if (string.IsNullOrWhiteSpace(grammarScope))
        {
            return false;
        }

        var code = MarkdownCodeBlockRendering.NormalizeCode(codeBlock.Lines.ToString());
        var inlines = TokenizeCode(code, grammarScope, context.RenderContext);
        if (inlines is null)
        {
            return false;
        }

        var lineCount = string.IsNullOrEmpty(code) ? 0 : code.Split('\n', StringSplitOptions.None).Length;
        var codeSurface = MarkdownCodeBlockRendering.CreateSurface(
            codeBlock,
            code,
            inlines,
            languageHint,
            lineCount == 1 ? "1 line • TextMate" : $"{lineCount} lines • TextMate",
            context.RenderContext,
            textForeground: context.RenderContext.Foreground);
        context.AddBlockControl(codeSurface.Control, codeSurface.HitTestHandler);
        return true;
    }

    private static TextMateCodeSurface CreateCodeSurface(
        InlineCollection inlines,
        string? languageHint,
        int lineCount,
        MarkdownRenderContext renderContext)
    {
        var codeText = new SelectableTextBlock
        {
            FontFamily = MonospaceFamily,
            FontSize = Math.Max(renderContext.FontSize - 1, 12),
            Foreground = renderContext.Foreground,
            TextWrapping = renderContext.TextWrapping == TextWrapping.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Inlines = inlines
        };

        Control codeContent = renderContext.TextWrapping == TextWrapping.NoWrap
            ? new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = codeText
            }
            : codeText;

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var header = CreateHeader(languageHint, lineCount);
        layout.Children.Add(header);

        var body = new Border
        {
            Padding = new Thickness(12, 10),
            Child = codeContent
        };
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var root = new Border
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = layout
        };
        return new TextMateCodeSurface(
            root,
            request => CreateHitTestResult(request, header, body));
    }

    private static Border CreateHeader(string? languageHint, int lineCount)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12
        };

        grid.Children.Add(new TextBlock
        {
            Text = FormatLanguageLabel(languageHint),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var meta = new TextBlock
        {
            Text = lineCount == 1 ? "1 line • TextMate" : $"{lineCount} lines • TextMate",
            Foreground = MetaForeground,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(meta, 1);
        grid.Children.Add(meta);

        return new Border
        {
            Background = HeaderBackground,
            Padding = new Thickness(12, 8),
            Child = grid
        };
    }

    private static MarkdownVisualHitTestResult CreateHitTestResult(
        MarkdownVisualHitTestRequest request,
        Control header,
        Control body)
    {
        if (request.Control.TranslatePoint(request.LocalPoint, body) is { } bodyPoint &&
            new Rect(body.Bounds.Size).Contains(bodyPoint))
        {
            return new MarkdownVisualHitTestResult
            {
                LocalHighlightRects = ResolveControlBounds(body, request.Control)
            };
        }

        if (request.Control.TranslatePoint(request.LocalPoint, header) is { } headerPoint &&
            new Rect(header.Bounds.Size).Contains(headerPoint))
        {
            return new MarkdownVisualHitTestResult
            {
                LocalHighlightRects = ResolveControlBounds(header, request.Control)
            };
        }

        return new MarkdownVisualHitTestResult
        {
            LocalHighlightRects = ResolveControlBounds(request.Control, request.Control)
        };
    }

    private static IReadOnlyList<Rect> ResolveControlBounds(Control control, Control root)
    {
        if (control.TranslatePoint(default, root) is not { } topLeft)
        {
            return Array.Empty<Rect>();
        }

        return [new Rect(topLeft, control.Bounds.Size)];
    }

    private static InlineCollection? TokenizeCode(string code, string grammarScope, MarkdownRenderContext renderContext)
    {
        var grammar = TryCreateGrammar(grammarScope);
        if (grammar is null)
        {
            return null;
        }

        var inlines = new InlineCollection();
        IStateStack? state = null;
        var lines = code.Split('\n', StringSplitOptions.None);

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var result = state is null
                ? grammar.TokenizeLine2(line)
                : grammar.TokenizeLine2(line, state, TimeSpan.FromMilliseconds(100));
            state = result.RuleStack;

            if (result.Tokens.Length == 0)
            {
                inlines.Add(CreateRun(line, renderContext.Foreground, null, TextMateFontStyle.None));
            }
            else
            {
                for (var tokenIndex = 0; tokenIndex + 1 < result.Tokens.Length; tokenIndex += 2)
                {
                    var start = result.Tokens[tokenIndex];
                    var metadata = result.Tokens[tokenIndex + 1];
                    var end = tokenIndex + 2 < result.Tokens.Length ? result.Tokens[tokenIndex + 2] : line.Length;
                    if (start >= end || start < 0 || end > line.Length)
                    {
                        continue;
                    }

                    var tokenText = line[start..end];
                    var foreground = ResolveBrush(Theme.GetColor(EncodedTokenAttributes.GetForeground(metadata)), renderContext.Foreground);
                    var background = ResolveOptionalBackground(Theme.GetColor(EncodedTokenAttributes.GetBackground(metadata)));
                    var style = EncodedTokenAttributes.GetFontStyle(metadata);
                    inlines.Add(CreateRun(tokenText, foreground, background, style));
                }
            }

            if (lineIndex < lines.Length - 1)
            {
                inlines.Add(new LineBreak());
            }
        }

        return inlines;
    }

    private static IGrammar? TryCreateGrammar(string grammarScope)
    {
        var rawGrammar = RegistryOptions.GetGrammar(grammarScope);
        if (rawGrammar is null)
        {
            return null;
        }

        var registry = new SyncRegistry(Theme);
        registry.AddGrammar(rawGrammar, RegistryOptions.GetInjections(grammarScope));
        return registry.GrammarForScopeName(
            grammarScope,
            0,
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
            EmptyBalancedBracketSelectors);
    }

    private static Run CreateRun(string text, IBrush? foreground, IBrush? background, TextMateFontStyle style)
    {
        var run = new Run(text);
        if (foreground is not null)
        {
            run.Foreground = foreground;
        }

        if (background is not null)
        {
            run.Background = background;
        }

        if (HasStyle(style, TextMateFontStyle.Bold))
        {
            run.FontWeight = FontWeight.SemiBold;
        }

        if (HasStyle(style, TextMateFontStyle.Italic))
        {
            run.FontStyle = Avalonia.Media.FontStyle.Italic;
        }

        if (HasStyle(style, TextMateFontStyle.Underline))
        {
            run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
        }
        else if (HasStyle(style, TextMateFontStyle.Strikethrough))
        {
            run.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
        }

        return run;
    }

    private static IBrush? ResolveBrush(string? colorText, IBrush? fallback)
    {
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return fallback;
        }

        if (BrushCache.TryGetValue(colorText, out var brush))
        {
            return brush;
        }

        brush = new SolidColorBrush(Color.Parse(colorText));
        BrushCache[colorText] = brush;
        return brush;
    }

    private static IBrush? ResolveOptionalBackground(string? colorText)
    {
        if (string.IsNullOrWhiteSpace(colorText))
        {
            return null;
        }

        var normalized = colorText.Trim();
        return normalized.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("#FFF", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("white", StringComparison.OrdinalIgnoreCase)
            ? null
            : ResolveBrush(normalized, null);
    }

    private static bool HasStyle(TextMateFontStyle value, TextMateFontStyle flag)
    {
        return (value & flag) == flag;
    }

    internal static string? ResolveScopeName(string? languageHint)
    {
        var extension = ResolveExtension(languageHint);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var language = RegistryOptions.GetLanguageByExtension(extension);
        return language is null ? null : RegistryOptions.GetScopeByLanguageId(language.Id);
    }

    internal static string? ResolveExtension(string? languageHint)
    {
        var normalized = MarkdownCodeBlockRendering.NormalizeLanguageHint(languageHint);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (ExtensionMap.TryGetValue(normalized, out var mappedExtension))
        {
            return mappedExtension;
        }

        return normalized.StartsWith(".", StringComparison.Ordinal) ? normalized : $".{normalized}";
    }

    internal static bool IsMermaidLanguage(string? languageHint)
    {
        return MermaidLanguageAliases.Contains(MarkdownCodeBlockRendering.NormalizeLanguageHint(languageHint));
    }

    internal static string NormalizeCode(string code)
    {
        return code
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');
    }

    internal static string NormalizeLanguageHint(string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(languageHint))
        {
            return string.Empty;
        }

        var trimmed = languageHint.Trim();
        var separatorIndex = trimmed.IndexOfAny([' ', '\t', ',', ';', '{', '(']);
        var normalized = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        return normalized.Trim().Trim('.').ToLowerInvariant();
    }

    internal static string FormatLanguageLabel(string? languageHint)
    {
        return NormalizeLanguageHint(languageHint) switch
        {
            "axaml" => "AXAML",
            "c#" or "cs" or "csharp" => "C#",
            "html" or "htm" => "HTML",
            "javascript" or "js" => "JavaScript",
            "json" or "jsonc" => "JSON",
            "markdown" or "md" => "Markdown",
            "powershell" or "ps1" or "pwsh" => "PowerShell",
            "python" or "py" => "Python",
            "rs" or "rust" => "Rust",
            "shell" or "sh" or "bash" => "Shell",
            "sql" => "SQL",
            "ts" or "typescript" => "TypeScript",
            "tsx" => "TSX",
            "xaml" => "XAML",
            "xml" => "XML",
            "yaml" or "yml" => "YAML",
            var other when !string.IsNullOrWhiteSpace(other) => other.ToUpperInvariant(),
            _ => "Code"
        };
    }

    private sealed record TextMateCodeSurface(Control Control, MarkdownVisualHitTestHandler? HitTestHandler);
}

internal sealed class TextMateCodeBlockEditorPlugin : IMarkdownEditorPlugin
{
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly RegistryOptions EditorRegistryOptions = new(ThemeName.LightPlus);

    public string EditorId => TextMateMarkdownPlugin.TextMateCodeEditorId;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Code;

    public int Order => 20;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<CodeBlock>(out var codeBlock, out var depth) ||
            codeBlock is null ||
            TextMateCodeBlockRenderingPlugin.IsMermaidLanguage(codeBlock is FencedCodeBlock fencedCode ? fencedCode.Info : null))
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(codeBlock.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(codeBlock, sourceSpan, codeBlock.Line, codeBlock.Column),
            depth,
            "TextMate code block");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        if (context.Node is not CodeBlock codeBlock)
        {
            return null;
        }

        var languageTextBox = MarkdownEditorUiFactory.CreateTextEditor(
            codeBlock is FencedCodeBlock fencedCode ? TextMateCodeBlockRenderingPlugin.NormalizeLanguageHint(fencedCode.Info) : string.Empty,
            acceptsReturn: false,
            minHeight: 40);
        languageTextBox.Width = 180;
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(languageTextBox, context.RenderContext.FontSize);
        }

        var editor = new TextEditor
        {
            Text = TextMateCodeBlockRenderingPlugin.NormalizeCode(codeBlock.Lines.ToString()),
            FontFamily = MonospaceFamily,
            FontSize = Math.Max(context.RenderContext.FontSize - 1, 12),
            ShowLineNumbers = true,
            WordWrap = context.RenderContext.TextWrapping != TextWrapping.NoWrap,
            MinHeight = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var installation = editor.InstallTextMate(EditorRegistryOptions);
        context.TrackResource(new DelegateDisposable(installation.Dispose));

        void ApplyGrammar()
        {
            var grammarScope = TextMateCodeBlockRenderingPlugin.ResolveScopeName(languageTextBox.Text);
            if (!string.IsNullOrWhiteSpace(grammarScope))
            {
                installation.SetGrammar(grammarScope);
            }
        }

        languageTextBox.TextChanged += (_, _) => ApplyGrammar();
        ApplyGrammar();

        var settingsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                MarkdownEditorUiFactory.CreateFieldLabel("Language"),
                languageTextBox
            }
        };

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit the code fence with AvaloniaEdit + TextMate highlighting and apply the generated fenced block back into the markdown source."));
        }

        string BuildCurrentMarkdown() => BuildCodeFence(languageTextBox.Text ?? string.Empty, editor.Text ?? string.Empty);

        body.Children.Add(settingsRow);
        body.Children.Add(new Border
        {
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = editor
        });

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit code block",
            "TextMate code editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            editor,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }

    private static string BuildCodeFence(string languageHint, string code)
    {
        var normalizedCode = TextMateCodeBlockRenderingPlugin.NormalizeCode(code);
        var normalizedLanguage = TextMateCodeBlockRenderingPlugin.NormalizeLanguageHint(languageHint);
        var fence = normalizedCode.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
        return string.IsNullOrWhiteSpace(normalizedLanguage)
            ? $"{fence}\n{normalizedCode}\n{fence}"
            : $"{fence}{normalizedLanguage}\n{normalizedCode}\n{fence}";
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
