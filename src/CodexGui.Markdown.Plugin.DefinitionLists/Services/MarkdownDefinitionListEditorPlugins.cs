using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexGui.Markdown.Services;
using Markdig.Extensions.DefinitionLists;

namespace CodexGui.Markdown.Plugin.DefinitionLists;

internal sealed class DefinitionListBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 10;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "definition-list-basic",
            "Definition list",
            MarkdownEditorFeature.DefinitionList,
            MarkdownDefinitionListSyntax.BuildDefinitionList(
            [
                new MarkdownDefinitionDraftEntry(
                    "Rendering engine",
                    "Maps `Markdig` nodes to native Avalonia controls.")
            ]),
            "Insert a simple definition list.");

        yield return new MarkdownBlockTemplate(
            "definition-list-glossary",
            "Glossary entry",
            MarkdownEditorFeature.DefinitionList,
            MarkdownDefinitionListSyntax.BuildDefinitionList(
            [
                new MarkdownDefinitionDraftEntry(
                    "AST\nAbstract syntax tree",
                    "A structured representation that separates **terms** from their definition body.\n\n- supports multiple labels\n- supports nested markdown")
            ]),
            "Insert a glossary-style definition list block.");
    }
}

internal sealed class DefinitionListBlockEditorPlugin : IMarkdownEditorPlugin
{
    public string EditorId => DefinitionListMarkdownEditorIds.Block;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.DefinitionList;

    public int Order => -20;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<DefinitionList>(out var definitionList, out var depth) || definitionList is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(definitionList.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(definitionList, sourceSpan, definitionList.Line, definitionList.Column),
            depth,
            "Definition list");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is DefinitionList
            ? MarkdownDefinitionListEditorUiFactory.CreateEditor(context)
            : null;
    }
}

internal static class MarkdownDefinitionListEditorUiFactory
{
    public static Control CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var drafts = CreateDrafts(context.SourceText);
        var entriesPanel = new StackPanel
        {
            Spacing = 10
        };
        var previewHost = CreatePreviewHost(context);

        string BuildMarkdown() => MarkdownDefinitionListSyntax.BuildDefinitionList(drafts);

        void UpdatePreview()
        {
            previewHost.Child = MarkdownDefinitionListRendering.CreateBlockView(
                MarkdownDefinitionListParser.Parse(BuildMarkdown()),
                context.RenderContext);
        }

        void RebuildEditors()
        {
            entriesPanel.Children.Clear();

            for (var index = 0; index < drafts.Count; index++)
            {
                var entryIndex = index;
                var draft = drafts[entryIndex];
                var termsEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.TermsText, acceptsReturn: true, minHeight: 86);
                var definitionEditor = MarkdownEditorUiFactory.CreateTextEditor(draft.DefinitionMarkdown, acceptsReturn: true, minHeight: 150);

                if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
                {
                    MarkdownEditorUiFactory.ApplyInlineMetadataStyle(termsEditor, context.RenderContext.FontSize);
                    MarkdownEditorUiFactory.ApplyInlineParagraphStyle(definitionEditor, context.RenderContext.FontSize);
                }

                termsEditor.TextChanged += (_, _) =>
                {
                    drafts[entryIndex] = drafts[entryIndex] with { TermsText = termsEditor.Text ?? string.Empty };
                    UpdatePreview();
                };
                definitionEditor.TextChanged += (_, _) =>
                {
                    drafts[entryIndex] = drafts[entryIndex] with { DefinitionMarkdown = definitionEditor.Text ?? string.Empty };
                    UpdatePreview();
                };

                var removeButton = MarkdownEditorUiFactory.CreateSecondaryButton("Remove entry", () =>
                {
                    if (drafts.Count <= 1)
                    {
                        return;
                    }

                    drafts.RemoveAt(entryIndex);
                    RebuildEditors();
                    UpdatePreview();
                });
                removeButton.IsEnabled = drafts.Count > 1;

                entriesPanel.Children.Add(new Border
                {
                    BorderBrush = MarkdownEditorUiFactory.BorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(12),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 8,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = $"Entry {entryIndex + 1}",
                                        FontWeight = FontWeight.SemiBold,
                                        VerticalAlignment = VerticalAlignment.Center
                                    },
                                    removeButton
                                }
                            },
                            MarkdownEditorUiFactory.CreateFieldLabel("Terms (one per line)"),
                            termsEditor,
                            MarkdownEditorUiFactory.CreateFieldLabel("Definition markdown"),
                            definitionEditor
                        }
                    }
                });
            }
        }

        var addEntryButton = MarkdownEditorUiFactory.CreateSecondaryButton("Add entry", () =>
        {
            drafts.Add(new MarkdownDefinitionDraftEntry("New term", "Definition text."));
            RebuildEditors();
            UpdatePreview();
        });

        RebuildEditors();
        UpdatePreview();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };

        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit the definition list as structured entries with one or more terms and a shared markdown definition body."));
        }

        body.Children.Add(addEntryButton);
        body.Children.Add(entriesPanel);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit definition list",
            "Definition list editor",
            body,
            () => context.CommitReplacement(BuildMarkdown()),
            context.CancelEdit,
            focusTarget: null,
            buildBlockMarkdownForActions: BuildMarkdown);
    }

    private static List<MarkdownDefinitionDraftEntry> CreateDrafts(string sourceText)
    {
        var document = MarkdownDefinitionListParser.Parse(sourceText);
        if (document.Entries.Count == 0)
        {
            return
            [
                new MarkdownDefinitionDraftEntry("Term", "Definition text.")
            ];
        }

        return document.Entries
            .Select(static entry => new MarkdownDefinitionDraftEntry(
                string.Join(Environment.NewLine, entry.Terms.Select(static term => term.Markdown)),
                entry.DefinitionMarkdown))
            .ToList();
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
