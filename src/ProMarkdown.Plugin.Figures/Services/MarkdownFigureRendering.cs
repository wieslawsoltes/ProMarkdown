using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProMarkdown.Controls;
using ProMarkdown.Services;

namespace ProMarkdown.Plugin.Figures;

internal static class MarkdownFigureRendering
{
    private static readonly IReadOnlyList<IMarkdownPlugin> NestedPlugins =
    [
        new FiguresMarkdownPlugin()
    ];
    private static readonly IMarkdownRenderController NestedRenderController = MarkdownRenderingServices.CreateController(NestedPlugins);
    private static readonly IMarkdownEditingService NestedEditingService = MarkdownRenderingServices.CreateEditingService(NestedPlugins);

    public static Control CreateBlockView(MarkdownFigureDocument document, MarkdownRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderContext);

        var content = new StackPanel
        {
            Spacing = 10
        };

        if (document.LeadingCaption is not null)
        {
            content.Children.Add(CreateCaptionPanel(document.LeadingCaption, renderContext));
        }

        content.Children.Add(CreateBodyView(document.BodyMarkdown, renderContext));

        if (document.TrailingCaption is not null)
        {
            content.Children.Add(CreateCaptionView(document.TrailingCaption.Markdown, renderContext, fontWeight: FontWeight.Normal));
        }

        if (document.Diagnostics.Count > 0)
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
                        Text = "Figure",
                        FontWeight = FontWeight.SemiBold,
                        Foreground = ResolvePalette(renderContext).Accent
                    },
                    content
                }
            }
        };
    }

    private static Control CreateCaptionPanel(MarkdownFigureCaption caption, MarkdownRenderContext renderContext)
    {
        return new Border
        {
            Background = ResolvePalette(renderContext).NoteBackground,
            BorderBrush = ResolvePalette(renderContext).NoteAccent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            Child = CreateCaptionView(caption.Markdown, renderContext, fontWeight: FontWeight.SemiBold)
        };
    }

    private static Control CreateCaptionView(string markdown, MarkdownRenderContext renderContext, FontWeight fontWeight)
    {
        var palette = ResolvePalette(renderContext);
        var captionPalette = palette.WithForeground(palette.MutedForeground);
        var control = new MarkdownTextBlock
        {
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            IsEditingEnabled = false,
            EditorPresentationMode = MarkdownEditorPresentationMode.Inline,
            FontSize = Math.Max(renderContext.FontSize - 1, 11),
            FontFamily = renderContext.FontFamily,
            FontWeight = fontWeight,
            Foreground = palette.MutedForeground,
            ThemePalette = captionPalette,
            ImageOptions = renderContext.ImageOptions,
            ImageLoader = renderContext.ImageLoader,
            TextWrapping = renderContext.TextWrapping,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
        renderContext.TrackNestedRendering(control);
        return control;
    }

    private static Control CreateBodyView(string markdown, MarkdownRenderContext renderContext)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = "Add figure body content to render a preview.",
                Foreground = ResolvePalette(renderContext).MutedForeground,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap
            };
        }

        var control = new MarkdownTextBlock
        {
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            IsEditingEnabled = false,
            EditorPresentationMode = MarkdownEditorPresentationMode.Inline,
            FontSize = renderContext.FontSize,
            FontFamily = renderContext.FontFamily,
            Foreground = ResolvePalette(renderContext).Foreground,
            ThemePalette = renderContext.ThemePalette,
            ImageOptions = renderContext.ImageOptions,
            ImageLoader = renderContext.ImageLoader,
            TextWrapping = renderContext.TextWrapping,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
        renderContext.TrackNestedRendering(control);

        return new Border
        {
            BorderBrush = ResolvePalette(renderContext).Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10),
            Child = control
        };
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownFigureDiagnostic> diagnostics, MarkdownThemePalette palette)
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
