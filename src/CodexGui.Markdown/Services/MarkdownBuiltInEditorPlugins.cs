using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;

namespace CodexGui.Markdown.Services;

public static class MarkdownBuiltInEditorIds
{
    public const string Paragraph = "built-in-paragraph-editor";
    public const string Heading = "built-in-heading-editor";
    public const string List = "built-in-list-editor";
    public const string Table = "built-in-table-editor";
    public const string Code = "built-in-code-editor";
    public const string YamlFrontMatter = "built-in-yaml-front-matter-editor";
    public const string Abbreviation = "built-in-abbreviation-editor";
    public const string LinkReference = "built-in-link-reference-editor";
    public const string Footnote = "built-in-footnote-editor";
}

internal static class MarkdownBuiltInEditorPlugins
{
    public static void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockTemplateProvider(new BuiltInMarkdownBlockTemplateProvider())
            .AddBlockTemplateProvider(new BuiltInMarkdownMetadataTemplateProvider())
            .AddEditorPlugin(new ParagraphMarkdownEditorPlugin())
            .AddEditorPlugin(new HeadingMarkdownEditorPlugin())
            .AddEditorPlugin(new ListMarkdownEditorPlugin())
            .AddEditorPlugin(new TableMarkdownEditorPlugin())
            .AddEditorPlugin(new PlainCodeMarkdownEditorPlugin())
            .AddEditorPlugin(new YamlFrontMatterMarkdownEditorPlugin())
            .AddEditorPlugin(new AbbreviationMarkdownEditorPlugin())
            .AddEditorPlugin(new LinkReferenceMarkdownEditorPlugin())
            .AddEditorPlugin(new FootnoteMarkdownEditorPlugin());
    }
}

internal sealed class BuiltInMarkdownBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "built-in-block-paragraph",
            "Paragraph",
            MarkdownEditorFeature.Paragraph,
            "New paragraph text.",
            "Insert a plain markdown paragraph.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-heading",
            "Heading",
            MarkdownEditorFeature.Heading,
            "## New heading",
            "Insert a second-level heading.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-bullet-list",
            "Bullet list",
            MarkdownEditorFeature.List,
            "- First item\n- Second item",
            "Insert a bullet list.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-task-list",
            "Task list",
            MarkdownEditorFeature.List,
            "- [ ] Pending task\n- [x] Completed task",
            "Insert a markdown task list.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-table",
            "Table",
            MarkdownEditorFeature.Table,
            "| Column | Value |\n| --- | --- |\n| Item | Detail |",
            "Insert a simple markdown table.");

        yield return new MarkdownBlockTemplate(
            "built-in-block-code",
            "Code block",
            MarkdownEditorFeature.Code,
            MarkdownSourceEditing.BuildCodeFence("text", "// code"),
            "Insert a fenced code block.");
    }
}

internal abstract class MarkdownEditorPluginBase<TNode> : IMarkdownEditorPlugin
    where TNode : MarkdownObject
{
    public abstract string EditorId { get; }

    public abstract MarkdownEditorFeature Feature { get; }

    public virtual int Order => 0;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!TryResolveNode(context, out var node, out var depth) || node is null || !IsEligible(node, context))
        {
            return false;
        }

        if (MarkdownRenderedElementMetadata.CreateAstNodeInfo(node) is not { } astNode || astNode.SourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(Feature, astNode, depth, ResolveTitle(node, context));
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Node is TNode node ? CreateEditor(node, context) : null;
    }

    protected virtual bool TryResolveNode(MarkdownEditorResolveContext context, out TNode? node, out int depth)
    {
        return context.TryFindAncestor<TNode>(out node, out depth);
    }

    protected virtual bool IsEligible(TNode node, MarkdownEditorResolveContext context)
    {
        return true;
    }

    protected abstract string ResolveTitle(TNode node, MarkdownEditorResolveContext context);

    protected abstract Control CreateEditor(TNode node, MarkdownEditorPluginContext context);
}

internal sealed class ParagraphMarkdownEditorPlugin : MarkdownEditorPluginBase<ParagraphBlock>
{
    public override string EditorId => MarkdownBuiltInEditorIds.Paragraph;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.Paragraph;

    protected override bool IsEligible(ParagraphBlock node, MarkdownEditorResolveContext context)
    {
        return !context.TryFindAncestor<Table>(out _, out _) &&
               !context.TryFindAncestor<ListBlock>(out _, out _);
    }

    protected override string ResolveTitle(ParagraphBlock node, MarkdownEditorResolveContext context) => "Paragraph";

    protected override Control CreateEditor(ParagraphBlock node, MarkdownEditorPluginContext context)
    {
        var paragraphEditor = MarkdownEditorUiFactory.CreateTextEditor(
            MarkdownSourceEditing.NormalizeInlineMarkdown(context.SourceText),
            acceptsReturn: true,
            minHeight: Math.Max(context.RenderContext.FontSize * 6, 140));
        paragraphEditor.FontSize = context.RenderContext.FontSize;
        paragraphEditor.FontFamily = context.RenderContext.FontFamily;
        paragraphEditor.Foreground = context.RenderContext.Foreground ?? MarkdownEditorUiFactory.EditorForeground;
        paragraphEditor.Background = MarkdownEditorUiFactory.InputBackground;
        paragraphEditor.BorderBrush = MarkdownEditorUiFactory.BorderBrush;
        paragraphEditor.BorderThickness = new Thickness(1);
        paragraphEditor.CornerRadius = new CornerRadius(10);
        paragraphEditor.Padding = new Thickness(12, 10);
        paragraphEditor.VerticalContentAlignment = VerticalAlignment.Top;

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit the paragraph in one large writing surface and use the toolbar to wrap selected text."));
        }

        string BuildParagraphMarkdown() => MarkdownSourceEditing.BuildParagraphMarkdown(paragraphEditor.Text);

