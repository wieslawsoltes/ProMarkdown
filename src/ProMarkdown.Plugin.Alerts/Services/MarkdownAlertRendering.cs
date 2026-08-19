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
            body.Children.Add(CreateDiagnosticsPanel(document.Diagnostics, ResolvePalette(renderContext)));
        }

        var kindOption = MarkdownAlertSyntax.ResolveKindOption(document.Kind);
        var palette = ResolvePalette(renderContext);
        var presentation = MarkdownCalloutRendering.ResolvePresentation(kindOption.Value, fallbackTitle: "Alert", palette);
        return MarkdownCalloutRendering.CreateCalloutSurface(
            presentation.Title,
            subtitle: kindOption.Description,
            body,
            presentation.AccentBrush,
            presentation.Background,
            palette);
    }

    private static Control CreateBodyView(string markdown, MarkdownRenderContext renderContext)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return new TextBlock
            {
                Text = "Add alert body content to render a preview.",
                Foreground = ResolvePalette(renderContext).MutedForeground,
                TextWrapping = TextWrapping.Wrap
            };
        }

        var control = new MarkdownTextBlock
        {
            BaseUri = renderContext.BaseUri,
            RenderController = NestedRenderController,
            EditingService = NestedEditingService,
            FontFamily = renderContext.FontFamily,
            FontSize = renderContext.FontSize,
            Foreground = ResolvePalette(renderContext).Foreground,
            ThemePalette = renderContext.ThemePalette,
            ImageOptions = renderContext.ImageOptions,
            ImageLoader = renderContext.ImageLoader,
            TextWrapping = renderContext.TextWrapping,
            IsEditingEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Markdown = markdown
        };
        renderContext.TrackNestedRendering(control);
        return control;
    }

    private static Control CreateDiagnosticsPanel(
        IReadOnlyList<MarkdownAlertDiagnostic> diagnostics,
        MarkdownThemePalette palette)
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
