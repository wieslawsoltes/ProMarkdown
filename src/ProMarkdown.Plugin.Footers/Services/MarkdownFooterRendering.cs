using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProMarkdown.Controls;
using ProMarkdown.Services;

namespace ProMarkdown.Plugin.Footers;

internal static class MarkdownFooterRendering
{
    private static readonly IBrush BorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush HeaderForeground = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush DiagnosticBorderBrush = new SolidColorBrush(Color.Parse("#FECACA"));
    private static readonly IBrush DiagnosticBackground = new SolidColorBrush(Color.Parse("#FEF2F2"));
    private static readonly IBrush DiagnosticForeground = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush PlaceholderForeground = new SolidColorBrush(Color.Parse("#6E6E6E"));
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
            content.Children.Add(CreateDiagnosticsPanel(document.Diagnostics));
        }

        return new Border
        {
            BorderBrush = BorderBrush,
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
                        Foreground = HeaderForeground,
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
                Foreground = PlaceholderForeground,
                TextWrapping = TextWrapping.Wrap
            };
        }

        return new MarkdownTextBlock
        {
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            IsEditingEnabled = false,
            EditorPresentationMode = MarkdownEditorPresentationMode.Inline,
            FontSize = Math.Max(renderContext.FontSize - 1, 11),
            FontFamily = renderContext.FontFamily,
            Foreground = HeaderForeground,
            ThemePalette = renderContext.ThemePalette,
            TextWrapping = renderContext.TextWrapping,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownFooterDiagnostic> diagnostics)
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