        body.Children.Add(
            context.PresentationMode == MarkdownEditorPresentationMode.Inline
                ? MarkdownEditorUiFactory.CreateCompactTextStyleToolbar(() => paragraphEditor)
                : MarkdownEditorUiFactory.CreateTextStyleToolbar(() => paragraphEditor));
        body.Children.Add(paragraphEditor);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit paragraph",
            "Paragraph editor",
            body,
            () => context.CommitReplacement(BuildParagraphMarkdown()),
            context.CancelEdit,
            paragraphEditor,
            preferInlineTextLayout: false,
            buildBlockMarkdownForActions: BuildParagraphMarkdown);
    }
}

internal sealed class HeadingMarkdownEditorPlugin : MarkdownEditorPluginBase<HeadingBlock>
{
    public override string EditorId => MarkdownBuiltInEditorIds.Heading;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.Heading;

    protected override string ResolveTitle(HeadingBlock node, MarkdownEditorResolveContext context) => $"Heading {node.Level}";

    protected override Control CreateEditor(HeadingBlock node, MarkdownEditorPluginContext context)
    {
        var headingOptions = Enumerable.Range(1, 6)
            .Select(level => new HeadingLevelOption(level, $"Heading {level}"))
            .ToArray();
        var headingLevel = headingOptions.First(option => option.Level == Math.Clamp(node.Level, 1, 6));

        var levelComboBox = new ComboBox
        {
            ItemsSource = headingOptions,
            SelectedItem = headingLevel,
            MinWidth = 140
        };
        var headingEditor = MarkdownEditorUiFactory.CreateTextEditor(
            MarkdownSourceEditing.ExtractHeadingText(context.SourceText, node.Level),
            acceptsReturn: false,
            minHeight: 48);
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineHeadingStyle(headingEditor, context.RenderContext.FontSize, node.Level);
        }

        var settingsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                MarkdownEditorUiFactory.CreateFieldLabel("Level"),
                levelComboBox
            }
        };

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit heading text in one editor surface and adjust the heading level separately."));
        }

        string BuildHeadingMarkdown()
        {
            var selectedLevel = levelComboBox.SelectedItem as HeadingLevelOption ?? headingLevel;
            return MarkdownSourceEditing.BuildHeadingMarkdown(selectedLevel.Level, headingEditor.Text ?? string.Empty);
        }

        body.Children.Add(settingsRow);
        body.Children.Add(
            context.PresentationMode == MarkdownEditorPresentationMode.Inline
                ? MarkdownEditorUiFactory.CreateCompactTextStyleToolbar(() => headingEditor)
                : MarkdownEditorUiFactory.CreateTextStyleToolbar(() => headingEditor));
        body.Children.Add(headingEditor);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit heading",
            "Heading editor",
            body,
            () => context.CommitReplacement(BuildHeadingMarkdown()),
            context.CancelEdit,
            headingEditor,
            preferInlineTextLayout: true,
            buildBlockMarkdownForActions: BuildHeadingMarkdown);
    }

    private sealed record HeadingLevelOption(int Level, string Label)
    {
        public override string ToString() => Label;
    }
}

internal sealed class ListMarkdownEditorPlugin : MarkdownEditorPluginBase<ListBlock>
{
    public override string EditorId => MarkdownBuiltInEditorIds.List;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.List;

    protected override string ResolveTitle(ListBlock node, MarkdownEditorResolveContext context) => node.IsOrdered ? "Ordered list" : "List";

    protected override Control CreateEditor(ListBlock node, MarkdownEditorPluginContext context)
    {
        var listKind = ResolveListKind(node);
        var orderedStart = TryParseOrderedStart(node.OrderedStart ?? string.Empty);
        TextBox? activeTextBox = null;
        var items = BuildListItems(node, context);
        if (items.Count == 0)
        {
            items.Add(new ListEditorItem(string.Empty, false));
        }

        var itemsHost = new StackPanel
        {
            Spacing = 8
        };

        var kindComboBox = new ComboBox
        {
            ItemsSource = Enum.GetValues<EditableListKind>(),
            SelectedItem = listKind,
            MinWidth = 140
        };

        var orderedStartTextBox = MarkdownEditorUiFactory.CreateTextEditor(
            orderedStart.ToString(CultureInfo.InvariantCulture),
            acceptsReturn: false,
            minHeight: 40);
        orderedStartTextBox.Width = 90;
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(orderedStartTextBox, context.RenderContext.FontSize);
        }

        void RebuildItemsHost()
        {
            activeTextBox = null;
            itemsHost.Children.Clear();
            var currentKind = kindComboBox.SelectedItem as EditableListKind? ?? listKind;
            orderedStartTextBox.IsVisible = currentKind == EditableListKind.Ordered;

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var textBox = MarkdownEditorUiFactory.CreateTextEditor(item.Text, acceptsReturn: true, minHeight: 70);
                textBox.TextChanged += (_, _) => item.Text = textBox.Text ?? string.Empty;
                if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
                {
                    MarkdownEditorUiFactory.ApplyInlineListItemStyle(textBox, context.RenderContext.FontSize);
                }
                textBox.GotFocus += (_, _) => activeTextBox = textBox;
                if (activeTextBox is null)
                {
                    activeTextBox = textBox;
                }

                var rowHeader = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = VerticalAlignment.Center
                };

