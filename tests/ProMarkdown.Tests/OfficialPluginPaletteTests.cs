using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using ProMarkdown.Controls;
using ProMarkdown.Plugin.CustomContainers;
using ProMarkdown.Plugin.DefinitionLists;
using ProMarkdown.Plugin.Figures;
using ProMarkdown.Plugin.Footers;
using ProMarkdown.Plugin.Math;
using ProMarkdown.Plugin.SyntaxHighlighting;
using ProMarkdown.Plugin.TextMate;
using ProMarkdown.Services;
using Shouldly;
using TextMateSharp.Grammars;

namespace ProMarkdown.Tests;

public sealed class OfficialPluginPaletteTests
{
    [AvaloniaFact]
    public void TextMateUsesSemanticPaletteBrushes()
    {
        var palette = CreateDistinctPalette();
        var control = CreateMarkdown(
            "```csharp\npublic string Name = \"hello\"; // note\n```",
            palette,
            new TextMateMarkdownPlugin());

        var foregrounds = EnumerateInlines(control.Inlines!)
            .OfType<Run>()
            .Select(run => run.Foreground)
            .ToArray();

        foregrounds.ShouldContain(palette.CodeKeywordForeground);
        foregrounds.ShouldContain(palette.CodeTypeForeground);
        foregrounds.ShouldContain(palette.CodeStringForeground);
        foregrounds.ShouldContain(palette.CodeCommentForeground);
    }

    [AvaloniaFact]
    public void TextMateUsesPaletteForStructuredLanguageTokens()
    {
        var palette = CreateDistinctPalette();
        var control = CreateMarkdown(
            "```json\n{\"name\": 42}\n```\n\n```xml\n<main id=\"content\"></main>\n```",
            palette,
            new TextMateMarkdownPlugin());

        var foregrounds = EnumerateInlines(control.Inlines!)
            .OfType<Run>()
            .Select(run => run.Foreground)
            .ToArray();

        foregrounds.ShouldContain(palette.CodePropertyForeground);
        foregrounds.ShouldContain(palette.CodeNumberForeground);
        foregrounds.ShouldContain(palette.CodeTagForeground);
        foregrounds.ShouldContain(palette.CodeAttributeForeground);
        foregrounds.ShouldContain(palette.CodePunctuationForeground);
    }

    [AvaloniaFact]
    public void TextMateEditorThemeUsesSemanticPaletteColors()
    {
        var palette = CreateDistinctPalette();
        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        var theme = TextMateCodeBlockRenderingPlugin.CreateEditorTheme(
            registryOptions.LoadTheme(ThemeName.DarkPlus),
            palette);

        GetThemeForeground(theme.GetTokenColors(), "keyword")
            .ShouldBe(ToThemeColor(palette.CodeKeywordForeground));
        GetThemeForeground(theme.GetTokenColors(), "string")
            .ShouldBe(ToThemeColor(palette.CodeStringForeground));
        GetThemeForeground(theme.GetTokenColors(), "comment")
            .ShouldBe(ToThemeColor(palette.CodeCommentForeground));

        var documentDefaults = theme.GetSettings().First(setting => setting.GetScope() is null).GetSetting();
        documentDefaults.GetForeground().ShouldBe(ToThemeColor(palette.Foreground));
        documentDefaults.GetBackground().ShouldBe(ToThemeColor(palette.SurfaceRaised));

        var guiColors = theme.GetGuiColors().ToDictionary(pair => pair.Key, pair => pair.Value);
        guiColors["editor.foreground"].ShouldBe(ToThemeColor(palette.Foreground));
        guiColors["editor.background"].ShouldBe(ToThemeColor(palette.SurfaceRaised));
    }

    [AvaloniaFact]
    public void BuiltInSyntaxHighlightingUsesSemanticPaletteBrushes()
    {
        var palette = CreateDistinctPalette();
        var control = CreateMarkdown(
            "```csharp\npublic string Name = \"hello\"; // note\n```",
            palette,
            new SyntaxHighlightingMarkdownPlugin());

        var foregrounds = EnumerateInlines(control.Inlines!)
            .OfType<Run>()
            .Select(run => run.Foreground)
            .ToArray();

        foregrounds.ShouldContain(palette.CodeKeywordForeground);
        foregrounds.ShouldContain(palette.CodeTypeForeground);
        foregrounds.ShouldContain(palette.CodeStringForeground);
        foregrounds.ShouldContain(palette.CodeCommentForeground);
    }

