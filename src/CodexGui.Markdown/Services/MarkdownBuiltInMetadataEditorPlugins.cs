using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace CodexGui.Markdown.Services;

internal sealed class BuiltInMarkdownMetadataTemplateProvider : IMarkdownBlockTemplateProvider
{
    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "built-in-block-yaml-front-matter",
            "YAML front matter",
            MarkdownEditorFeature.YamlFrontMatter,
            MarkdownSourceEditing.BuildYamlFrontMatter("title: New document\nsummary: Add summary here"),
            "Insert YAML front matter metadata.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-link-reference",
            "Link reference",
            MarkdownEditorFeature.LinkReference,
            MarkdownSourceEditing.BuildLinkReferenceDefinition("docs", "https://example.com", "Reference title"),
            "Insert a reference-style markdown link definition.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-footnote",
            "Footnote",
            MarkdownEditorFeature.Footnote,
            MarkdownSourceEditing.BuildFootnoteMarkdown("note", "Footnote text."),
            "Insert a markdown footnote definition.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-abbreviation",
            "Abbreviation",
            MarkdownEditorFeature.Abbreviation,
            MarkdownSourceEditing.BuildAbbreviationMarkdown("API", "Application programming interface"),
            "Insert an abbreviation definition.");
    }
}

internal sealed class YamlFrontMatterMarkdownEditorPlugin : MarkdownEditorPluginBase<YamlFrontMatterBlock>
{
    public override string EditorId => MarkdownBuiltInEditorIds.YamlFrontMatter;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.YamlFrontMatter;

    protected override string ResolveTitle(YamlFrontMatterBlock node, MarkdownEditorResolveContext context) => "YAML front matter";

    protected override Control CreateEditor(YamlFrontMatterBlock node, MarkdownEditorPluginContext context)
    {
        var yamlTextBox = MarkdownEditorUiFactory.CreateCodeEditor(MarkdownSourceEditing.ExtractYamlFrontMatterBody(context.SourceText));
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineCodeStyle(yamlTextBox, context.RenderContext.FontSize);
        }

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit YAML metadata directly. The editor writes the surrounding `---` fences for you when you apply changes."));
        }

        string BuildCurrentMarkdown() => MarkdownSourceEditing.BuildYamlFrontMatter(yamlTextBox.Text);

        body.Children.Add(yamlTextBox);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit YAML front matter",
            "Document metadata editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            yamlTextBox,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }
}

internal sealed class AbbreviationMarkdownEditorPlugin : MarkdownEditorPluginBase<Abbreviation>
{
    public override string EditorId => MarkdownBuiltInEditorIds.Abbreviation;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.Abbreviation;

    protected override string ResolveTitle(Abbreviation node, MarkdownEditorResolveContext context) => $"Abbreviation {node.Label}";

    protected override Control CreateEditor(Abbreviation node, MarkdownEditorPluginContext context)
    {
        var labelTextBox = MarkdownEditorUiFactory.CreateTextEditor(node.Label, acceptsReturn: false, minHeight: 40);
        var meaningTextBox = MarkdownEditorUiFactory.CreateTextEditor(node.Text.ToString(), acceptsReturn: false, minHeight: 56);
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(labelTextBox, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(meaningTextBox, context.RenderContext.FontSize);
        }

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10,
            Children =
            {
                MarkdownMetadataEditorLayout.CreateLabeledField("Label", labelTextBox),
                MarkdownMetadataEditorLayout.CreateLabeledField("Meaning", meaningTextBox)
            }
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Insert(0, MarkdownEditorUiFactory.CreateInfoText("Edit the abbreviation label and its expanded meaning. Inline abbreviation occurrences update automatically after apply."));
        }

        string BuildCurrentMarkdown() => MarkdownSourceEditing.BuildAbbreviationMarkdown(labelTextBox.Text, meaningTextBox.Text);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit abbreviation",
            "Abbreviation definition editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            labelTextBox,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }
}

