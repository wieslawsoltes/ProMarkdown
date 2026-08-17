using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CodexGui.Markdown.Services;
using Markdig.Extensions.Alerts;

namespace CodexGui.Markdown.Plugin.Alerts;

internal sealed class AlertBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 10;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "alert-note",
            "Note alert",
            MarkdownEditorFeature.Alert,
            MarkdownAlertSyntax.BuildAlertBlock("note", "Capture supporting context or implementation notes."),
            "Insert a neutral note alert.");

        yield return new MarkdownBlockTemplate(
            "alert-tip",
            "Tip alert",
            MarkdownEditorFeature.Alert,
            MarkdownAlertSyntax.BuildAlertBlock("tip", "Highlight a best practice or a productivity shortcut."),
            "Insert a helpful tip alert.");

        yield return new MarkdownBlockTemplate(
            "alert-important",
            "Important alert",
            MarkdownEditorFeature.Alert,
            MarkdownAlertSyntax.BuildAlertBlock("important", "Call out a decision or guidance that should stand out."),
            "Insert an important alert.");

        yield return new MarkdownBlockTemplate(
            "alert-warning",
            "Warning alert",
            MarkdownEditorFeature.Alert,
            MarkdownAlertSyntax.BuildAlertBlock("warning", "Explain the risk and the follow-up action before continuing."),
            "Insert a warning alert.");

        yield return new MarkdownBlockTemplate(
            "alert-danger",
            "Danger alert",
            MarkdownEditorFeature.Alert,
            MarkdownAlertSyntax.BuildAlertBlock("danger", "Document the breaking impact or high-risk condition."),
            "Insert a danger alert.");
    }
}

internal sealed class AlertBlockEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => AlertsMarkdownEditorIds.Block;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Alert;

    public int Order => -25;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<AlertBlock>(out var alertBlock, out var depth) || alertBlock is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(alertBlock.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(alertBlock, sourceSpan, alertBlock.Line, alertBlock.Column),
            depth,
            "Alert");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is AlertBlock ? MarkdownAlertEditorUiFactory.CreateEditor(context) : null;
    }
}

internal static class MarkdownAlertEditorUiFactory
{
    public static Control CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var draft = CreateDraft(context.SourceText);
        var kindComboBox = new ComboBox
        {
            ItemsSource = MarkdownAlertSyntax.AvailableKinds,
            SelectedItem = MarkdownAlertSyntax.ResolveKindOption(draft.Kind),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = MarkdownEditorUiFactory.InputBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6)
        };

        var kindDescription = MarkdownEditorUiFactory.CreateInfoText(((MarkdownAlertKindOption?)kindComboBox.SelectedItem)?.Description ?? "Alert block");
        var bodyEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.BodyMarkdown, acceptsReturn: true, minHeight: Math.Max(context.RenderContext.FontSize * 6, 150));

        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineParagraphStyle(bodyEditor, context.RenderContext.FontSize);
        }
        else
        {
            bodyEditor.FontSize = context.RenderContext.FontSize;
            bodyEditor.FontFamily = context.RenderContext.FontFamily;
            bodyEditor.Foreground = context.RenderContext.Foreground ?? MarkdownEditorUiFactory.EditorForeground;
            bodyEditor.Background = MarkdownEditorUiFactory.InputBackground;
            bodyEditor.BorderBrush = MarkdownEditorUiFactory.BorderBrush;
            bodyEditor.BorderThickness = new Thickness(1);
            bodyEditor.CornerRadius = new CornerRadius(10);
            bodyEditor.Padding = new Thickness(12, 10);
            bodyEditor.VerticalContentAlignment = VerticalAlignment.Top;
        }

        var previewHost = CreatePreviewHost(context);

        string BuildMarkdown()
        {
            var selectedKind = (kindComboBox.SelectedItem as MarkdownAlertKindOption)?.Value;
            return MarkdownAlertSyntax.BuildAlertBlock(selectedKind, bodyEditor.Text);
        }

        void UpdatePreview()
        {
            var selectedOption = kindComboBox.SelectedItem as MarkdownAlertKindOption;
            kindDescription.Text = selectedOption?.Description ?? "Alert block";
            previewHost.Child = MarkdownAlertRendering.CreateBlockView(
                MarkdownAlertParser.Parse(BuildMarkdown()),
                context.RenderContext);
        }

        kindComboBox.SelectionChanged += (_, _) => UpdatePreview();
        bodyEditor.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Choose an alert kind and edit the quoted markdown body without manually managing `>` markers."));
        }

        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Alert kind"));
        body.Children.Add(kindComboBox);
        body.Children.Add(kindDescription);
        body.Children.Add(MarkdownEditorUiFactory.CreateFieldLabel("Alert body markdown"));
        body.Children.Add(bodyEditor);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit alert",
            "Alert block editor",
            body,
            () => context.CommitReplacement(BuildMarkdown()),
            context.CancelEdit,
            bodyEditor,
            preferInlineTextLayout: false,
            buildBlockMarkdownForActions: BuildMarkdown);
    }

    private static MarkdownAlertDraft CreateDraft(string sourceText)
    {
        var document = MarkdownAlertParser.Parse(sourceText);
        return new MarkdownAlertDraft(document.Kind, document.BodyMarkdown);
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
}
