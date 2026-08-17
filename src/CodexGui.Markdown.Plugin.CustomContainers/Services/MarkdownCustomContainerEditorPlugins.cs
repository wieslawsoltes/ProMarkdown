using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexGui.Markdown.Services;
using Markdig.Extensions.CustomContainers;

namespace CodexGui.Markdown.Plugin.CustomContainers;

internal sealed class CustomContainerBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 20;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "custom-container-info",
            "Info container",
            MarkdownEditorFeature.CustomContainer,
            MarkdownCustomContainerSyntax.BuildCustomContainer(
                "info",
                "Renderer status",
                "Custom containers can preserve a **type**, optional arguments, and nested markdown body content."),
            "Insert an info-style custom container.");

        yield return new MarkdownBlockTemplate(
            "custom-container-warning",
            "Warning container",
            MarkdownEditorFeature.CustomContainer,
            MarkdownCustomContainerSyntax.BuildCustomContainer(
                "warning",
                "Migration note",
                "- keep Mermaid-specific containers in the Mermaid plugin\n- use generic containers for documentation callouts"),
            "Insert a warning-style custom container.");

        yield return new MarkdownBlockTemplate(
            "custom-container-neutral",
            "Neutral container",
            MarkdownEditorFeature.CustomContainer,
            MarkdownCustomContainerSyntax.BuildCustomContainer(
                "details",
                string.Empty,
                "Use a neutral custom container for grouped notes or supporting context."),
            "Insert a neutral custom container.");
    }
}

internal sealed class CustomContainerBlockEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => CustomContainerMarkdownEditorIds.Block;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.CustomContainer;

    public int Order => 50;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<CustomContainer>(out var customContainer, out var depth) || customContainer is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(customContainer.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(customContainer, sourceSpan, customContainer.Line, customContainer.Column),
            depth,
            "Custom container");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is CustomContainer ? MarkdownCustomContainerEditorUiFactory.CreateEditor(context) : null;
    }
}

internal static class MarkdownCustomContainerEditorUiFactory
{
    public static Control CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var draft = CreateDraft(context.SourceText);
        var infoEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.Info, acceptsReturn: false, minHeight: 42);
        var argumentsEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.Arguments, acceptsReturn: false, minHeight: 42);
        var bodyEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.BodyMarkdown, acceptsReturn: true, minHeight: Math.Max(context.RenderContext.FontSize * 6, 160));

        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(infoEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(argumentsEditor, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineParagraphStyle(bodyEditor, context.RenderContext.FontSize);
        }
        else
        {
            ApplyCardEditorStyle(infoEditor, context, metadata: true);
            ApplyCardEditorStyle(argumentsEditor, context, metadata: true);
            ApplyCardEditorStyle(bodyEditor, context, metadata: false);
        }

        var previewHost = CreatePreviewHost(context);

        string BuildMarkdown()
        {
            return MarkdownCustomContainerSyntax.BuildCustomContainer(
                infoEditor.Text,
                argumentsEditor.Text,
                bodyEditor.Text);
        }

        void UpdatePreview()
        {
            previewHost.Child = MarkdownCustomContainerRendering.CreateBlockView(
                MarkdownCustomContainerParser.Parse(BuildMarkdown()),
                context.RenderContext);
        }

        infoEditor.TextChanged += (_, _) => UpdatePreview();
        argumentsEditor.TextChanged += (_, _) => UpdatePreview();
        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit the container type, optional arguments, and markdown body as separate fields while the preview keeps the `:::` fence syntax in sync."));
        }

        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Container type"));
        body.Children.Add(infoEditor);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Arguments"));
        body.Children.Add(argumentsEditor);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Container body markdown"));
        body.Children.Add(
            context.PresentationMode == MarkdownEditorPresentationMode.Inline
                ? MarkdownEditorUiFactory.CreateCompactTextStyleToolbar(() => bodyEditor)
                : MarkdownEditorUiFactory.CreateTextStyleToolbar(() => bodyEditor));
        body.Children.Add(bodyEditor);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit custom container",
            "Custom container editor",
            body,
            () => context.CommitReplacement(BuildMarkdown()),
            context.CancelEdit,
            bodyEditor,
            preferInlineTextLayout: false,
            buildBlockMarkdownForActions: BuildMarkdown);
    }

    private static MarkdownCustomContainerDraft CreateDraft(string sourceText)
    {
        var document = MarkdownCustomContainerParser.Parse(sourceText);
        return new MarkdownCustomContainerDraft(document.Info, document.Arguments, document.BodyMarkdown);
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