                rowHeader.Children.Add(new TextBlock
                {
                    Text = currentKind switch
                    {
                        EditableListKind.Ordered => $"{orderedStart + index}.",
                        EditableListKind.Task => "Task",
                        _ => "Item"
                    },
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (currentKind == EditableListKind.Task)
                {
                    var checkBox = new CheckBox
                    {
                        IsChecked = item.IsChecked,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    checkBox.IsCheckedChanged += (_, _) => item.IsChecked = checkBox.IsChecked == true;
                    rowHeader.Children.Add(checkBox);
                }

                var removeButton = MarkdownEditorUiFactory.CreateSecondaryButton("Remove", () =>
                {
                    if (items.Count == 1)
                    {
                        items[0] = new ListEditorItem(string.Empty, false);
                        items[0].IsChecked = false;
                    }
                    else
                    {
                        items.Remove(item);
                    }

                    RebuildItemsHost();
                });
                rowHeader.Children.Add(removeButton);

                itemsHost.Children.Add(new Border
                {
                    Background = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? Brushes.Transparent : MarkdownEditorUiFactory.SectionBackground,
                    BorderBrush = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? Brushes.Transparent : MarkdownEditorUiFactory.BorderBrush,
                    BorderThickness = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? new Thickness(0) : new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? new Thickness(0) : new Thickness(10),
                    Child = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            rowHeader,
                            textBox
                        }
                    }
                });
            }
        }

        kindComboBox.SelectionChanged += (_, _) => RebuildItemsHost();
        orderedStartTextBox.TextChanged += (_, _) =>
        {
            if ((kindComboBox.SelectedItem as EditableListKind? ?? listKind) == EditableListKind.Ordered)
            {
                RebuildItemsHost();
            }
        };

        var addItemButton = MarkdownEditorUiFactory.CreateSecondaryButton("Add item", () =>
        {
            items.Add(new ListEditorItem(string.Empty, false));
            RebuildItemsHost();
        });

        var settingsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                MarkdownEditorUiFactory.CreateFieldLabel("List type"),
                kindComboBox,
                MarkdownEditorUiFactory.CreateFieldLabel("Start"),
                orderedStartTextBox,
                addItemButton
            }
        };

        RebuildItemsHost();

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit list items, switch between bullet/ordered/task lists, and use inline markdown formatting."));
        }

        body.Children.Add(settingsRow);
        body.Children.Add(
            context.PresentationMode == MarkdownEditorPresentationMode.Inline
                ? MarkdownEditorUiFactory.CreateCompactTextStyleToolbar(() => activeTextBox)
                : MarkdownEditorUiFactory.CreateTextStyleToolbar(() => activeTextBox));
        body.Children.Add(itemsHost);

        string BuildCurrentMarkdown()
        {
            var currentKind = kindComboBox.SelectedItem as EditableListKind? ?? listKind;
            var startValue = int.TryParse(orderedStartTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? Math.Max(parsed, 1)
                : 1;
            return BuildListMarkdown(currentKind, startValue, items);
        }

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit list",
            "List editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            activeTextBox ?? (Control)itemsHost,
            preferInlineTextLayout: true,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }

    private static EditableListKind ResolveListKind(ListBlock list)
    {
        if (list.IsOrdered)
        {
            return EditableListKind.Ordered;
        }

        foreach (var child in list)
        {
            if (child is ListItemBlock listItem && TryResolveTaskItem(listItem, out _))
            {
                return EditableListKind.Task;
            }
        }

        return EditableListKind.Bullet;
    }

    private static int TryParseOrderedStart(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(parsed, 1)
            : 1;
    }

    private static List<ListEditorItem> BuildListItems(ListBlock list, MarkdownEditorPluginContext context)
    {
        var items = new List<ListEditorItem>();
        foreach (var child in list)
        {
            if (child is not ListItemBlock listItem)
            {
                continue;
            }

            var itemSource = MarkdownSourceSpan.FromMarkdig(listItem.Span).Slice(context.Markdown);
            items.Add(new ListEditorItem(
                MarkdownSourceEditing.StripListMarker(itemSource),
                TryResolveTaskItem(listItem, out var isChecked) && isChecked));
        }

        return items;
    }

    private static bool TryResolveTaskItem(ListItemBlock listItem, out bool isChecked)
    {
        isChecked = false;

        if (listItem.Count == 0 ||
            listItem[0] is not ParagraphBlock paragraph ||
            paragraph.Inline?.FirstChild is not TaskList taskList)
        {
            return false;
        }

        isChecked = taskList.Checked;
        return true;
    }

    private static string BuildListMarkdown(EditableListKind kind, int orderedStart, IReadOnlyList<ListEditorItem> items)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < items.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            var item = items[index];
            var lines = MarkdownSourceEditing.NormalizeBlockText(item.Text).Split('\n', StringSplitOptions.None);
            var marker = kind switch
            {
                EditableListKind.Ordered => $"{orderedStart + index}. ",
                EditableListKind.Task => $"- [{(item.IsChecked ? 'x' : ' ')}] ",
                _ => "- "
            };

            builder.Append(marker);
            builder.Append(lines.Length > 0 ? lines[0] : string.Empty);

            for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                builder.Append('\n');
                builder.Append("  ");
                builder.Append(lines[lineIndex]);
            }
        }

        return builder.ToString();
    }

    private sealed class ListEditorItem(string text, bool isChecked)
    {
        public string Text { get; set; } = text;

        public bool IsChecked { get; set; } = isChecked;
    }

    private enum EditableListKind
    {
        Bullet,
        Ordered,
        Task
    }
}

internal sealed class TableMarkdownEditorPlugin : MarkdownEditorPluginBase<Table>
{
    public override string EditorId => MarkdownBuiltInEditorIds.Table;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.Table;

    public override int Order => -10;

    protected override string ResolveTitle(Table node, MarkdownEditorResolveContext context) => "Table";

    protected override Control CreateEditor(Table node, MarkdownEditorPluginContext context)
    {
        var rows = BuildRows(node, context.Markdown);
        if (rows.Count == 0)
        {
            rows.Add(["", ""]);
        }

        TextBox? activeTextBox = null;
        var gridHost = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8
        };

        void TrackFocus(TextBox textBox)
        {
            textBox.GotFocus += (_, _) => activeTextBox = textBox;
            if (activeTextBox is null)
            {
                activeTextBox = textBox;
            }
        }

