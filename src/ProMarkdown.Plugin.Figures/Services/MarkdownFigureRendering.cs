using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProMarkdown.Controls;
using ProMarkdown.Services;

namespace ProMarkdown.Plugin.Figures;

internal static class MarkdownFigureRendering
{
    private static readonly IBrush SurfaceBackground = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush SurfaceBorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush HeaderBackground = new SolidColorBrush(Color.Parse("#EFF6FF"));
    private static readonly IBrush HeaderBorderBrush = new SolidColorBrush(Color.Parse("#BFDBFE"));
    private static readonly IBrush CaptionForeground = new SolidColorBrush(Color.Parse("#6E6E6E"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush DiagnosticBorderBrush = new SolidColorBrush(Color.Parse("#FECACA"));
    private static readonly IBrush DiagnosticBackground = new SolidColorBrush(Color.Parse("#FEF2F2"));
    private static readonly IBrush DiagnosticForeground = new SolidColorBrush(Color.Parse("#B42318"));
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
            content.Children.Add(CreateDiagnosticsPanel(document.Diagnostics));
        }

        return new Border
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
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
                        Foreground = AccentBrush
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
            Background = HeaderBackground,
            BorderBrush = HeaderBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            Child = CreateCaptionView(caption.Markdown, renderContext, fontWeight: FontWeight.SemiBold)
        };
    }

    private static Control CreateCaptionView(string markdown, MarkdownRenderContext renderContext, FontWeight fontWeight)
    {
        return new MarkdownTextBlock
        {
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            IsEditingEnabled = false,
            EditorPresentationMode = MarkdownEditorPresentationMode.Inline,
            FontSize = Math.Max(renderContext.FontSize - 1, 11),
            FontFamily = renderContext.FontFamily,
            FontWeight = fontWeight,
            Foreground = CaptionForeground,
            ThemePalette = renderContext.ThemePalette,
            TextWrapping = renderContext.TextWrapping,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
    }

    private static Control CreateBodyView(string markdown, MarkdownRenderContext renderContext)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = "Add figure body content to render a preview.",
                Foreground = CaptionForeground,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap
            };
        }

        return new Border
        {
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10),
            Child = new MarkdownTextBlock
            {
                BaseUri = renderContext.BaseUri,
                RenderController = NestedRenderController,
                EditingService = NestedEditingService,
                IsEditingEnabled = false,
                EditorPresentationMode = MarkdownEditorPresentationMode.Inline,
                FontSize = renderContext.FontSize,
                FontFamily = renderContext.FontFamily,
                Foreground = renderContext.Foreground,
                ThemePalette = renderContext.ThemePalette,
                TextWrapping = renderContext.TextWrapping,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Markdown = markdown
            }
        };
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownFigureDiagnostic> diagnostics)
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
                Foreground = DiagnosticForeground,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            Background = DiagnosticBackground,
            BorderBrush = DiagnosticBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            Child = panel
        };
    }
}