internal sealed class LinkReferenceMarkdownEditorPlugin : MarkdownEditorPluginBase<LinkReferenceDefinition>
{
    public override string EditorId => MarkdownBuiltInEditorIds.LinkReference;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.LinkReference;

    protected override bool IsEligible(LinkReferenceDefinition node, MarkdownEditorResolveContext context)
    {
        return node.GetType() == typeof(LinkReferenceDefinition);
    }

    protected override string ResolveTitle(LinkReferenceDefinition node, MarkdownEditorResolveContext context) => $"Link reference [{node.Label}]";

    protected override Control CreateEditor(LinkReferenceDefinition node, MarkdownEditorPluginContext context)
    {
        var labelTextBox = MarkdownEditorUiFactory.CreateTextEditor(node.Label, acceptsReturn: false, minHeight: 40);
        var urlTextBox = MarkdownEditorUiFactory.CreateTextEditor(node.Url, acceptsReturn: false, minHeight: 40);
        var titleTextBox = MarkdownEditorUiFactory.CreateTextEditor(node.Title, acceptsReturn: false, minHeight: 40);
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(labelTextBox, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(urlTextBox, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(titleTextBox, context.RenderContext.FontSize);
        }

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10,
            Children =
            {
                MarkdownMetadataEditorLayout.CreateLabeledField("Label", labelTextBox),
                MarkdownMetadataEditorLayout.CreateLabeledField("URL", urlTextBox),
                MarkdownMetadataEditorLayout.CreateLabeledField("Title", titleTextBox)
            }
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Insert(0, MarkdownEditorUiFactory.CreateInfoText("Edit the label, destination URL, and optional title for this reference-style link definition."));
        }

        string BuildCurrentMarkdown() => MarkdownSourceEditing.BuildLinkReferenceDefinition(labelTextBox.Text, urlTextBox.Text, titleTextBox.Text);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit link reference",
            "Reference definition editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            labelTextBox,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }
}

internal sealed class FootnoteMarkdownEditorPlugin : MarkdownEditorPluginBase<Footnote>
{
    public override string EditorId => MarkdownBuiltInEditorIds.Footnote;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.Footnote;

    protected override string ResolveTitle(Footnote node, MarkdownEditorResolveContext context) => $"Footnote {node.Label}";

    protected override Control CreateEditor(Footnote node, MarkdownEditorPluginContext context)
    {
        var labelTextBox = MarkdownEditorUiFactory.CreateTextEditor(node.Label, acceptsReturn: false, minHeight: 40);
        var bodyTextBox = MarkdownEditorUiFactory.CreateTextEditor(MarkdownSourceEditing.ExtractFootnoteBody(context.SourceText), acceptsReturn: true, minHeight: Math.Max(context.RenderContext.FontSize * 5, 120));
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(labelTextBox, context.RenderContext.FontSize);
            MarkdownEditorUiFactory.ApplyInlineParagraphStyle(bodyTextBox, context.RenderContext.FontSize);
        }

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit the footnote label and body. Extra body lines are emitted with the indentation required by markdown footnote syntax."));
        }

        string BuildCurrentMarkdown() => MarkdownSourceEditing.BuildFootnoteMarkdown(labelTextBox.Text, bodyTextBox.Text);

        body.Children.Add(MarkdownMetadataEditorLayout.CreateLabeledField("Label", labelTextBox));
        body.Children.Add(MarkdownEditorUiFactory.CreateTextStyleToolbar(() => bodyTextBox));
        body.Children.Add(bodyTextBox);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit footnote",
            "Footnote definition editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            bodyTextBox,
            preferInlineTextLayout: false,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }
}

internal static class MarkdownMetadataEditorLayout
{
    public static Control CreateLabeledField(string label, Control editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                MarkdownEditorUiFactory.CreateFieldLabel(label),
                editor
            }
        };
    }
}