        void RebuildGrid()
        {
            activeTextBox = null;
            gridHost.Children.Clear();
            gridHost.ColumnDefinitions.Clear();
            gridHost.RowDefinitions.Clear();

            var columnCount = rows.Max(static row => row.Count);
            if (columnCount <= 0)
            {
                columnCount = 1;
            }

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                gridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            }

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                while (rows[rowIndex].Count < columnCount)
                {
                    rows[rowIndex].Add(string.Empty);
                }

                gridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    var row = rows[rowIndex];
                    var cellIndex = columnIndex;
                    var textBox = MarkdownEditorUiFactory.CreateTextEditor(row[cellIndex], acceptsReturn: false, minHeight: 40);
                    textBox.TextChanged += (_, _) =>
                    {
                        if ((uint)cellIndex < (uint)row.Count)
                        {
                            row[cellIndex] = textBox.Text ?? string.Empty;
                        }
                    };
                    if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
                    {
                        MarkdownEditorUiFactory.ApplyInlineTableCellStyle(textBox, context.RenderContext.FontSize);
                    }
                    TrackFocus(textBox);

                    var cellHost = new Border
                    {
                        Background = rowIndex == 0 ? MarkdownEditorUiFactory.HeaderBackground : MarkdownEditorUiFactory.EditorBackground,
                        BorderBrush = MarkdownEditorUiFactory.BorderBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(6),
                        Child = textBox
                    };
                    Grid.SetRow(cellHost, rowIndex);
                    Grid.SetColumn(cellHost, columnIndex);
                    gridHost.Children.Add(cellHost);
                }
            }
        }

        var addRowButton = MarkdownEditorUiFactory.CreateSecondaryButton("Add row", () =>
        {
            var columnCount = rows.Max(static row => row.Count);
            rows.Add(Enumerable.Repeat(string.Empty, Math.Max(columnCount, 1)).ToList());
            RebuildGrid();
        });

        var removeRowButton = MarkdownEditorUiFactory.CreateSecondaryButton("Remove row", () =>
        {
            if (rows.Count > 1)
            {
                rows.RemoveAt(rows.Count - 1);
                RebuildGrid();
            }
        });

        var addColumnButton = MarkdownEditorUiFactory.CreateSecondaryButton("Add column", () =>
        {
            foreach (var row in rows)
            {
                row.Add(string.Empty);
            }

            RebuildGrid();
        });

        var removeColumnButton = MarkdownEditorUiFactory.CreateSecondaryButton("Remove column", () =>
        {
            var columnCount = rows.Max(static row => row.Count);
            if (columnCount <= 1)
            {
                return;
            }

            foreach (var row in rows)
            {
                row.RemoveAt(row.Count - 1);
            }

            RebuildGrid();
        });

        RebuildGrid();

        var actionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                addRowButton,
                removeRowButton,
                addColumnButton,
                removeColumnButton
            }
        };

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit table cells directly. The first row is emitted as the markdown header row."));
        }

        body.Children.Add(actionsRow);
        body.Children.Add(
            context.PresentationMode == MarkdownEditorPresentationMode.Inline
                ? MarkdownEditorUiFactory.CreateCompactTextStyleToolbar(() => activeTextBox)
                : MarkdownEditorUiFactory.CreateTextStyleToolbar(() => activeTextBox));
        body.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = gridHost
        });

        string BuildCurrentMarkdown() => BuildTableMarkdown(rows);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit table",
            "Table editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            activeTextBox ?? (Control)gridHost,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }

    private static List<List<string>> BuildRows(Table table, string markdown)
    {
        var rows = new List<List<string>>();
        foreach (var child in table)
        {
            if (child is not TableRow row)
            {
                continue;
            }

            var cells = new List<string>();
            foreach (var rowChild in row)
            {
                if (rowChild is TableCell cell)
                {
                    cells.Add(MarkdownSourceSpan.FromMarkdig(cell.Span).Slice(markdown).Trim());
                }
            }

            rows.Add(cells);
        }

        return rows;
    }

    private static string BuildTableMarkdown(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var columnCount = rows.Max(static row => row.Count);
        if (columnCount <= 0)
        {
            columnCount = 1;
        }

        var normalizedRows = rows
            .Select(row =>
            {
                var normalizedRow = row.Take(columnCount).ToList();
                while (normalizedRow.Count < columnCount)
                {
                    normalizedRow.Add(string.Empty);
                }

                return normalizedRow;
            })
            .ToList();

        var builder = new StringBuilder();
        AppendTableRow(builder, normalizedRows[0]);
        builder.Append('\n');
        AppendTableSeparator(builder, columnCount);

        for (var rowIndex = 1; rowIndex < normalizedRows.Count; rowIndex++)
        {
            builder.Append('\n');
            AppendTableRow(builder, normalizedRows[rowIndex]);
        }

        return builder.ToString();
    }

    private static void AppendTableRow(StringBuilder builder, IReadOnlyList<string> cells)
    {
        builder.Append('|');
        for (var index = 0; index < cells.Count; index++)
        {
            builder.Append(' ');
            builder.Append(MarkdownSourceEditing.SanitizeTableCell(cells[index]));
            builder.Append(' ');
            builder.Append('|');
        }
    }

    private static void AppendTableSeparator(StringBuilder builder, int columnCount)
    {
        builder.Append('|');
        for (var index = 0; index < columnCount; index++)
        {
            builder.Append(" --- |");
        }
    }
}

