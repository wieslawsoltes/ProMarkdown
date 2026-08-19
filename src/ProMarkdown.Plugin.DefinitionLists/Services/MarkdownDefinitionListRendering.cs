using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProMarkdown.Controls;
using ProMarkdown.Services;

namespace ProMarkdown.Plugin.DefinitionLists;

internal static class MarkdownDefinitionListRendering
{
    private static readonly IReadOnlyList<IMarkdownPlugin> NestedPlugins =
    [
        new DefinitionListMarkdownPlugin()
    ];
    private static readonly IMarkdownRenderController NestedRenderController = MarkdownRenderingServices.CreateController(NestedPlugins);
    private static readonly IMarkdownEditingService NestedEditingService = MarkdownRenderingServices.CreateEditingService(NestedPlugins);

    public static Control CreateBlockView(MarkdownDefinitionListDocument document, MarkdownRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderContext);

        var content = new StackPanel
        {
            Spacing = 12
        };

        if (document.IsEmpty)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Definition list is empty.",
                Foreground = ResolvePalette(renderContext).Foreground,
                FontStyle = FontStyle.Italic
            });
        }
        else
        {
            foreach (var entry in document.Entries)
            {
                content.Children.Add(CreateEntryView(entry, renderContext));
            }
        }

        if (document.HasDiagnostics)
        {
            content.Children.Add(CreateDiagnosticsPanel(document.Diagnostics, ResolvePalette(renderContext)));
        }

        return new Border
        {
            Background = ResolvePalette(renderContext).SurfaceRaised,
            BorderBrush = ResolvePalette(renderContext).Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Definition list",
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ResolvePalette(renderContext).Accent
                    },
                    content
                }
            }
        };
    }

    private static Control CreateEntryView(MarkdownDefinitionEntry entry, MarkdownRenderContext renderContext)
    {
        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(0.34, GridUnitType.Star),
                new ColumnDefinition(0.66, GridUnitType.Star)
            },
            ColumnSpacing = 14
        };

        var termsPanel = new StackPanel
        {
            Spacing = 8
        };

        foreach (var term in entry.Terms)
        {
            termsPanel.Children.Add(new Border
            {
                Background = ResolvePalette(renderContext).NoteBackground,
                BorderBrush = ResolvePalette(renderContext).NoteAccent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Child = CreateMarkdownView(term.Markdown, renderContext, fontWeight: FontWeight.SemiBold)
            });
        }

        var definitionPanel = new Border
        {
            BorderBrush = ResolvePalette(renderContext).Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10),
            Child = CreateMarkdownView(entry.DefinitionMarkdown, renderContext)
        };

        layout.Children.Add(termsPanel);
        Grid.SetColumn(definitionPanel, 1);
        layout.Children.Add(definitionPanel);

        return new Border
        {
            BorderBrush = ResolvePalette(renderContext).Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = layout
        };
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownDefinitionListDiagnostic> diagnostics, MarkdownThemePalette palette)
    {
        var panel = new StackPanel
        {
            Spacing = 2
        };

        foreach (var diagnostic in diagnostics)
        {
            panel.Children.Add(new TextBlock
            {
                Text = diagnostic.Message,
                Foreground = palette.CautionAccent,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private static MarkdownTextBlock CreateMarkdownView(
        string markdown,
        MarkdownRenderContext renderContext,
        FontWeight? fontWeight = null)
    {
        var control = new MarkdownTextBlock
        {
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            IsEditingEnabled = false,
            EditorPresentationMode = MarkdownEditorPresentationMode.Inline,
            FontSize = renderContext.FontSize,
            FontFamily = renderContext.FontFamily,
            FontWeight = fontWeight ?? FontWeight.Normal,
            Foreground = ResolvePalette(renderContext).Foreground,
            ThemePalette = renderContext.ThemePalette,
            ImageOptions = renderContext.ImageOptions,
            ImageLoader = renderContext.ImageLoader,
            TextWrapping = renderContext.TextWrapping,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
        renderContext.TrackNestedRendering(control);
        return control;
    }

    private static MarkdownThemePalette ResolvePalette(MarkdownRenderContext context) =>
        context.ThemePalette ?? MarkdownThemePalette.Resolve(context.Foreground);
}