    [AvaloniaFact]
    public void ContainerDefinitionAndFigurePluginsUsePaletteSurfaces()
    {
        var palette = CreateDistinctPalette();
        var cases = new (string Markdown, IMarkdownPlugin Plugin)[]
        {
            (":::details\nContainer body.\n:::", new CustomContainersMarkdownPlugin()),
            ("Term\n:   Definition body.", new DefinitionListMarkdownPlugin()),
            ("^^^ Caption\nFigure body.\n^^^ Supporting caption.", new FiguresMarkdownPlugin())
        };

        foreach (var testCase in cases)
        {
            var control = CreateMarkdown(testCase.Markdown, palette, testCase.Plugin);
            var borders = EnumerateControls(control.Inlines!).OfType<Border>().ToArray();

            borders.ShouldContain(border => ReferenceEquals(border.Background, palette.SurfaceRaised));
            borders.ShouldContain(border => ReferenceEquals(border.BorderBrush, palette.Border));
        }
    }

    [AvaloniaFact]
    public void FooterPluginUsesMutedForegroundAndPaletteBorder()
    {
        var palette = CreateDistinctPalette();
        var control = CreateMarkdown(
            "^^ Footer body.",
            palette,
            new FootersMarkdownPlugin());

        var controls = EnumerateControls(control.Inlines!).ToArray();
        controls.OfType<Border>()
            .ShouldContain(border => ReferenceEquals(border.BorderBrush, palette.Border));
        controls.OfType<TextBlock>()
            .ShouldContain(text => ReferenceEquals(text.Foreground, palette.MutedForeground));
    }

    [AvaloniaFact]
    public void MathPluginUsesPaletteFormulaAndCalloutBrushes()
    {
        var palette = CreateDistinctPalette();
        var control = CreateMarkdown(
            "$$\na^2 + b^2 = c^2\n$$",
            palette,
            new MathMarkdownPlugin());
        var controls = EnumerateControls(control.Inlines!).ToArray();

        controls.OfType<Border>()
            .ShouldContain(border => ReferenceEquals(border.Background, palette.ImportantBackground));
        controls.OfType<TextBlock>()
            .ShouldContain(text => ReferenceEquals(text.Foreground, palette.ImportantAccent));
    }

    [AvaloniaFact]
    public void CoreMetadataCardTitlesUsePaletteForeground()
    {
        var palette = CreateDistinctPalette();
        var cases = new (string Markdown, string Title)[]
        {
            ("[docs]: https://example.com", "[docs]"),
            ("*[API]: Application programming interface", "API")
        };

        foreach (var testCase in cases)
        {
            var control = CreateMarkdown(
                testCase.Markdown,
                palette,
                new SyntaxHighlightingMarkdownPlugin());
            var title = EnumerateControls(control.Inlines!)
                .OfType<TextBlock>()
                .Single(text => string.Equals(text.Text, testCase.Title, StringComparison.Ordinal));

            title.Foreground.ShouldBeSameAs(palette.Foreground);
        }
    }