internal sealed class PlainCodeMarkdownEditorPlugin : MarkdownEditorPluginBase<CodeBlock>
{
    private static readonly HashSet<string> MermaidLanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "diagram-mermaid",
        "mmd",
        "mermaid",
        "mermaidjs"
    };

    public override string EditorId => MarkdownBuiltInEditorIds.Code;

    public override MarkdownEditorFeature Feature => MarkdownEditorFeature.Code;

    protected override bool IsEligible(CodeBlock node, MarkdownEditorResolveContext context)
    {
        return !IsMermaidCodeBlock(node);
    }

    protected override string ResolveTitle(CodeBlock node, MarkdownEditorResolveContext context) => "Code block";

    protected override Control CreateEditor(CodeBlock node, MarkdownEditorPluginContext context)
    {
        var languageTextBox = MarkdownEditorUiFactory.CreateTextEditor(
            node is FencedCodeBlock fencedCode ? MarkdownSourceEditing.NormalizeLanguageHint(fencedCode.Info) : string.Empty,
            acceptsReturn: false,
            minHeight: 40);
        languageTextBox.Width = 180;
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineMetadataStyle(languageTextBox, context.RenderContext.FontSize);
        }

        var codeTextBox = MarkdownEditorUiFactory.CreateCodeEditor(node.Lines.ToString());
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineCodeStyle(codeTextBox, context.RenderContext.FontSize);
        }

        var settingsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                MarkdownEditorUiFactory.CreateFieldLabel("Language"),
                languageTextBox
            }
        };

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit the code block content and language hint. The built-in editor emits fenced code blocks when you apply changes."));
        }

        string BuildCurrentMarkdown() => MarkdownSourceEditing.BuildCodeFence(languageTextBox.Text ?? string.Empty, codeTextBox.Text ?? string.Empty);

        body.Children.Add(settingsRow);
        body.Children.Add(codeTextBox);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit code block",
            "Code editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            codeTextBox,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }

    private static bool IsMermaidCodeBlock(CodeBlock codeBlock)
    {
        return codeBlock switch
        {
            FencedCodeBlock fencedCode => MermaidLanguageAliases.Contains(MarkdownSourceEditing.NormalizeLanguageHint(fencedCode.Info)),
            _ => string.Equals(codeBlock.GetType().Name, "MermaidDiagramBlock", StringComparison.Ordinal)
        };
    }
}

public static class MarkdownEditorUiFactory
{
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly IBrush LightEditorBackground = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush DarkEditorBackground = new SolidColorBrush(Color.Parse("#111827"));
    private static readonly IBrush LightInputBackground = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush DarkInputBackground = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IBrush LightBorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1"));
    private static readonly IBrush DarkBorderBrush = new SolidColorBrush(Color.Parse("#475569"));
    private static readonly IBrush LightHeaderBackground = new SolidColorBrush(Color.Parse("#EEF2FF"));
    private static readonly IBrush DarkHeaderBackground = new SolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IBrush LightSectionBackground = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush DarkSectionBackground = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IBrush LightEditorForeground = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IBrush DarkEditorForeground = new SolidColorBrush(Color.Parse("#E5E7EB"));
    private static readonly IBrush LightSecondaryTextBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush DarkSecondaryTextBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush LightPrimaryButtonBackground = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush DarkPrimaryButtonBackground = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush PrimaryButtonForeground = Brushes.White;
    private static readonly IBrush LightToolbarButtonBackground = new SolidColorBrush(Color.Parse("#E2E8F0"));
    private static readonly IBrush DarkToolbarButtonBackground = new SolidColorBrush(Color.Parse("#1F2937"));
    private static readonly IBrush LightToolbarButtonForeground = new SolidColorBrush(Color.Parse("#0F172A"));
    private static readonly IBrush DarkToolbarButtonForeground = new SolidColorBrush(Color.Parse("#E5E7EB"));
    private static readonly IBrush LightInlineAccentBrush = new SolidColorBrush(Color.Parse("#93C5FD"));
    private static readonly IBrush DarkInlineAccentBrush = new SolidColorBrush(Color.Parse("#60A5FA"));
    private static readonly IBrush LightInlineToolbarBackground = new SolidColorBrush(Color.Parse("#EFF6FF"));
    private static readonly IBrush DarkInlineToolbarBackground = new SolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IBrush LightInlineSecondaryButtonBackground = new SolidColorBrush(Color.Parse("#F1F5F9"));
    private static readonly IBrush DarkInlineSecondaryButtonBackground = new SolidColorBrush(Color.Parse("#334155"));
    private static readonly IBrush LightInlineSecondaryButtonForeground = new SolidColorBrush(Color.Parse("#334155"));
    private static readonly IBrush DarkInlineSecondaryButtonForeground = new SolidColorBrush(Color.Parse("#E5E7EB"));
    private static readonly IBrush LightDestructiveForeground = new SolidColorBrush(Color.Parse("#B42318"));
    private static readonly IBrush DarkDestructiveForeground = new SolidColorBrush(Color.Parse("#FCA5A5"));

    public static IBrush EditorBackground => SelectThemeBrush(LightEditorBackground, DarkEditorBackground);

    public static IBrush InputBackground => SelectThemeBrush(LightInputBackground, DarkInputBackground);

    public static IBrush BorderBrush => SelectThemeBrush(LightBorderBrush, DarkBorderBrush);

    public static IBrush HeaderBackground => SelectThemeBrush(LightHeaderBackground, DarkHeaderBackground);

    public static IBrush SectionBackground => SelectThemeBrush(LightSectionBackground, DarkSectionBackground);

    public static IBrush EditorForeground => SelectThemeBrush(LightEditorForeground, DarkEditorForeground);

    private static IBrush SecondaryTextBrush => SelectThemeBrush(LightSecondaryTextBrush, DarkSecondaryTextBrush);

    private static IBrush PrimaryButtonBackground => SelectThemeBrush(LightPrimaryButtonBackground, DarkPrimaryButtonBackground);

    private static IBrush ToolbarButtonBackground => SelectThemeBrush(LightToolbarButtonBackground, DarkToolbarButtonBackground);

    private static IBrush ToolbarButtonForeground => SelectThemeBrush(LightToolbarButtonForeground, DarkToolbarButtonForeground);

