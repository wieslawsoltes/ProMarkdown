using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using System.Collections;
using ProMarkdown.Services;
using Markdig.Syntax;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;
using TextMateSharp.Themes;
using TextMateFontStyle = TextMateSharp.Themes.FontStyle;

namespace ProMarkdown.Plugin.TextMate;

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
    private static readonly BalancedBracketSelectors EmptyBalancedBracketSelectors = new([], []);
    private static readonly IReadOnlyDictionary<string, SemanticForegroundKind> SemanticColors =
        new Dictionary<string, SemanticForegroundKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["#010101"] = SemanticForegroundKind.Foreground,
            ["#020202"] = SemanticForegroundKind.Comment,
            ["#030303"] = SemanticForegroundKind.String,
            ["#040404"] = SemanticForegroundKind.Number,
            ["#050505"] = SemanticForegroundKind.Tag,
            ["#060606"] = SemanticForegroundKind.Attribute,
            ["#070707"] = SemanticForegroundKind.Property,
            ["#080808"] = SemanticForegroundKind.Type,
            ["#090909"] = SemanticForegroundKind.Keyword,
            ["#0A0A0A"] = SemanticForegroundKind.Punctuation
        };
    private static readonly (string Scope, SemanticForegroundKind Kind)[] SemanticScopeRules =
    [
        ("comment", SemanticForegroundKind.Comment),
        ("string", SemanticForegroundKind.String),
        ("constant.character", SemanticForegroundKind.String),
        ("constant.numeric", SemanticForegroundKind.Number),
        ("entity.name.tag", SemanticForegroundKind.Tag),
        ("entity.other.attribute-name", SemanticForegroundKind.Attribute),
        ("variable.other.property", SemanticForegroundKind.Property),
        ("variable.other.member", SemanticForegroundKind.Property),
        ("support.variable.property", SemanticForegroundKind.Property),
        ("support.type.property-name", SemanticForegroundKind.Property),
        ("entity.name.function", SemanticForegroundKind.Property),
        ("support.function", SemanticForegroundKind.Property),
        ("entity.name.type", SemanticForegroundKind.Type),
        ("entity.name.class", SemanticForegroundKind.Type),
        ("entity.name.namespace", SemanticForegroundKind.Type),
        ("support.type", SemanticForegroundKind.Type),
        ("support.class", SemanticForegroundKind.Type),
        ("keyword.type", SemanticForegroundKind.Type),
        ("storage.type", SemanticForegroundKind.Type),
        ("keyword", SemanticForegroundKind.Keyword),
        ("storage.modifier", SemanticForegroundKind.Keyword),
        ("constant.language", SemanticForegroundKind.Keyword),
        ("punctuation", SemanticForegroundKind.Punctuation)
    ];
    private static readonly TextMateThemeResources LightTheme = CreateThemeResources(ThemeName.LightPlus);
    private static readonly TextMateThemeResources DarkTheme = CreateThemeResources(ThemeName.DarkPlus);
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
        var palette = context.RenderContext.ThemePalette ??
                      MarkdownThemePalette.Resolve(context.RenderContext.Foreground);
        var codeSurface = MarkdownCodeBlockRendering.CreateSurface(
            codeBlock,
            code,
            inlines,
            languageHint,
            lineCount == 1 ? "1 line • TextMate" : $"{lineCount} lines • TextMate",
            context.RenderContext,
            textForeground: palette.Foreground,
            metaForeground: palette.MutedForeground);
        context.AddBlockControl(codeSurface.Control, codeSurface.HitTestHandler);
        return true;
    }

    private static InlineCollection? TokenizeCode(string code, string grammarScope, MarkdownRenderContext renderContext)
    {
        var palette = renderContext.ThemePalette ?? MarkdownThemePalette.Resolve(renderContext.Foreground);
        var theme = palette.IsDark ? DarkTheme : LightTheme;
        var grammar = TryCreateGrammar(grammarScope, theme);
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
                inlines.Add(CreateRun(line, palette.Foreground, TextMateFontStyle.None));
            }
            else
            {
                for (var tokenIndex = 0; tokenIndex + 1 < result.Tokens.Length; tokenIndex += 2)
                {
                    var start = result.Tokens[tokenIndex];
                    var metadata = result.Tokens[tokenIndex + 1];
                    var end = tokenIndex + 2 < result.Tokens.Length
                        ? result.Tokens[tokenIndex + 2]
                        : line.Length;
                    if (start >= end || start < 0 || end > line.Length)
                    {
                        continue;
                    }

                    var tokenText = line[start..end];
                    var style = EncodedTokenAttributes.GetFontStyle(metadata);
                    inlines.Add(CreateRun(
                        tokenText,
                        ResolveSemanticForeground(theme.Theme, metadata, palette),
                        style));
                }
            }

            if (lineIndex < lines.Length - 1)
            {
                inlines.Add(new LineBreak());
            }
        }

        return inlines;
    }

    private static IGrammar? TryCreateGrammar(string grammarScope, TextMateThemeResources theme)
    {
        var rawGrammar = theme.RegistryOptions.GetGrammar(grammarScope);
        if (rawGrammar is null)
        {
            return null;
        }

        var registry = new SyncRegistry(theme.Theme);
        registry.AddGrammar(rawGrammar, theme.RegistryOptions.GetInjections(grammarScope));
        return registry.GrammarForScopeName(
            grammarScope,
            0,
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal),
            EmptyBalancedBracketSelectors);
    }

    private static Run CreateRun(string text, IBrush? foreground, TextMateFontStyle style)
    {
        var run = new Run(text);
        if (foreground is not null)
        {
            run.Foreground = foreground;
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

    private static IBrush ResolveSemanticForeground(Theme theme, int metadata, MarkdownThemePalette palette)
    {
        var colorId = EncodedTokenAttributes.GetForeground(metadata);
        var color = theme.GetColor(colorId);
        if (!SemanticColors.TryGetValue(color, out var kind))
            kind = SemanticForegroundKind.Foreground;

        return kind switch
        {
            SemanticForegroundKind.Comment => palette.CodeCommentForeground,
            SemanticForegroundKind.String => palette.CodeStringForeground,
            SemanticForegroundKind.Number => palette.CodeNumberForeground,
            SemanticForegroundKind.Tag => palette.CodeTagForeground,
            SemanticForegroundKind.Attribute => palette.CodeAttributeForeground,
            SemanticForegroundKind.Property => palette.CodePropertyForeground,
            SemanticForegroundKind.Type => palette.CodeTypeForeground,
            SemanticForegroundKind.Keyword => palette.CodeKeywordForeground,
            SemanticForegroundKind.Punctuation => palette.CodePunctuationForeground,
            _ => palette.Foreground
        };
    }

    private static bool IsScope(string scope, ReadOnlySpan<char> prefix)
    {
        var candidate = scope.AsSpan();
        return candidate.StartsWith(prefix, StringComparison.Ordinal) &&
               (candidate.Length == prefix.Length || candidate[prefix.Length] == '.');
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

        var language = LightTheme.RegistryOptions.GetLanguageByExtension(extension);
        return language is null ? null : LightTheme.RegistryOptions.GetScopeByLanguageId(language.Id);
    }

    private static TextMateThemeResources CreateThemeResources(ThemeName themeName)
    {
        var registryOptions = new RegistryOptions(themeName);
        var rawTheme = registryOptions.LoadTheme(themeName);
        var theme = Theme.CreateFromRawTheme(new SemanticRawTheme(rawTheme), registryOptions);
        return new TextMateThemeResources(registryOptions, theme);
    }

    internal static IRawTheme CreateEditorTheme(IRawTheme rawTheme, MarkdownThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(rawTheme);
        ArgumentNullException.ThrowIfNull(palette);
        return new SemanticRawTheme(rawTheme, palette);
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


    private sealed record TextMateThemeResources(
        RegistryOptions RegistryOptions,
        Theme Theme);

    private enum SemanticForegroundKind
    {
        Foreground,
        Comment,
        String,
        Number,
        Tag,
        Attribute,
        Property,
        Type,
        Keyword,
        Punctuation
    }

    private sealed class SemanticRawTheme : IRawTheme
    {
        private readonly IRawTheme _source;
        private readonly ICollection<IRawThemeSetting> _settings;
        private readonly ICollection<IRawThemeSetting> _tokenColors;
        private readonly ICollection<KeyValuePair<string, object>> _guiColors;

        public SemanticRawTheme(IRawTheme source, MarkdownThemePalette? palette = null)
        {
            _source = source;
            _settings = CreateSemanticSettings(source.GetSettings(), palette);
            _tokenColors = CreateSemanticSettings(source.GetTokenColors(), palette);
            _guiColors = palette is null
                ? source.GetGuiColors() ?? []
                : CreatePaletteGuiColors(source.GetGuiColors(), palette);
        }

        public string GetName() => _source.GetName();

        public string GetInclude() => _source.GetInclude();

        public ICollection<IRawThemeSetting> GetSettings() => _settings;

        public ICollection<IRawThemeSetting> GetTokenColors() => _tokenColors;

        public ICollection<KeyValuePair<string, object>> GetGuiColors() => _guiColors;

        private static ICollection<IRawThemeSetting> CreateSemanticSettings(
            ICollection<IRawThemeSetting>? sourceSettings,
            MarkdownThemePalette? palette)
        {
            var capacity = (sourceSettings?.Count ?? 0) + SemanticScopeRules.Length + 1;
            var settings = new List<IRawThemeSetting>(capacity)
            {
                new SemanticRawThemeSetting(null, null, SemanticForegroundKind.Foreground, palette)
            };

            if (sourceSettings is not null)
            {
                foreach (var sourceSetting in sourceSettings)
                {
                    foreach (var scope in EnumerateScopes(sourceSetting.GetScope()))
                    {
                        settings.Add(new SemanticRawThemeSetting(
                            sourceSetting,
                            scope,
                            ResolveSemanticKind(scope),
                            palette));
                    }
                }
            }

            foreach (var (scope, kind) in SemanticScopeRules)
                settings.Add(new SemanticRawThemeSetting(null, scope, kind, palette));
            return settings;
        }

        private static ICollection<KeyValuePair<string, object>> CreatePaletteGuiColors(
            ICollection<KeyValuePair<string, object>>? sourceColors,
            MarkdownThemePalette palette)
        {
            var colors = new Dictionary<string, object>((sourceColors?.Count ?? 0) + 2, StringComparer.OrdinalIgnoreCase);
            if (sourceColors is not null)
            {
                foreach (var pair in sourceColors)
                    colors[pair.Key] = pair.Value;
            }

            var fallback = palette.IsDark ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light;
            colors["editor.foreground"] = ToThemeColor(palette.Foreground, fallback.Foreground);
            colors["editor.background"] = ToThemeColor(palette.SurfaceRaised, fallback.SurfaceRaised);
            return colors.ToArray();
        }

        private static IEnumerable<string?> EnumerateScopes(object? value)
        {
            if (value is null)
            {
                yield return null;
                yield break;
            }

            if (value is string scope)
            {
                foreach (var candidate in scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    yield return candidate;
                yield break;
            }

            if (value is IEnumerable values)
            {
                foreach (var item in values)
                {
                    if (item is not null)
                    {
                        foreach (var candidate in item.ToString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            yield return candidate;
                    }
                }
            }
        }

        private static SemanticForegroundKind ResolveSemanticKind(string? selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
                return SemanticForegroundKind.Foreground;

            var scopes = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var scopeIndex = scopes.Length - 1; scopeIndex >= 0; scopeIndex--)
            {
                var scope = scopes[scopeIndex];
                for (var ruleIndex = 0; ruleIndex < SemanticScopeRules.Length; ruleIndex++)
                {
                    if (IsScope(scope, SemanticScopeRules[ruleIndex].Scope))
                        return SemanticScopeRules[ruleIndex].Kind;
                }
            }

            return SemanticForegroundKind.Foreground;
        }
    }

    private sealed class SemanticRawThemeSetting(
        IRawThemeSetting? source,
        string? scope,
        SemanticForegroundKind kind,
        MarkdownThemePalette? palette) : IRawThemeSetting
    {
        private readonly IThemeSetting _setting = new SemanticThemeSetting(
            source?.GetSetting(),
            kind,
            scope is null,
            palette);

        public string GetName() => source?.GetName() ?? string.Empty;

        public object? GetScope() => scope;

        public IThemeSetting GetSetting() => _setting;
    }

    private sealed class SemanticThemeSetting(
        IThemeSetting? source,
        SemanticForegroundKind kind,
        bool isDocumentDefault,
        MarkdownThemePalette? palette) : IThemeSetting
    {
        public object? GetFontStyle() => source?.GetFontStyle();

        public string? GetBackground()
        {
            if (!isDocumentDefault || palette is null)
                return source?.GetBackground();

            var fallback = palette.IsDark ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light;
            return ToThemeColor(palette.SurfaceRaised, fallback.SurfaceRaised);
        }

        public string GetForeground()
        {
            if (palette is null)
            {
                return kind switch
                {
                    SemanticForegroundKind.Comment => "#020202",
                    SemanticForegroundKind.String => "#030303",
                    SemanticForegroundKind.Number => "#040404",
                    SemanticForegroundKind.Tag => "#050505",
                    SemanticForegroundKind.Attribute => "#060606",
                    SemanticForegroundKind.Property => "#070707",
                    SemanticForegroundKind.Type => "#080808",
                    SemanticForegroundKind.Keyword => "#090909",
                    SemanticForegroundKind.Punctuation => "#0A0A0A",
                    _ => "#010101"
                };
            }

            var fallback = palette.IsDark ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light;
            return kind switch
            {
                SemanticForegroundKind.Comment => ToThemeColor(palette.CodeCommentForeground, fallback.CodeCommentForeground),
                SemanticForegroundKind.String => ToThemeColor(palette.CodeStringForeground, fallback.CodeStringForeground),
                SemanticForegroundKind.Number => ToThemeColor(palette.CodeNumberForeground, fallback.CodeNumberForeground),
                SemanticForegroundKind.Tag => ToThemeColor(palette.CodeTagForeground, fallback.CodeTagForeground),
                SemanticForegroundKind.Attribute => ToThemeColor(palette.CodeAttributeForeground, fallback.CodeAttributeForeground),
                SemanticForegroundKind.Property => ToThemeColor(palette.CodePropertyForeground, fallback.CodePropertyForeground),
                SemanticForegroundKind.Type => ToThemeColor(palette.CodeTypeForeground, fallback.CodeTypeForeground),
                SemanticForegroundKind.Keyword => ToThemeColor(palette.CodeKeywordForeground, fallback.CodeKeywordForeground),
                SemanticForegroundKind.Punctuation => ToThemeColor(palette.CodePunctuationForeground, fallback.CodePunctuationForeground),
                _ => ToThemeColor(palette.Foreground, fallback.Foreground)
            };
        }
    }

    private static string ToThemeColor(IBrush brush, IBrush fallback)
    {
        var color = brush is ISolidColorBrush solid
            ? solid.Color
            : ((ISolidColorBrush)fallback).Color;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}

internal sealed class TextMateCodeBlockEditorPlugin : IMarkdownEditorPlugin
{
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly RegistryOptions LightEditorRegistryOptions = new(ThemeName.LightPlus);
    private static readonly RegistryOptions DarkEditorRegistryOptions = new(ThemeName.DarkPlus);

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

        var palette = context.RenderContext.ThemePalette ??
                      MarkdownThemePalette.Resolve(context.RenderContext.Foreground);
        var editorRegistryOptions = palette.IsDark
            ? DarkEditorRegistryOptions
            : LightEditorRegistryOptions;
        var editorThemeName = palette.IsDark ? ThemeName.DarkPlus : ThemeName.LightPlus;
        var installation = editor.InstallTextMate(editorRegistryOptions);
        installation.SetTheme(TextMateCodeBlockRenderingPlugin.CreateEditorTheme(
            editorRegistryOptions.LoadTheme(editorThemeName),
            palette));
        editor.SetCurrentValue(TextEditor.BackgroundProperty, palette.SurfaceRaised);
        editor.SetCurrentValue(TextEditor.ForegroundProperty, palette.Foreground);
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
            BorderBrush = palette.Border,
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
