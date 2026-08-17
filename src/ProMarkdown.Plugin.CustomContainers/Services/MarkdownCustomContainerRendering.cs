using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProMarkdown.Controls;
using ProMarkdown.Services;

namespace ProMarkdown.Plugin.CustomContainers;

internal static class MarkdownCustomContainerRendering
{
    private static readonly IBrush DiagnosticBorderBrush = new SolidColorBrush(Color.Parse("#FECACA"));
    private static readonly IBrush DiagnosticBackground = new SolidColorBrush(Color.Parse("#FEF2F2"));
    private static readonly IBrush DiagnosticForeground = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush PlaceholderForeground = new SolidColorBrush(Color.Parse("#6E6E6E"));
    private static readonly IReadOnlyList<IMarkdownPlugin> NestedPlugins =
    [
        new CustomContainersMarkdownPlugin()
    ];
    private static readonly IMarkdownRenderController NestedRenderController = MarkdownRenderingServices.CreateController(NestedPlugins);
    private static readonly IMarkdownEditingService NestedEditingService = MarkdownRenderingServices.CreateEditingService(NestedPlugins);

    public static Control CreateBlockView(MarkdownCustomContainerDocument document, MarkdownRenderContext renderContext)
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

        var label = string.IsNullOrWhiteSpace(document.Info)
            ? "Container"
            : MarkdownCalloutRendering.FormatLabel(document.Info);
        var presentation = MarkdownCalloutRendering.ResolvePresentation(document.Info, fallbackTitle: label);
        var subtitle = string.IsNullOrWhiteSpace(document.Arguments) ? null : document.Arguments;

        return MarkdownCalloutRendering.CreateCalloutSurface(
            presentation.Title,
            subtitle,
            content,
            presentation.AccentBrush,
            presentation.Background);
    }

    private static Control CreateBodyView(string markdown, MarkdownRenderContext renderContext)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = "Add custom-container body content to render a preview.",
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
            FontSize = renderContext.FontSize,
            FontFamily = renderContext.FontFamily,
            Foreground = renderContext.Foreground,
            ThemePalette = renderContext.ThemePalette,
            TextWrapping = renderContext.TextWrapping,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownCustomContainerDiagnostic> diagnostics)
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