    private static IBrush InlineAccentBrush => SelectThemeBrush(LightInlineAccentBrush, DarkInlineAccentBrush);

    private static IBrush InlineToolbarBackground => SelectThemeBrush(LightInlineToolbarBackground, DarkInlineToolbarBackground);

    private static IBrush InlineSecondaryButtonBackground => SelectThemeBrush(LightInlineSecondaryButtonBackground, DarkInlineSecondaryButtonBackground);

    private static IBrush InlineSecondaryButtonForeground => SelectThemeBrush(LightInlineSecondaryButtonForeground, DarkInlineSecondaryButtonForeground);

    private static IBrush DestructiveForeground => SelectThemeBrush(LightDestructiveForeground, DarkDestructiveForeground);

    public static Control CreateEditorSurface(
        MarkdownEditorPluginContext context,
        string title,
        string subtitle,
        Control body,
        Action apply,
        Action cancel,
        Control? focusTarget = null,
        bool preferInlineTextLayout = false,
        Func<string>? buildBlockMarkdownForActions = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.PresentationMode == MarkdownEditorPresentationMode.Card
            ? CreateEditorCard(context, title, subtitle, body, apply, cancel, focusTarget, buildBlockMarkdownForActions)
            : CreateInlineEditorSurface(context, title, subtitle, body, apply, cancel, focusTarget, preferInlineTextLayout, buildBlockMarkdownForActions);
    }

