using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProMarkdown.Controls;
using ProMarkdown.Services;

namespace ProMarkdown.Plugin.Alerts;

internal static class MarkdownAlertRendering
{
    private static readonly IMarkdownPlugin[] NestedPlugins =
    [
        new AlertsMarkdownPlugin()
    ];

    private static readonly IMarkdownRenderController NestedRenderController = MarkdownRenderingServices.CreateController(NestedPlugins);
    private static readonly IMarkdownEditingService NestedEditingService = MarkdownRenderingServices.CreateEditingService(NestedPlugins);
    private static readonly IBrush DiagnosticBorderBrush = new SolidColorBrush(Color.Parse("#FECACA"));
    private static readonly IBrush DiagnosticBackground = new SolidColorBrush(Color.Parse("#FEF2F2"));
    private static readonly IBrush DiagnosticForeground = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush PlaceholderForeground = new SolidColorBrush(Color.Parse("#6E6E6E"));

    public static Control CreateBlockView(MarkdownAlertDocument document, MarkdownRenderContext renderContext)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(renderContext);

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateBodyView(document.BodyMarkdown, renderContext)
            }
        };

        if (document.Diagnostics.Count > 0)
        {
            body.Children.Add(CreateDiagnosticsPanel(document.Diagnostics));
        }

        var kindOption = MarkdownAlertSyntax.ResolveKindOption(document.Kind);
        var presentation = MarkdownCalloutRendering.ResolvePresentation(kindOption.Value, fallbackTitle: "Alert");
        return MarkdownCalloutRendering.CreateCalloutSurface(
            presentation.Title,
            subtitle: kindOption.Description,
            body,
            presentation.AccentBrush,
            presentation.Background);
    }

    private static Control CreateBodyView(string markdown, MarkdownRenderContext renderContext)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = "Add alert body content to render a preview.",
                Foreground = PlaceholderForeground,
                TextWrapping = TextWrapping.Wrap
            };
        }

        return new MarkdownTextBlock
        {
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            FontFamily = renderContext.FontFamily,
            FontSize = renderContext.FontSize,
            Foreground = renderContext.Foreground,
            ThemePalette = renderContext.ThemePalette,
            TextWrapping = renderContext.TextWrapping,
            IsEditingEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
    }

    private static Control CreateDiagnosticsPanel(IReadOnlyList<MarkdownAlertDiagnostic> diagnostics)
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
