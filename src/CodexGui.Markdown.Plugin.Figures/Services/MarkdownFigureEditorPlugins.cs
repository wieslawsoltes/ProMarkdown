using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexGui.Markdown.Services;
using Markdig.Extensions.Figures;

namespace CodexGui.Markdown.Plugin.Figures;

internal sealed class FigureBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 10;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "figure-basic",
            "Figure block",
            MarkdownEditorFeature.Figure,
            MarkdownFigureSyntax.BuildFigure(
                "Preview surface",
                "Supporting markdown can live inside the figure body.\n\n- captions stay separate from body content\n- preview editing preserves the fence syntax",
                "Figure captions can also live on the closing fence."),
            "Insert a figure with opening and closing fence captions.");

        yield return new MarkdownBlockTemplate(
            "figure-image",
            "Image figure",
            MarkdownEditorFeature.Figure,
            MarkdownFigureSyntax.BuildFigure(
                "Product preview",
                "![Alt text](https://example.com/image.png)\n\nReplace the placeholder image URL with your real media asset.",
                "Use the closing fence caption for supporting notes."),
            "Insert an image-oriented figure.");

        yield return new MarkdownBlockTemplate(
            "figure-code",
            "Code figure",
            MarkdownEditorFeature.Figure,
            MarkdownFigureSyntax.BuildFigure(
                "Render contract",
                """
                ```json
                {
                  "feature": "figures",
                  "mode": "plugin-backed"
                }
                ```
                """,
                "Figures can wrap code, notes, or media."),
            "Insert a figure that showcases non-image body content.");
    }
}

internal sealed class FigureBlockEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => FiguresMarkdownEditorIds.Block;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Figure;

    public int Order => -18;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<Figure>(out var figure, out var depth) || figure is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(figure.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(figure, sourceSpan, figure.Line, figure.Column),
            depth,
            "Figure");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is Figure ? MarkdownFigureEditorUiFactory.CreateEditor(context) : null;
    }
}

internal static class MarkdownFigureEditorUiFactory
{
    public static Control CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var draft = CreateDraft(context.SourceText);
        var leadingCaptionEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.LeadingCaptionMarkdown, acceptsReturn: false, minHeight: 42);
        var bodyEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.BodyMarkdown, acceptsReturn: true, minHeight: Math.Max(context.RenderContext.FontSize * 7, 180));
        var trailingCaptionEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.TrailingCaptionMarkdown, acceptsReturn: false, minHeight: 42);

        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(leadingCaptionEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineParagraphStyle(bodyEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(trailingCaptionEditor, context.RenderContext.FontSize);
        }
        else
        {
            ApplyCardEditorStyle(leadingCaptionEditor, context, metadata: true);
            ApplyCardEditorStyle(bodyEditor, context, metadata: false);
            ApplyCardEditorStyle(trailingCaptionEditor, context, metadata: true);
        }

        var previewHost = CreatePreviewHost(context);

        string BuildMarkdown()
        {
            return MarkdownFigureSyntax.BuildFigure(
                leadingCaptionEditor.Text,
                bodyEditor.Text,
                trailingCaptionEditor.Text);
        }

        void UpdatePreview()
        {
            previewHost.Child = MarkdownFigureRendering.CreateBlockView(
                MarkdownFigureParser.Parse(BuildMarkdown()),
                context.RenderContext);
        }

        leadingCaptionEditor.TextChanged += (_, _) => UpdatePreview();
        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        trailingCaptionEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var snippetToolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        snippetToolbar.Children.Add(MarkdownEditorUiFactory.CreateSecondaryButton("Insert image", () =>
            AppendSnippet(bodyEditor, "![Alt text](https://example.com/image.png)")));
        snippetToolbar.Children.Add(MarkdownEditorUiFactory.CreateSecondaryButton("Insert note", () =>
            AppendSnippet(bodyEditor, "Supporting figure notes go here.")));

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit the opening caption, body markdown, and closing caption as separate parts of the figure fence syntax."));
        }

        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Opening fence caption"));
        body.Children.Add(leadingCaptionEditor);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Figure body markdown"));
        body.Children.Add(snippetToolbar);
        body.Children.Add(bodyEditor);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Closing fence caption"));
        body.Children.Add(trailingCaptionEditor);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit figure",
            "Figure block editor",
            body,
            () => context.CommitReplacement(BuildMarkdown()),
            context.CancelEdit,
            bodyEditor,
            preferInlineTextLayout: false,
            buildBlockMarkdownForActions: BuildMarkdown);
    }

    private static MarkdownFigureDraft CreateDraft(string sourceText)
    {
        var document = MarkdownFigureParser.Parse(sourceText);
        return new MarkdownFigureDraft(
            document.LeadingCaption?.Markdown ?? string.Empty,
            document.BodyMarkdown,
            document.TrailingCaption?.Markdown ?? string.Empty);
    }

    private static Border CreatePreviewHost(MarkdownEditorPluginContext context)
    {
        return new Border
        {
            Background = MarkdownEditorUiFactory.SectionBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? new Thickness(8, 6) : new Thickness(10)
        };
    }

    private static void AppendSnippet(TextBox textBox, string snippet)
    {
        var current = MarkdownFigureSyntax.NormalizeBlockText(textBox.Text);
        if (current.Length > 0)
        {
            current += "\n\n";
        }

        current += snippet;
        textBox.Text = current;
        textBox.CaretIndex = current.Length;
    }

    private static void ApplyCardEditorStyle(TextBox textBox, MarkdownEditorPluginContext context, bool metadata)
    {
        textBox.FontSize = metadata ? Math.Max(context.RenderContext.FontSize - 1, 12) : context.RenderContext.FontSize;
        textBox.FontFamily = context.RenderContext.FontFamily;
        textBox.Foreground = context.RenderContext.Foreground ?? MarkdownEditorUiFactory.EditorForeground;
        textBox.Background = MarkdownEditorUiFactory.InputBackground;
        textBox.BorderBrush = MarkdownEditorUiFactory.BorderBrush;
        textBox.BorderThickness = new Thickness(1);
        textBox.CornerRadius = new CornerRadius(metadata ? 6 : 10);
        textBox.Padding = metadata ? new Thickness(8, 6) : new Thickness(12, 10);
        textBox.VerticalContentAlignment = metadata ? VerticalAlignment.Center : VerticalAlignment.Top;
    }
}
