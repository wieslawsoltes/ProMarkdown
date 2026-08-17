using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexGui.Markdown.Services;
using Markdig.Extensions.Footers;

namespace CodexGui.Markdown.Plugin.Footers;

internal sealed class FooterBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 25;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "footer-basic",
            "Footer block",
            MarkdownEditorFeature.Footer,
            MarkdownFooterSyntax.BuildFooter("Footer blocks can carry release notes, provenance, or closing context."),
            "Insert a simple footer block.");

        yield return new MarkdownBlockTemplate(
            "footer-links",
            "Footer with links",
            MarkdownEditorFeature.Footer,
            MarkdownFooterSyntax.BuildFooter("Closing notes can still include [links](https://docs.avaloniaui.net) and inline markdown."),
            "Insert a footer that carries closing links.");
    }
}

internal sealed class FooterBlockEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => FooterMarkdownEditorIds.Block;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Footer;

    public int Order => 30;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<FooterBlock>(out var footerBlock, out var depth) || footerBlock is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(footerBlock.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(footerBlock, sourceSpan, footerBlock.Line, footerBlock.Column),
            depth,
            "Footer");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is FooterBlock ? MarkdownFooterEditorUiFactory.CreateEditor(context) : null;
    }
}

internal static class MarkdownFooterEditorUiFactory
{
    public static Control CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var draft = CreateDraft(context.SourceText);
        var bodyEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.BodyMarkdown, acceptsReturn: true, minHeight: Math.Max(context.RenderContext.FontSize * 5, 140));

        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineParagraphStyle(bodyEditor, context.RenderContext.FontSize);
        }
        else
        {
            ApplyCardEditorStyle(bodyEditor, context);
        }

        var previewHost = CreatePreviewHost(context);

        string BuildMarkdown() => MarkdownFooterSyntax.BuildFooter(bodyEditor.Text);

        void UpdatePreview()
        {
            previewHost.Child = MarkdownFooterRendering.CreateBlockView(
                MarkdownFooterParser.Parse(BuildMarkdown()),
                context.RenderContext);
        }

        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit footer markdown without manually maintaining the `^^` prefix on each source line."));
        }

        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Footer markdown"));
        body.Children.Add(
            context.PresentationMode == MarkdownEditorPresentationMode.Inline
                ? MarkdownEditorUiFactory.CreateCompactTextStyleToolbar(() => bodyEditor)
                : MarkdownEditorUiFactory.CreateTextStyleToolbar(() => bodyEditor));
        body.Children.Add(bodyEditor);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit footer",
            "Footer block editor",
            body,
            () => context.CommitReplacement(BuildMarkdown()),
            context.CancelEdit,
            bodyEditor,
            preferInlineTextLayout: false,
            buildBlockMarkdownForActions: BuildMarkdown);
    }

    private static MarkdownFooterDraft CreateDraft(string sourceText)
    {
        var document = MarkdownFooterParser.Parse(sourceText);
        return new MarkdownFooterDraft(document.BodyMarkdown);
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

    private static void ApplyCardEditorStyle(TextBox textBox, MarkdownEditorPluginContext context)
    {
        textBox.FontSize = context.RenderContext.FontSize;
        textBox.FontFamily = context.RenderContext.FontFamily;
        textBox.Foreground = context.RenderContext.Foreground ?? MarkdownEditorUiFactory.EditorForeground;
        textBox.Background = MarkdownEditorUiFactory.InputBackground;
        textBox.BorderBrush = MarkdownEditorUiFactory.BorderBrush;
        textBox.BorderThickness = new Thickness(1);
        textBox.CornerRadius = new CornerRadius(10);
        textBox.Padding = new Thickness(12, 10);
        textBox.VerticalContentAlignment = VerticalAlignment.Top;
    }
}
