using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProMarkdown.Controls;
using ProMarkdown.Services;

namespace ProMarkdown.Plugin.Footers;

internal static class MarkdownFooterRendering
{
    private static readonly IReadOnlyList<IMarkdownPlugin> NestedPlugins =
    [
        new FootersMarkdownPlugin()
    ];
    private static readonly IMarkdownRenderController NestedRenderController = MarkdownRenderingServices.CreateController(NestedPlugins);
    private static readonly IMarkdownEditingService NestedEditingService = MarkdownRenderingServices.CreateEditingService(NestedPlugins);

    public static Control CreateBlockView(MarkdownFooterDocument document, MarkdownRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderContext);

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateBodyView(document.BodyMarkdown, renderContext)
            }
        };

        if (document.Diagnostics.Count > 0)
        {
            content.Children.Add(CreateDiagnosticsPanel(document.Diagnostics, ResolvePalette(renderContext)));
        }

        return new Border
        {
            BorderBrush = ResolvePalette(renderContext).Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Footer",
                        Foreground = ResolvePalette(renderContext).MutedForeground,
                        FontWeight = FontWeight.SemiBold
                    },
                    content
                }
            }
        };
    }

    private static Control CreateBodyView(string markdown, MarkdownRenderContext renderContext)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = "Add footer content to render a preview.",
                Foreground = ResolvePalette(renderContext).MutedForeground,
                TextWrapping = TextWrapping.Wrap
            };
        }

        var palette = ResolvePalette(renderContext);
        var footerPalette = palette.WithForeground(palette.MutedForeground);
        var control = new MarkdownTextBlock
        {
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            IsEditingEnabled = false,
            EditorPresentationMode = MarkdownEditorPresentationMode.Inline,
            FontSize = Math.Max(renderContext.FontSize - 1, 11),
            FontFamily = renderContext.FontFamily,
            Foreground = palette.MutedForeground,
            ThemePalette = footerPalette,
            ImageOptions = renderContext.ImageOptions,
            ImageLoader = renderContext.ImageLoader,
            TextWrapping = renderContext.TextWrapping,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
        renderContext.TrackNestedRendering(control);
        return control;
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownFooterDiagnostic> diagnostics, MarkdownThemePalette palette)
    {
        var panel = new StackPanel
        {
            Spacing = 4
        };

        foreach (var diagnostic in diagnostics)
        {
            panel.Children.Add(new TextBlock
            {
                Text = diagnostic.Message,
                Foreground = palette.CautionAccent,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            Background = palette.CautionBackground,
            BorderBrush = palette.CautionAccent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            Child = panel
        };
    }

    private static MarkdownThemePalette ResolvePalette(MarkdownRenderContext context) =>
        context.ThemePalette ?? MarkdownThemePalette.Resolve(context.Foreground);
}