    [AvaloniaFact]
    public void BuiltInEditorControlsResolveColorsFromTheSemanticPalette()
    {
        var palette = CreateDistinctPalette();
        var textEditor = MarkdownEditorUiFactory.CreateTextEditor("Body", acceptsReturn: true, minHeight: 40);
        var infoText = MarkdownEditorUiFactory.CreateInfoText("Help");
        var fieldLabel = MarkdownEditorUiFactory.CreateFieldLabel("Field");
        var primaryButton = MarkdownEditorUiFactory.CreatePrimaryButton("Apply", () => { });
        var comboBox = new ComboBox();
        var checkBox = new CheckBox();
        MarkdownEditorUiFactory.ApplyInputPalette(comboBox);
        MarkdownEditorUiFactory.ApplyInputPalette(checkBox);
        var root = new StackPanel
        {
            Children =
            {
                textEditor,
                infoText,
                fieldLabel,
                primaryButton,
                comboBox,
                checkBox
            }
        };
        MarkdownEditorUiFactory.ApplyPaletteResources(root, palette);

        var window = new Window { Width = 320, Height = 200, Content = root };
        window.Show();
        try
        {
            window.UpdateLayout();

            textEditor.Background.ShouldBeSameAs(palette.SurfaceRaised);
            textEditor.Foreground.ShouldBeSameAs(palette.Foreground);
            textEditor.BorderBrush.ShouldBeSameAs(palette.Border);
            infoText.Foreground.ShouldBeSameAs(palette.MutedForeground);
            fieldLabel.Foreground.ShouldBeSameAs(palette.Foreground);
            primaryButton.Background.ShouldBeSameAs(palette.Accent);
            primaryButton.Foreground.ShouldBeSameAs(palette.Surface);
            primaryButton.BorderBrush.ShouldBeSameAs(palette.Accent);
            comboBox.Background.ShouldBeSameAs(palette.SurfaceRaised);
            comboBox.Foreground.ShouldBeSameAs(palette.Foreground);
            comboBox.BorderBrush.ShouldBeSameAs(palette.Border);
            checkBox.Background.ShouldBeSameAs(palette.SurfaceRaised);
            checkBox.Foreground.ShouldBeSameAs(palette.Foreground);
            checkBox.BorderBrush.ShouldBeSameAs(palette.Border);
        }
        finally
        {
            window.Close();
        }
    }

    private static MarkdownThemePalette CreateDistinctPalette() => new()
    {
        IsDark = true,
        Foreground = Brushes.White,
        MutedForeground = Brushes.Gray,
        Accent = Brushes.Lime,
        Surface = Brushes.Black,
        SurfaceRaised = Brushes.Navy,
        Border = Brushes.Olive,
        CodeHeaderBackground = Brushes.Teal,
        CautionAccent = Brushes.Maroon,
        ImportantAccent = Brushes.Fuchsia,
        ImportantBackground = Brushes.Purple,
        CodeKeywordForeground = Brushes.Red,
        CodeTypeForeground = Brushes.Blue,
        CodeStringForeground = Brushes.Green,
        CodeCommentForeground = Brushes.Orange,
        CodeNumberForeground = Brushes.Yellow,
        CodePropertyForeground = Brushes.Cyan,
        CodeTagForeground = Brushes.Brown,
        CodeAttributeForeground = Brushes.Pink,
        CodePunctuationForeground = Brushes.Silver
    };

    private static string GetThemeForeground(
        ICollection<TextMateSharp.Themes.IRawThemeSetting> settings,
        string scope) => settings
        .Last(setting => string.Equals(setting.GetScope() as string, scope, StringComparison.Ordinal))
        .GetSetting()
        .GetForeground();

    private static string ToThemeColor(IBrush brush)
    {
        var color = ((ISolidColorBrush)brush).Color;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static MarkdownTextBlock CreateMarkdown(
        string markdown,
        MarkdownThemePalette palette,
        IMarkdownPlugin plugin) => new()
    {
        Markdown = markdown,
        FontSize = 14,
        Foreground = palette.Foreground,
        ThemePalette = palette,
        RenderController = MarkdownRenderingServices.CreateController(plugin),
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private static IEnumerable<Inline> EnumerateInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is Span span)
            {
                foreach (var child in EnumerateInlines(span.Inlines))
                    yield return child;
            }
            if (inline is InlineUIContainer { Child: { } control })
            {
                foreach (var child in EnumerateInlines(control))
                    yield return child;
            }
        }
    }

    private static IEnumerable<Inline> EnumerateInlines(Control control)
    {
        if (control is TextBlock { Inlines: { } inlines })
        {
            foreach (var inline in EnumerateInlines(inlines))
                yield return inline;
        }
        foreach (var child in EnumerateChildren(control))
        {
            foreach (var inline in EnumerateInlines(child))
                yield return inline;
        }
    }

    private static IEnumerable<Control> EnumerateControls(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var child in EnumerateControls(span.Inlines))
                    yield return child;
            }
            if (inline is InlineUIContainer { Child: { } control })
            {
                foreach (var child in EnumerateControls(control))
                    yield return child;
            }
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control control)
    {
        yield return control;
        foreach (var child in EnumerateChildren(control))
        {
            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private static IEnumerable<Control> EnumerateChildren(Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                yield return child;
        }
        else if (control is Decorator { Child: { } child })
        {
            yield return child;
        }
        else if (control is ContentControl { Content: Control contentChild })
        {
            yield return contentChild;
        }
    }
}