    public static Control CreateEditorCard(
        MarkdownEditorPluginContext context,
        string title,
        string subtitle,
        Control body,
        Action apply,
        Action cancel,
        Control? focusTarget = null,
        Func<string>? buildBlockMarkdownForActions = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(cancel);

        var applyButton = CreatePrimaryButton("Apply", apply);
        var cancelButton = CreateSecondaryButton("Cancel", cancel);
        var blockActions = CreateBlockActionPanel(context, buildBlockMarkdownForActions, compact: false);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                applyButton
            }
        };

        var root = new Border
        {
            Background = EditorBackground,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new Border
                    {
                        Background = HeaderBackground,
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(12, 10),
                        Child = new StackPanel
                        {
                            Spacing = 2,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = title,
                                    FontWeight = FontWeight.SemiBold,
                                    FontSize = 15
                                },
                                new TextBlock
                                {
                                    Text = subtitle,
                                    Foreground = SecondaryTextBrush
                                }
                            }
                        }
                    },
                }
            }
        };

        if (blockActions is not null)
        {
            ((StackPanel)root.Child!).Children.Add(blockActions);
        }

        ((StackPanel)root.Child!).Children.Add(body);
        ((StackPanel)root.Child!).Children.Add(footer);

        if (focusTarget is InputElement inputElement)
        {
            root.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => inputElement.Focus());
        }

        AttachEditorShortcuts(root, apply, cancel);

        return root;
    }

    public static void ApplyInlineParagraphStyle(TextBox textBox, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ApplyInlineTextBoxChrome(textBox, fontSize, FontWeight.Normal, FontStyle.Normal, fontFamily: null, minHeight: Math.Max(fontSize * 2.6, 68));
    }

    public static void ApplyInlineHeadingStyle(TextBox textBox, double baseFontSize, int level)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ApplyInlineTextBoxChrome(
            textBox,
            ResolveHeadingSize(baseFontSize, level),
            FontWeight.SemiBold,
            FontStyle.Normal,
            fontFamily: null,
            minHeight: Math.Max(baseFontSize * 2.4, 48));
    }

    public static void ApplyInlineListItemStyle(TextBox textBox, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ApplyInlineTextBoxChrome(textBox, fontSize, FontWeight.Normal, FontStyle.Normal, fontFamily: null, minHeight: Math.Max(fontSize * 2.4, 54));
    }

    public static void ApplyInlineTableCellStyle(TextBox textBox, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ApplyInlineTextBoxChrome(textBox, fontSize, FontWeight.Normal, FontStyle.Normal, fontFamily: null, minHeight: Math.Max(fontSize * 2, 36));
    }

    public static void ApplyInlineMetadataStyle(TextBox textBox, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        textBox.FontSize = Math.Max(fontSize - 1, 12);
        textBox.Background = InputBackground;
        textBox.Foreground = EditorForeground;
        textBox.BorderBrush = BorderBrush;
        textBox.BorderThickness = new Thickness(1);
        textBox.CornerRadius = new CornerRadius(6);
        textBox.Padding = new Thickness(8, 6);
    }

    public static void ApplyInlineCodeStyle(TextBox textBox, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        textBox.FontFamily = MonospaceFamily;
        textBox.FontSize = Math.Max(fontSize - 1, 12);
        textBox.Background = Brushes.Transparent;
        textBox.BorderBrush = Brushes.Transparent;
        textBox.BorderThickness = new Thickness(0);
        textBox.Padding = new Thickness(0);
    }

    public static TextBlock CreateInfoText(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SecondaryTextBrush
        };
    }

    public static TextBlock CreateFieldLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.Medium
        };
    }

    public static TextBox CreateTextEditor(string? text, bool acceptsReturn, double minHeight)
    {
        var textBox = new TextBox
        {
            Text = MarkdownSourceEditing.NormalizeBlockText(text),
            AcceptsReturn = acceptsReturn,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = minHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        return textBox;
    }

    public static TextBox CreateCodeEditor(string? text)
    {
        return new TextBox
        {
            Text = MarkdownSourceEditing.NormalizeBlockText(text),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 180,
            FontFamily = MonospaceFamily,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    public static Button CreatePrimaryButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Background = PrimaryButtonBackground,
            Foreground = PrimaryButtonForeground,
            Padding = new Thickness(14, 8)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Button CreateSecondaryButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Control CreateTextStyleToolbar(Func<TextBox?> resolveTextBox)
    {
        ArgumentNullException.ThrowIfNull(resolveTextBox);

        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        panel.Children.Add(CreateToolbarButton("Bold", () => ApplyWrapper(resolveTextBox(), "**", "**")));
        panel.Children.Add(CreateToolbarButton("Italic", () => ApplyWrapper(resolveTextBox(), "_", "_")));
        panel.Children.Add(CreateToolbarButton("Strike", () => ApplyWrapper(resolveTextBox(), "~~", "~~")));
        panel.Children.Add(CreateToolbarButton("Code", () => ApplyWrapper(resolveTextBox(), "`", "`")));
        panel.Children.Add(CreateToolbarButton("Link", () => ApplyLink(resolveTextBox())));

        return panel;
    }

    public static Control CreateCompactTextStyleToolbar(Func<TextBox?> resolveTextBox)
    {
        ArgumentNullException.ThrowIfNull(resolveTextBox);

        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        panel.Children.Add(CreateToolbarButton("B", () => ApplyWrapper(resolveTextBox(), "**", "**"), compact: true));
        panel.Children.Add(CreateToolbarButton("I", () => ApplyWrapper(resolveTextBox(), "_", "_"), compact: true));
        panel.Children.Add(CreateToolbarButton("S", () => ApplyWrapper(resolveTextBox(), "~~", "~~"), compact: true));
        panel.Children.Add(CreateToolbarButton("</>", () => ApplyWrapper(resolveTextBox(), "`", "`"), compact: true));
        panel.Children.Add(CreateToolbarButton("Link", () => ApplyLink(resolveTextBox()), compact: true));

        return panel;
    }

    private static Control CreateToolbarButton(string text, Action onClick, bool compact = false)
    {
        var button = new Button
        {
            Content = text,
            Margin = compact ? new Thickness(0, 0, 6, 6) : new Thickness(0, 0, 8, 8),
            Padding = compact ? new Thickness(8, 4) : new Thickness(10, 6),
            MinWidth = compact ? 0 : double.NaN,
            Background = compact ? InlineToolbarBackground : ToolbarButtonBackground,
            Foreground = ToolbarButtonForeground,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Focusable = false
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control CreateInlineEditorSurface(
        MarkdownEditorPluginContext context,
        string title,
        string subtitle,
        Control body,
        Action apply,
        Action cancel,
        Control? focusTarget,
        bool preferInlineTextLayout,
        Func<string>? buildBlockMarkdownForActions)
    {
        var applyButton = CreateInlineActionButton("Apply", apply, primary: true);
        var cancelButton = CreateInlineActionButton("Cancel", cancel, primary: false);
        var blockActions = CreateBlockActionPanel(context, buildBlockMarkdownForActions, compact: true);

        var titlePanel = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = preferInlineTextLayout ? 12 : 13,
                    Foreground = preferInlineTextLayout ? SecondaryTextBrush : null
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(subtitle) && !preferInlineTextLayout)
        {
            titlePanel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = SecondaryTextBrush,
                FontSize = 11
            });
        }

        var actionRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionRow.Children.Add(titlePanel);

        var shortcutHint = new TextBlock
        {
            Text = "Esc cancel • Ctrl/⌘+Enter apply",
            Foreground = SecondaryTextBrush,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(shortcutHint, 1);
        actionRow.Children.Add(shortcutHint);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children =
            {
                cancelButton,
                applyButton
            }
        };
        Grid.SetColumn(buttonPanel, 2);
        actionRow.Children.Add(buttonPanel);

        var root = new Border
        {
            Background = preferInlineTextLayout ? Brushes.Transparent : EditorBackground,
            BorderBrush = preferInlineTextLayout ? InlineAccentBrush : BorderBrush,
            BorderThickness = preferInlineTextLayout ? new Thickness(0, 0, 0, 1.5) : new Thickness(1.25),
            CornerRadius = preferInlineTextLayout ? new CornerRadius(0) : new CornerRadius(10),
            Padding = preferInlineTextLayout ? new Thickness(0, 0, 0, 6) : new Thickness(10),
            Child = new StackPanel
            {
                Spacing = preferInlineTextLayout ? 6 : 10,
                Children =
                {
                    actionRow,
                }
            }
        };

        if (blockActions is not null)
        {
            ((StackPanel)root.Child!).Children.Add(blockActions);
        }

        ((StackPanel)root.Child!).Children.Add(body);

        AttachEditorShortcuts(root, apply, cancel);

        if (focusTarget is InputElement inputElement)
        {
            root.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => inputElement.Focus());
        }

        return root;
    }

    private static Button CreateInlineActionButton(string text, Action onClick, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(10, 4),
            FontSize = 11,
            Background = primary ? PrimaryButtonBackground : InlineSecondaryButtonBackground,
            Foreground = primary ? PrimaryButtonForeground : InlineSecondaryButtonForeground,
            BorderBrush = primary ? PrimaryButtonBackground : BorderBrush,
            BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control? CreateBlockActionPanel(
        MarkdownEditorPluginContext context,
        Func<string>? buildBlockMarkdownForActions,
        bool compact)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.BlockTemplates.Count == 0 && buildBlockMarkdownForActions is null)
        {
            return CreateRemoveBlockButton(context, compact);
        }

        var panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        if (context.BlockTemplates.Count > 0 && buildBlockMarkdownForActions is not null)
        {
            panel.Children.Add(CreateTemplateActionComboBox(
                "Insert before…",
                context.BlockTemplates,
                template => context.InsertBlockBefore(template, buildBlockMarkdownForActions()),
                compact));
            panel.Children.Add(CreateTemplateActionComboBox(
                "Insert after…",
                context.BlockTemplates,
                template => context.InsertBlockAfter(template, buildBlockMarkdownForActions()),
                compact));
        }

        panel.Children.Add(CreateRemoveBlockButton(context, compact));
        return panel;
    }

    private static Control CreateRemoveBlockButton(MarkdownEditorPluginContext context, bool compact)
    {
        var button = compact
            ? CreateInlineActionButton("Remove block", context.RemoveBlock, primary: false)
            : CreateSecondaryButton("Remove block", context.RemoveBlock);
        button.Margin = compact ? new Thickness(0, 0, 6, 6) : new Thickness(0, 0, 8, 8);
        button.Foreground = DestructiveForeground;
        return button;
    }

    private static ComboBox CreateTemplateActionComboBox(
        string placeholder,
        IReadOnlyList<MarkdownBlockTemplate> templates,
        Action<MarkdownBlockTemplate> onSelected,
        bool compact)
    {
        var items = new List<BlockTemplateActionOption>(templates.Count + 1)
        {
            new(placeholder, null)
        };

        foreach (var template in templates)
        {
            items.Add(new(template.Label, template));
        }

        var comboBox = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = 0,
            MinWidth = compact ? 150 : 180,
            Margin = compact ? new Thickness(0, 0, 6, 6) : new Thickness(0, 0, 8, 8)
        };
        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not BlockTemplateActionOption { Template: { } template })
            {
                return;
            }

            onSelected(template);
            Dispatcher.UIThread.Post(() => comboBox.SelectedIndex = 0);
        };
        return comboBox;
    }

    private static void AttachEditorShortcuts(Control root, Action apply, Action cancel)
    {
        root.AddHandler(
            InputElement.KeyDownEvent,
            (_, eventArgs) =>
            {
                if (eventArgs.Key == Key.Escape)
                {
                    cancel();
                    eventArgs.Handled = true;
                    return;
                }

                if (eventArgs.Key == Key.Enter &&
                    (eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control) || eventArgs.KeyModifiers.HasFlag(KeyModifiers.Meta)))
                {
                    apply();
                    eventArgs.Handled = true;
                }
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void ApplyInlineTextBoxChrome(
        TextBox textBox,
        double fontSize,
        FontWeight fontWeight,
        FontStyle fontStyle,
        FontFamily? fontFamily,
        double minHeight)
    {
        textBox.FontSize = fontSize;
        textBox.FontWeight = fontWeight;
        textBox.FontStyle = fontStyle;
        if (fontFamily is not null)
        {
            textBox.FontFamily = fontFamily;
        }

        textBox.MinHeight = minHeight;
        textBox.Background = Brushes.Transparent;
        textBox.BorderBrush = Brushes.Transparent;
        textBox.BorderThickness = new Thickness(0);
        textBox.Padding = new Thickness(0);
    }

    private static double ResolveHeadingSize(double baseSize, int level)
    {
        return level switch
        {
            <= 1 => baseSize + 9,
            2 => baseSize + 7,
            3 => baseSize + 5,
            4 => baseSize + 3,
            5 => baseSize + 2,
            _ => baseSize + 1
        };
    }

    private static IBrush SelectThemeBrush(IBrush light, IBrush dark)
    {
        return Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? dark : light;
    }

    private static (int Start, int End) ResolveFormattingRange(TextBox textBox, string text)
    {
        var selectionStart = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var selectionEnd = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        if (selectionEnd > selectionStart)
        {
            return (selectionStart, selectionEnd);
        }

        return TryExpandSelectionToWord(text, textBox.CaretIndex, out var wordStart, out var wordEnd)
            ? (wordStart, wordEnd)
            : (selectionStart, selectionEnd);
    }

    private static bool TryExpandSelectionToWord(string text, int caretIndex, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var index = Math.Clamp(caretIndex, 0, text.Length);
        if (index == text.Length || !IsFormattingWordCharacter(text[index]))
        {
            if (index > 0 && IsFormattingWordCharacter(text[index - 1]))
            {
                index--;
            }
            else
            {
                return false;
            }
        }

        start = index;
        end = index + 1;

        while (start > 0 && IsFormattingWordCharacter(text[start - 1]))
        {
            start--;
        }

        while (end < text.Length && IsFormattingWordCharacter(text[end]))
        {
            end++;
        }

        return end > start;
    }

    private static bool IsFormattingWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or '-';
    }

    private sealed record BlockTemplateActionOption(string Label, MarkdownBlockTemplate? Template)
    {
        public override string ToString() => Label;
    }

    private static void ApplyWrapper(TextBox? textBox, string prefix, string suffix)
    {
        if (textBox is null)
        {
            return;
        }

        var text = textBox.Text ?? string.Empty;
        var (start, end) = ResolveFormattingRange(textBox, text);
        var selectedText = end > start ? text[start..end] : string.Empty;
        var replacement = string.Concat(prefix, selectedText, suffix);

        textBox.Text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end));
        var caretIndex = start + prefix.Length;
        if (selectedText.Length > 0)
        {
            textBox.SelectionStart = caretIndex;
            textBox.SelectionEnd = caretIndex + selectedText.Length;
        }
        else
        {
            textBox.SelectionStart = caretIndex;
            textBox.SelectionEnd = caretIndex;
            textBox.CaretIndex = caretIndex;
        }
        textBox.Focus();
    }

    private static void ApplyLink(TextBox? textBox)
    {
        if (textBox is null)
        {
            return;
        }

        var text = textBox.Text ?? string.Empty;
        var (start, end) = ResolveFormattingRange(textBox, text);
        var selectedText = end > start ? text[start..end] : string.Empty;
        var replacement = $"[{selectedText}]()";

        textBox.Text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(end));
        if (selectedText.Length > 0)
        {
            var urlStart = start + selectedText.Length + 3;
            textBox.SelectionStart = urlStart;
            textBox.SelectionEnd = urlStart;
            textBox.CaretIndex = urlStart;
        }
        else
        {
            var labelStart = start + 1;
            textBox.SelectionStart = labelStart;
            textBox.SelectionEnd = labelStart;
            textBox.CaretIndex = labelStart;
        }

        textBox.Focus();
    }
}
