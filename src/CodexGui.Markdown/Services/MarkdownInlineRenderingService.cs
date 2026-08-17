using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Emoji;
using Markdig.Extensions.Figures;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Footers;
using Markdig.Extensions.SmartyPants;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CodexGui.Markdown.Services;

public sealed class MarkdownInlineRenderingService : IMarkdownInlineRenderingService
{
    private static readonly HttpClient HttpClient = new();
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly IBrush LinkForeground = new SolidColorBrush(Color.Parse("#0A56C2"));
    private static readonly IBrush QuoteForeground = new SolidColorBrush(Color.Parse("#6E6E6E"));
    private static readonly IBrush CodeBackground = new SolidColorBrush(Color.Parse("#EEF1F5"));
    private static readonly IBrush SurfaceBackground = new SolidColorBrush(Color.Parse("#F6F8FA"));
    private static readonly IBrush SurfaceBorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush QuoteAccentBrush = new SolidColorBrush(Color.Parse("#D8DEE4"));
    private static readonly IBrush CodeTextForeground = new SolidColorBrush(Color.Parse("#1F2328"));
    private static readonly IBrush MarkedTextBackground = new SolidColorBrush(Color.Parse("#FFF1B8"));
    private static readonly IBrush InsertedTextForeground = new SolidColorBrush(Color.Parse("#116329"));
    private static readonly IBrush CodeKeywordForeground = new SolidColorBrush(Color.Parse("#CF222E"));
    private static readonly IBrush CodeTypeForeground = new SolidColorBrush(Color.Parse("#8250DF"));
    private static readonly IBrush CodeStringForeground = new SolidColorBrush(Color.Parse("#0A3069"));
    private static readonly IBrush CodeCommentForeground = new SolidColorBrush(Color.Parse("#6E7781"));
    private static readonly IBrush CodeNumberForeground = new SolidColorBrush(Color.Parse("#0550AE"));
    private static readonly IBrush CodePropertyForeground = new SolidColorBrush(Color.Parse("#953800"));
    private static readonly IBrush CodeTagForeground = new SolidColorBrush(Color.Parse("#1A7F37"));
    private static readonly IBrush CodeAttributeForeground = new SolidColorBrush(Color.Parse("#9A6700"));
    private static readonly IBrush CodePunctuationForeground = new SolidColorBrush(Color.Parse("#57606A"));
    private static readonly IBrush CodeHeaderBackground = new SolidColorBrush(Color.Parse("#EAEEF2"));
    private static readonly IBrush TableHeaderBackground = new SolidColorBrush(Color.Parse("#F3F4F6"));
    private static readonly IBrush TableAlternateRowBackground = new SolidColorBrush(Color.Parse("#FBFCFD"));
    private static readonly IReadOnlyDictionary<SmartyPantType, string> SmartyPantMapping =
        new Dictionary<SmartyPantType, string>(
            new SmartyPantOptions()
                .Mapping
                .ToDictionary(static pair => pair.Key, static pair => WebUtility.HtmlDecode(pair.Value)));
    private static readonly HashSet<string> CommonCodeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "abstract", "as", "async", "await", "base", "break", "case", "catch", "class", "const", "continue",
        "default", "delegate", "do", "else", "enum", "event", "explicit", "export", "extends", "extern",
        "false", "finally", "fixed", "for", "foreach", "from", "function", "goto", "if", "implicit",
        "implements", "import", "in", "interface", "internal", "is", "let", "lock", "namespace", "new",
        "null", "operator", "out", "override", "package", "params", "private", "protected", "public",
        "readonly", "record", "ref", "return", "sealed", "static", "struct", "super", "switch", "this",
        "throw", "true", "try", "typeof", "unchecked", "unsafe", "using", "var", "virtual", "void",
        "volatile", "while", "with", "yield"
    };
    private static readonly HashSet<string> TypeLikeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "bool", "byte", "char", "date", "datetime", "decimal", "double", "dynamic", "float", "guid", "int",
        "long", "nint", "nuint", "object", "sbyte", "short", "string", "task", "uint", "ulong", "ushort"
    };
    private static readonly HashSet<string> LiteralWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "false", "null", "true", "undefined"
    };
    private static readonly HashSet<string> ShellKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "case", "do", "done", "echo", "elif", "else", "esac", "export", "fi", "for", "function", "if",
        "in", "local", "readonly", "return", "select", "set", "then", "until", "while"
    };
    private static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "as", "asc", "by", "case", "create", "delete", "desc", "distinct", "drop", "else", "end",
        "from", "group", "having", "inner", "insert", "into", "join", "left", "like", "limit", "not",
        "null", "on", "or", "order", "outer", "right", "select", "set", "table", "then", "union", "update",
        "values", "when", "where"
    };
    private const double DefaultMaxImageWidth = 520;
    private const double DefaultMaxImageHeight = 360;
    private readonly IReadOnlyList<IMarkdownBlockRenderingPlugin> _blockRenderingPlugins;
    private readonly IReadOnlyList<IMarkdownInlineRenderingPlugin> _inlineRenderingPlugins;

    public MarkdownInlineRenderingService(
        IEnumerable<IMarkdownBlockRenderingPlugin>? blockRenderingPlugins = null,
        IEnumerable<IMarkdownInlineRenderingPlugin>? inlineRenderingPlugins = null)
    {
        _blockRenderingPlugins = (blockRenderingPlugins ?? [])
            .OrderBy(static plugin => plugin.Order)
            .ToArray();
        _inlineRenderingPlugins = (inlineRenderingPlugins ?? [])
            .OrderBy(static plugin => plugin.Order)
            .ToArray();
    }

    public MarkdownRenderResult Render(MarkdownParseResult parseResult, MarkdownRenderContext context)
    {
        var renderedInlines = new InlineCollection();
        var state = new RenderState(
            parseResult,
            context,
            renderedInlines,
            quoteDepth: 0,
            listDepth: 0,
            _blockRenderingPlugins,
            _inlineRenderingPlugins,
            sourceObject: null);

        RenderBlockSequence(parseResult.Document, state, lineBreaksBetweenBlocks: 2);
        RenderDeferredMetadataSections(state);
        TrimTrailingLineBreaks(renderedInlines);

        return new MarkdownRenderResult(
            renderedInlines,
            context.ResourceTracker,
            parseResult,
            MarkdownRenderMapBuilder.Build(renderedInlines));
    }

    private static void RenderBlockSequence(ContainerBlock container, RenderState state, int lineBreaksBetweenBlocks)
    {
        var first = true;
        foreach (var block in container)
        {
            if (!first)
            {
                AppendLineBreaks(state.Output, lineBreaksBetweenBlocks, state.SourceObject);
            }

            RenderBlock(block, state);
            first = false;
        }
    }

    private static void RenderBlock(Block block, RenderState state)
    {
        var blockState = state.WithSource(block);
        if (TryRenderActiveEditor(block, blockState))
        {
            return;
        }

        if (TryRenderBlockWithPlugins(block, blockState))
        {
            return;
        }

        switch (block)
        {
            case AlertBlock alert:
                RenderAlertBlock(alert, blockState);
                break;
            case HeadingBlock heading:
                RenderHeadingBlock(heading, blockState);
                break;
            case ParagraphBlock paragraph:
                RenderParagraphBlock(paragraph, blockState, prependQuotePrefix: true, skipLeadingTaskInline: false);
                break;
            case QuoteBlock quote:
                RenderQuoteBlock(quote, blockState);
                break;
            case ListBlock list:
                RenderListBlock(list, blockState);
                break;
            case DefinitionList definitionList:
                RenderDefinitionList(definitionList, blockState);
                break;
            case Table table:
                RenderTableBlock(table, blockState);
                break;
            case YamlFrontMatterBlock yamlFrontMatterBlock:
                RenderYamlFrontMatterBlock(yamlFrontMatterBlock, blockState);
                break;
            case FencedCodeBlock fencedCode:
                RenderCodeBlock(fencedCode, blockState, fencedCode.Info);
                break;
            case CodeBlock codeBlock:
                RenderCodeBlock(codeBlock, blockState, null);
                break;
            case ThematicBreakBlock:
                AppendQuotePrefix(blockState);
                AppendStyledRun(blockState.Output, "────────────────────────────────", sourceObject: blockState.SourceObject, fontWeight: FontWeight.Medium, foreground: QuoteForeground);
                break;
            case CustomContainer customContainer:
                RenderCustomContainerBlock(customContainer, blockState);
                break;
            case Figure figure:
                RenderFigureBlock(figure, blockState);
                break;
            case FigureCaption figureCaption:
                RenderFigureCaptionBlock(figureCaption, blockState);
                break;
            case HtmlBlock htmlBlock:
                RenderHtmlBlock(htmlBlock, blockState);
                break;
            case FooterBlock footerBlock:
                RenderFooterBlock(footerBlock, blockState);
                break;
            case FootnoteGroup footnoteGroup:
                RenderFootnoteGroup(footnoteGroup, blockState);
                break;
            case Abbreviation:
            case BlankLineBlock:
            case EmptyBlock:
            case LinkReferenceDefinitionGroup:
                break;
            case ContainerBlock container:
                RenderBlockSequence(container, blockState, lineBreaksBetweenBlocks: 1);
                break;
            case LeafBlock leaf:
                RenderLeafFallback(leaf, blockState);
                break;
            default:
                AppendQuotePrefix(blockState);
                AppendStyledRun(blockState.Output, block.ToString(), sourceObject: blockState.SourceObject);
                break;
        }
    }

    private static bool TryRenderActiveEditor(Block block, RenderState state)
    {
        if (state.Options.EditorState?.ActiveSession is not { } session)
        {
            return false;
        }

        if (!MarkdownSourceSpan.FromMarkdig(block.Span).Equals(session.SourceSpan))
        {
            return false;
        }

        var editor = state.Options.EditorState.EditingService.CreateEditor(new MarkdownEditorRenderRequest
        {
            Node = block,
            ParseResult = state.ParseResult,
            RenderContext = state.Options,
            Session = session,
            Preferences = state.Options.EditorState.Preferences,
            AvailableWidth = ResolveRichBlockWidth(state),
            CommitReplacement = replacementMarkdown => state.Options.EditorState.CommitReplacement(session, replacementMarkdown),
            InsertBlockBefore = (template, currentBlockMarkdown) => state.Options.EditorState.InsertBlockBefore(session, template, currentBlockMarkdown),
            InsertBlockAfter = (template, currentBlockMarkdown) => state.Options.EditorState.InsertBlockAfter(session, template, currentBlockMarkdown),
            RemoveBlock = () => state.Options.EditorState.RemoveBlock(session),
            CancelEdit = state.Options.EditorState.CancelEdit
        });

        if (editor is null)
        {
            return false;
        }

        state.Output.Add(CreateInlineControlContainer(
            CreateRichBlockHost(editor, state),
            block,
            MarkdownRenderedElementKind.BlockControl));

        return true;
    }

    private static bool TryRenderActiveInlineEditor(Markdig.Syntax.Inlines.Inline inline, InlineCollection output, RenderState state)
    {
        if (state.Options.EditorState?.ActiveSession is not { } session)
        {
            return false;
        }

        if (!MarkdownSourceSpan.FromMarkdig(inline.Span).Equals(session.SourceSpan))
        {
            return false;
        }

        var editor = state.Options.EditorState.EditingService.CreateEditor(new MarkdownEditorRenderRequest
        {
            Node = inline,
            ParseResult = state.ParseResult,
            RenderContext = state.Options,
            Session = session,
            Preferences = state.Options.EditorState.Preferences,
            AvailableWidth = ResolveInlineEditorWidth(state),
            CommitReplacement = replacementMarkdown => state.Options.EditorState.CommitReplacement(session, replacementMarkdown),
            InsertBlockBefore = (template, currentBlockMarkdown) => state.Options.EditorState.InsertBlockBefore(session, template, currentBlockMarkdown),
            InsertBlockAfter = (template, currentBlockMarkdown) => state.Options.EditorState.InsertBlockAfter(session, template, currentBlockMarkdown),
            RemoveBlock = () => state.Options.EditorState.RemoveBlock(session),
            CancelEdit = state.Options.EditorState.CancelEdit
        });

        if (editor is null)
        {
            return false;
        }

        output.Add(CreateInlineControlContainer(
            editor,
            inline,
            MarkdownRenderedElementKind.InlineControl,
            BaselineAlignment.Center));
        return true;
    }

    private static bool TryRenderBlockWithPlugins(Block block, RenderState state)
    {
        if (state.BlockRenderingPlugins.Count == 0)
        {
            return false;
        }

        foreach (var plugin in state.BlockRenderingPlugins)
        {
            if (!plugin.CanRender(block))
            {
                continue;
            }

            var pluginContext = new MarkdownBlockRenderingPluginContext(
                block,
                state.ParseResult,
                state.Options,
                state.Output,
                state.QuoteDepth,
                state.ListDepth,
                ResolveRichBlockWidth(state),
                addBlockControl: (control, hitTestHandler) => state.Output.Add(CreateInlineControlContainer(CreateRichBlockHost(control, state), state.SourceObject, MarkdownRenderedElementKind.BlockControl, hitTestHandler: hitTestHandler)),
                resolveUri: url => ResolveUri(state.Options, url));

            if (plugin.TryRender(pluginContext))
            {
                return true;
            }
        }

        return false;
    }

    private static void RenderHeadingBlock(HeadingBlock heading, RenderState state)
    {
        AppendQuotePrefix(state);
        AppendListIndent(state, extraDepth: 0);

        var headingSpan = new Span
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = ResolveHeadingSize(state.Options.FontSize, heading.Level)
        };
        AttachElementInfo(headingSpan, state.SourceObject, MarkdownRenderedElementKind.Text);

        RenderInlineContainer(heading.Inline, headingSpan.Inlines, state, skipLeadingTaskInline: false);
        state.Output.Add(headingSpan);
    }

    private static void RenderParagraphBlock(
        ParagraphBlock paragraph,
        RenderState state,
        bool prependQuotePrefix,
        bool skipLeadingTaskInline)
    {
        if (prependQuotePrefix)
        {
            AppendQuotePrefix(state);
            AppendListIndent(state, extraDepth: 0);
        }

        if (paragraph.Inline is not null)
        {
            RenderInlineContainer(paragraph.Inline, state.Output, state, skipLeadingTaskInline);
            return;
        }

        var plain = NormalizeMarkdownLineText(paragraph.Lines.ToString());
        AppendStyledRun(state.Output, plain, sourceObject: state.SourceObject);
    }

    private static void RenderQuoteBlock(QuoteBlock quote, RenderState state)
    {
        RenderBlockSequence(quote, state.WithQuoteDepth(state.QuoteDepth + 1), lineBreaksBetweenBlocks: 1);
    }

    private static void RenderListBlock(ListBlock list, RenderState state)
    {
        var itemIndex = 0;
        var orderedStart = TryParseOrderedStart(list.OrderedStart);

        foreach (var child in list)
        {
            if (child is not ListItemBlock listItem)
            {
                continue;
            }

            if (itemIndex > 0)
            {
                AppendLineBreaks(state.Output, 1, state.SourceObject);
            }

            var itemState = state.WithSource(listItem);
            AppendQuotePrefix(itemState);
            AppendListIndent(itemState, extraDepth: 0);

            var marker = TryResolveTaskMarker(listItem, out var isChecked)
                ? isChecked ? "☑" : "☐"
                : list.IsOrdered
                    ? $"{orderedStart + itemIndex}{(list.OrderedDelimiter == '\0' ? '.' : list.OrderedDelimiter)}"
                    : "•";

            AppendStyledRun(itemState.Output, $"{marker} ", sourceObject: itemState.SourceObject, fontWeight: FontWeight.SemiBold);

            RenderListItemContent(
                listItem,
                itemState.WithListDepth(itemState.ListDepth + 1),
                skipLeadingTaskInline: TryResolveTaskMarker(listItem, out _));

            itemIndex++;
        }
    }

    private static void RenderListItemContent(ListItemBlock listItem, RenderState state, bool skipLeadingTaskInline)
    {
        var firstBlock = true;
        foreach (var nested in listItem)
        {
            switch (nested)
            {
                case ParagraphBlock paragraph:
                    if (!firstBlock)
                    {
                        AppendLineBreaks(state.Output, 1, state.SourceObject);
                        AppendQuotePrefix(state);
                        AppendListIndent(state, extraDepth: 0);
                    }

                    RenderParagraphBlock(
                        paragraph,
                        state,
                        prependQuotePrefix: false,
                        skipLeadingTaskInline: firstBlock && skipLeadingTaskInline);
                    break;
                case ListBlock nestedList:
                    AppendLineBreaks(state.Output, 1, state.SourceObject);
                    RenderListBlock(nestedList, state);
                    break;
                default:
                    if (!firstBlock)
                    {
                        AppendLineBreaks(state.Output, 1, state.SourceObject);
                    }

                    RenderBlock(nested, state);
                    break;
            }

            firstBlock = false;
        }
    }

    private static bool TryResolveTaskMarker(ListItemBlock listItem, out bool isChecked)
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

    private static void RenderTableBlock(Table table, RenderState state)
    {
        var tableRows = BuildTableRows(table);
        if (tableRows.Count == 0)
        {
            return;
        }

        var columnCount = tableRows.Max(static row => row.Cells.Count);
        if (columnCount == 0)
        {
            return;
        }

        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var tableSelectionGroup = new object();
        var tableHitTargets = new List<TableCellHitTarget>();

        for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(CalculateTableColumnWeight(tableRows, columnIndex), GridUnitType.Star)
            });
        }

        for (var rowIndex = 0; rowIndex < tableRows.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var cellData = columnIndex < tableRows[rowIndex].Cells.Count
                    ? tableRows[rowIndex].Cells[columnIndex]
                    : TableCellData.Empty;
                var alignment = columnIndex < table.ColumnDefinitions.Count ? table.ColumnDefinitions[columnIndex].Alignment : null;
                var cellBorder = CreateTableCellBorder(
                    cellData,
                    alignment,
                    tableRows[rowIndex].IsHeader,
                    rowIndex,
                    columnIndex,
                    columnCount,
                    tableSelectionGroup,
                    state);

                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, columnIndex);
                grid.Children.Add(cellBorder);

                if (cellData.Cell is not null &&
                    MarkdownRenderedElementMetadata.CreateAstNodeInfo(cellData.Cell) is { } cellAstNode)
                {
                    tableHitTargets.Add(new TableCellHitTarget(cellBorder, cellAstNode));
                }
            }
        }

        var tableBorder = new MarkdownRichBlockBorder
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = grid
        };

        state.Output.Add(CreateInlineControlContainer(
            CreateRichBlockHost(tableBorder, state),
            state.SourceObject,
            MarkdownRenderedElementKind.BlockControl,
            hitTestHandler: CreateTableHitTestHandler(tableBorder, tableHitTargets)));
    }

    private static List<TableRowData> BuildTableRows(Table table)
    {
        var rows = new List<TableRowData>();
        foreach (var rowObject in table)
        {
            if (rowObject is not TableRow row)
            {
                continue;
            }
            var cells = new List<TableCellData>();
            foreach (var cellObject in row)
            {
                if (cellObject is not TableCell cell)
                {
                    continue;
                }

                cells.Add(new TableCellData(cell, ExtractTableCellText(cell)));
            }

            rows.Add(new TableRowData(row.IsHeader, cells));
        }

        return rows;
    }

    private static double CalculateTableColumnWeight(IReadOnlyList<TableRowData> tableRows, int columnIndex)
    {
        var maxLength = tableRows.Max(row => columnIndex < row.Cells.Count ? row.Cells[columnIndex].PlainText.Length : 0);
        return Math.Clamp((maxLength / 18d) + 1, 1, 4);
    }

    private static Border CreateTableCellBorder(
        TableCellData cellData,
        TableColumnAlign? alignment,
        bool isHeader,
        int rowIndex,
        int columnIndex,
        int columnCount,
        object tableSelectionGroup,
        RenderState state)
    {
        return new MarkdownRichBlockBorder
        {
            Background = isHeader
                ? TableHeaderBackground
                : rowIndex % 2 == 0
                    ? Brushes.Transparent
                    : TableAlternateRowBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(0, 0, columnIndex == columnCount - 1 ? 0 : 1, 1),
            Padding = new Thickness(12, 8),
            Child = CreateTableCellContent(
                cellData,
                alignment,
                isHeader,
                tableSelectionGroup,
                rowIndex,
                columnIndex,
                state)
        };
    }

    private static Control CreateTableCellContent(
        TableCellData cellData,
        TableColumnAlign? alignment,
        bool isHeader,
        object tableSelectionGroup,
        int rowIndex,
        int columnIndex,
        RenderState state)
    {
        var textBlock = new TextBlock
        {
            FontSize = state.Options.FontSize,
            FontFamily = state.Options.FontFamily,
            Foreground = state.Options.Foreground,
            FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = alignment switch
            {
                TableColumnAlign.Center => TextAlignment.Center,
                TableColumnAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            }
        };

        var inlines = new InlineCollection();
        if (cellData.Cell is not null)
        {
            RenderTableCellContent(cellData.Cell, inlines, state);
        }

        MarkdownSelectionNormalizer.IsolateTextSegments(inlines, state.Options, state.ParseResult);
        textBlock.Inlines = inlines;
        MarkdownDocumentSelection.RegisterTableCellSegments(
            textBlock,
            tableSelectionGroup,
            rowIndex,
            columnIndex);
        return textBlock;
    }

    private static void RenderTableCellContent(TableCell cell, InlineCollection output, RenderState state)
    {
        var cellState = new RenderState(
            state.ParseResult,
            state.Options,
            output,
            quoteDepth: 0,
            listDepth: 0,
            state.BlockRenderingPlugins,
            state.InlineRenderingPlugins,
            sourceObject: cell);
        var first = true;

        foreach (var block in cell)
        {
            if (!first)
            {
                AppendLineBreaks(output, 1, cellState.SourceObject);
            }

            switch (block)
            {
                case ParagraphBlock paragraph when paragraph.Inline is not null:
                    RenderInlineContainer(paragraph.Inline, output, cellState, skipLeadingTaskInline: false);
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    RenderInlineContainer(leaf.Inline, output, cellState, skipLeadingTaskInline: false);
                    break;
                case LeafBlock leaf:
                    AppendStyledRun(output, NormalizeMarkdownLineText(leaf.Lines.ToString()), sourceObject: leaf);
                    break;
                case ContainerBlock container:
                    AppendStyledRun(output, ExtractContainerText(container), sourceObject: container);
                    break;
                default:
                    AppendStyledRun(output, block.ToString(), sourceObject: block);
                    break;
            }

            first = false;
        }

        TrimTrailingLineBreaks(output);
    }

    private static string ExtractTableCellText(TableCell cell)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var block in cell)
        {
            var segment = block switch
            {
                ParagraphBlock paragraph when paragraph.Inline is not null => ExtractInlineText(paragraph.Inline),
                LeafBlock leaf when leaf.Inline is not null => ExtractInlineText(leaf.Inline),
                LeafBlock leaf => NormalizeMarkdownLineText(leaf.Lines.ToString()),
                ContainerBlock container => ExtractContainerText(container),
                _ => block.ToString() ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            if (!first)
            {
                builder.Append(' ');
            }

            builder.Append(segment.Trim());
            first = false;
        }

        return builder
            .ToString()
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private static string ExtractContainerText(ContainerBlock container)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var block in container)
        {
            var segment = block switch
            {
                ParagraphBlock paragraph when paragraph.Inline is not null => ExtractInlineText(paragraph.Inline),
                LeafBlock leaf when leaf.Inline is not null => ExtractInlineText(leaf.Inline),
                LeafBlock leaf => NormalizeMarkdownLineText(leaf.Lines.ToString()),
                ContainerBlock nested => ExtractContainerText(nested),
                _ => block.ToString() ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            if (!first)
            {
                builder.Append(' ');
            }

            builder.Append(segment.Trim());
            first = false;
        }

        return builder.ToString();
    }

    private static void RenderCodeBlock(CodeBlock block, RenderState state, string? languageHint)
    {
        var code = MarkdownCodeBlockRendering.NormalizeCode(block.Lines.ToString());
        var inlines = CreatePlainCodeInlines(code, block);
        var lineCount = string.IsNullOrEmpty(code) ? 0 : code.Split('\n', StringSplitOptions.None).Length;
        var surface = MarkdownCodeBlockRendering.CreateSurface(
            block,
            code,
            inlines,
            languageHint,
            lineCount == 1 ? "1 line" : $"{lineCount} lines",
            state.Options,
            textForeground: CodeTextForeground,
            metaForeground: QuoteForeground);

        AddRichBlockControl(state, surface.Control, surface.HitTestHandler);
    }

    private static void RenderAlertBlock(AlertBlock alert, RenderState state)
    {
        var presentation = MarkdownCalloutRendering.ResolvePresentation(alert.Kind.ToString(), fallbackTitle: "Alert");
        var body = CreateRenderedTextBlock(
            state,
            alert,
            nestedState => RenderBlockSequence(alert, nestedState, lineBreaksBetweenBlocks: 1));

        AddRichBlockControl(
            state,
            MarkdownCalloutRendering.CreateCalloutSurface(
                presentation.Title,
                subtitle: null,
                body,
                presentation.AccentBrush,
                presentation.Background));
    }

    private static void RenderDefinitionList(DefinitionList definitionList, RenderState state)
    {
        var itemIndex = 0;
        foreach (var definitionItem in definitionList.OfType<DefinitionItem>())
        {
            if (itemIndex > 0)
            {
                AppendLineBreaks(state.Output, 1, state.SourceObject);
            }

            RenderDefinitionItem(definitionItem, state.WithSource(definitionItem));
            itemIndex++;
        }
    }

    private static void RenderDefinitionItem(DefinitionItem definitionItem, RenderState state)
    {
        var terms = new List<DefinitionTerm>();
        var contentBlocks = new List<Block>();

        foreach (var child in definitionItem)
        {
            switch (child)
            {
                case DefinitionTerm definitionTerm:
                    terms.Add(definitionTerm);
                    break;
                case BlankLineBlock:
                    break;
                default:
                    contentBlocks.Add(child);
                    break;
            }
        }

        for (var termIndex = 0; termIndex < terms.Count; termIndex++)
        {
            if (termIndex > 0)
            {
                AppendLineBreaks(state.Output, 1, state.SourceObject);
            }

            RenderDefinitionTerm(terms[termIndex], state.WithSource(terms[termIndex]));
        }

        var definitionState = state.WithListDepth(state.ListDepth + 1);
        for (var blockIndex = 0; blockIndex < contentBlocks.Count; blockIndex++)
        {
            AppendLineBreaks(state.Output, 1, state.SourceObject);
            RenderBlock(contentBlocks[blockIndex], definitionState.WithSource(contentBlocks[blockIndex]));
        }
    }

    private static void RenderDefinitionTerm(DefinitionTerm definitionTerm, RenderState state)
    {
        AppendQuotePrefix(state);
        AppendListIndent(state, extraDepth: 0);

        var termSpan = new Span
        {
            FontWeight = FontWeight.SemiBold
        };
        AttachElementInfo(termSpan, state.SourceObject, MarkdownRenderedElementKind.Text);

        if (definitionTerm.Inline is not null)
        {
            RenderInlineContainer(definitionTerm.Inline, termSpan.Inlines, state, skipLeadingTaskInline: false);
        }
        else
        {
            AppendStyledRun(termSpan.Inlines, NormalizeMarkdownLineText(definitionTerm.Lines.ToString()), sourceObject: state.SourceObject, fontWeight: FontWeight.SemiBold);
        }

        state.Output.Add(termSpan);
    }

    private static void RenderCustomContainerBlock(CustomContainer customContainer, RenderState state)
    {
        var label = string.IsNullOrWhiteSpace(customContainer.Info)
            ? "Container"
            : MarkdownCalloutRendering.FormatLabel(customContainer.Info);
        var subtitle = string.IsNullOrWhiteSpace(customContainer.Arguments)
            ? null
            : customContainer.Arguments.Trim();
        var presentation = MarkdownCalloutRendering.ResolvePresentation(customContainer.Info, fallbackTitle: label);
        var body = CreateRenderedTextBlock(
            state,
            customContainer,
            nestedState => RenderBlockSequence(customContainer, nestedState, lineBreaksBetweenBlocks: 1));

        AddRichBlockControl(
            state,
            MarkdownCalloutRendering.CreateCalloutSurface(
                presentation.Title,
                subtitle,
                body,
                presentation.AccentBrush,
                presentation.Background));
    }

    private static void RenderFigureBlock(Figure figure, RenderState state)
    {
        var panel = new StackPanel
        {
            Spacing = 8
        };

        var body = CreateRenderedTextBlock(
            state,
            figure,
            nestedState =>
            {
                var first = true;
                foreach (var child in figure)
                {
                    if (child is FigureCaption)
                    {
                        continue;
                    }

                    if (!first)
                    {
                        AppendLineBreaks(nestedState.Output, 1, figure);
                    }

                    RenderBlock(child, nestedState.WithSource(child));
                    first = false;
                }
            });

        if (body.Inlines?.Count > 0)
        {
            panel.Children.Add(body);
        }

        if (figure.OfType<FigureCaption>().FirstOrDefault() is { } caption)
        {
            panel.Children.Add(CreateRenderedTextBlock(
                state,
                caption,
                nestedState => RenderLeafFallback(caption, nestedState.WithSource(caption)),
                fontSize: Math.Max(state.Options.FontSize - 1, 11),
                foreground: QuoteForeground,
                textAlignment: TextAlignment.Center));
        }

        if (panel.Children.Count == 0)
        {
            return;
        }

        AddRichBlockControl(
            state,
            new Border
            {
                Background = SurfaceBackground,
                BorderBrush = SurfaceBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = panel
            });
    }

    private static void RenderFigureCaptionBlock(FigureCaption figureCaption, RenderState state)
    {
        AddRichBlockControl(
            state,
            new Border
            {
                Padding = new Thickness(8, 4),
                Child = CreateRenderedTextBlock(
                    state,
                    figureCaption,
                    nestedState => RenderLeafFallback(figureCaption, nestedState.WithSource(figureCaption)),
                    fontSize: Math.Max(state.Options.FontSize - 1, 11),
                    foreground: QuoteForeground,
                    textAlignment: TextAlignment.Center)
            });
    }

    private static void RenderFooterBlock(FooterBlock footerBlock, RenderState state)
    {
        AddRichBlockControl(
            state,
            new Border
            {
                BorderBrush = SurfaceBorderBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 0),
                Child = CreateRenderedTextBlock(
                    state,
                    footerBlock,
                    nestedState => RenderBlockSequence(footerBlock, nestedState, lineBreaksBetweenBlocks: 1),
                    fontSize: Math.Max(state.Options.FontSize - 1, 11),
                    foreground: QuoteForeground)
            });
    }

    private static void RenderHtmlBlock(HtmlBlock htmlBlock, RenderState state)
    {
        AppendQuotePrefix(state);
        AppendListIndent(state, extraDepth: 0);

        var html = NormalizeMarkdownLineText(htmlBlock.Lines.ToString());
        AppendStyledRun(state.Output, html, sourceObject: state.SourceObject, fontFamily: MonospaceFamily, foreground: QuoteForeground);
    }

    private static void RenderYamlFrontMatterBlock(YamlFrontMatterBlock yamlFrontMatterBlock, RenderState state)
    {
        var yaml = MarkdownCodeBlockRendering.NormalizeCode(yamlFrontMatterBlock.Lines.ToString());
        var inlines = CreatePlainCodeInlines(yaml, yamlFrontMatterBlock);
        var surface = MarkdownCodeBlockRendering.CreateSurface(
            yamlFrontMatterBlock,
            yaml,
            inlines,
            languageHint: "yaml",
            metaText: "Front matter",
            state.Options,
            textForeground: CodeTextForeground,
            metaForeground: QuoteForeground);

        AddRichBlockControl(state, surface.Control, surface.HitTestHandler);
    }

    private static void RenderFootnoteGroup(FootnoteGroup footnoteGroup, RenderState state)
    {
        var footnotes = footnoteGroup
            .OfType<Footnote>()
            .OrderBy(static footnote => footnote.Order)
            .ToList();

        if (footnotes.Count == 0)
        {
            return;
        }

        AppendQuotePrefix(state);
        AppendListIndent(state, extraDepth: 0);
        AppendStyledRun(state.Output, "────────────────────────────────", sourceObject: state.SourceObject, fontWeight: FontWeight.Medium, foreground: QuoteForeground);
        AppendLineBreaks(state.Output, 1, state.SourceObject);
        AppendQuotePrefix(state);
        AppendListIndent(state, extraDepth: 0);
        AppendStyledRun(state.Output, "Footnotes", sourceObject: state.SourceObject, fontWeight: FontWeight.SemiBold, foreground: QuoteForeground);

        foreach (var footnote in footnotes)
        {
            AppendLineBreaks(state.Output, 1, state.SourceObject);
            AppendQuotePrefix(state);
            AppendListIndent(state, extraDepth: 0);
            AppendStyledRun(state.Output, $"[{footnote.Order}] ", sourceObject: footnote, fontWeight: FontWeight.SemiBold, foreground: LinkForeground);
            RenderFootnoteContent(footnote, state.WithListDepth(state.ListDepth + 1));
        }
    }

    private static void RenderFootnoteContent(Footnote footnote, RenderState state)
    {
        var firstBlock = true;
        foreach (var nested in footnote)
        {
            switch (nested)
            {
                case ParagraphBlock paragraph:
                    if (!firstBlock)
                    {
                        AppendLineBreaks(state.Output, 1, state.SourceObject);
                        AppendQuotePrefix(state);
                        AppendListIndent(state, extraDepth: 0);
                    }

                    RenderParagraphBlock(
                        paragraph,
                        state,
                        prependQuotePrefix: false,
                        skipLeadingTaskInline: false);
                    break;
                case ListBlock nestedList:
                    AppendLineBreaks(state.Output, 1, state.SourceObject);
                    RenderListBlock(nestedList, state);
                    break;
                default:
                    if (!firstBlock)
                    {
                        AppendLineBreaks(state.Output, 1, state.SourceObject);
                    }

                    RenderBlock(nested, state);
                    break;
            }

            firstBlock = false;
        }
    }

    private static void RenderDeferredMetadataSections(RenderState state)
    {
        var linkReferences = GetUserLinkReferenceDefinitions(state.ParseResult.Document);
        var abbreviations = GetDocumentAbbreviations(state.ParseResult.Document);
        if (linkReferences.Count == 0 && abbreviations.Count == 0)
        {
            return;
        }

        if (state.Output.Count > 0)
        {
            AppendLineBreaks(state.Output, 2, sourceObject: null);
        }

        var renderedSection = false;
        if (linkReferences.Count > 0)
        {
            RenderMetadataSectionHeading("Link references", linkReferences[0], state);
            renderedSection = true;
            for (var index = 0; index < linkReferences.Count; index++)
            {
                if (index > 0)
                {
                    AppendLineBreaks(state.Output, 1, linkReferences[index]);
                }

                RenderLinkReferenceDefinition(linkReferences[index], state.WithSource(linkReferences[index]));
            }
        }

        if (abbreviations.Count > 0)
        {
            if (renderedSection)
            {
                AppendLineBreaks(state.Output, 2, abbreviations[0]);
            }

            RenderMetadataSectionHeading("Abbreviations", abbreviations[0], state);
            for (var index = 0; index < abbreviations.Count; index++)
            {
                if (index > 0)
                {
                    AppendLineBreaks(state.Output, 1, abbreviations[index]);
                }

                RenderAbbreviationDefinition(abbreviations[index], state.WithSource(abbreviations[index]));
            }
        }
    }

    private static List<LinkReferenceDefinition> GetUserLinkReferenceDefinitions(MarkdownDocument document)
    {
        return document
            .OfType<LinkReferenceDefinitionGroup>()
            .SelectMany(static group => group.Links.Values)
            .Where(static definition => definition.GetType() == typeof(LinkReferenceDefinition) && !string.IsNullOrWhiteSpace(definition.Label))
            .OrderBy(static definition => definition.Span.Start)
            .ToList();
    }

    private static List<Abbreviation> GetDocumentAbbreviations(MarkdownDocument document)
    {
        var definitions = AbbreviationHelper.GetAbbreviations(document);
        return definitions is null
            ? []
            : definitions.Values
                .Where(static abbreviation => !string.IsNullOrWhiteSpace(abbreviation.Label) && abbreviation.Span.Start >= 0)
                .OrderBy(static abbreviation => abbreviation.Span.Start)
                .ToList();
    }

    private static void RenderMetadataSectionHeading(string title, MarkdownObject sourceObject, RenderState state)
    {
        AppendStyledRun(
            state.Output,
            title,
            sourceObject: sourceObject,
            fontWeight: FontWeight.SemiBold,
            foreground: QuoteForeground);
        AppendLineBreaks(state.Output, 1, sourceObject);
    }

    private static void RenderLinkReferenceDefinition(LinkReferenceDefinition definition, RenderState state)
    {
        var body = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                CreateMetadataBodyTextBlock(
                    definition.Url,
                    state,
                    fontFamily: MonospaceFamily,
                    foreground: LinkForeground)
            }
        };

        if (!string.IsNullOrWhiteSpace(definition.Title))
        {
            body.Children.Add(CreateMetadataBodyTextBlock(definition.Title, state, foreground: QuoteForeground));
        }

        AddRichBlockControl(
            state,
            CreateMetadataCard(
                $"[{definition.Label}]",
                "Reference definition",
                body));
    }

    private static void RenderAbbreviationDefinition(Abbreviation abbreviation, RenderState state)
    {
        var body = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                CreateMetadataBodyTextBlock(abbreviation.Text.ToString(), state)
            }
        };

        AddRichBlockControl(
            state,
            CreateMetadataCard(
                abbreviation.Label ?? "Abbreviation",
                "Abbreviation definition",
                body));
    }

    private static void RenderLeafFallback(LeafBlock leafBlock, RenderState state)
    {
        AppendQuotePrefix(state);
        AppendListIndent(state, extraDepth: 0);

        if (leafBlock.Inline is not null)
        {
            RenderInlineContainer(leafBlock.Inline, state.Output, state, skipLeadingTaskInline: false);
            return;
        }

        var fallback = NormalizeMarkdownLineText(leafBlock.Lines.ToString());
        AppendStyledRun(state.Output, fallback, sourceObject: state.SourceObject);
    }

    private static void RenderInlineContainer(
        ContainerInline? container,
        InlineCollection output,
        RenderState state,
        bool skipLeadingTaskInline)
    {
        if (container is null)
        {
            return;
        }

        var inline = container.FirstChild;
        if (skipLeadingTaskInline && inline is TaskList)
        {
            inline = inline.NextSibling;
        }

        while (inline is not null)
        {
            RenderInline(inline, output, state);
            inline = inline.NextSibling;
        }
    }

    private static void RenderInline(Markdig.Syntax.Inlines.Inline inline, InlineCollection output, RenderState state)
    {
        var inlineState = state.WithSource(inline);
        if (TryRenderActiveInlineEditor(inline, output, inlineState))
        {
            return;
        }

        if (TryRenderInlineWithPlugins(inline, output, inlineState))
        {
            return;
        }

        switch (inline)
        {
            case EmojiInline emojiInline:
                AppendStyledRun(output, emojiInline.ToString(), sourceObject: inlineState.SourceObject);
                break;
            case LiteralInline literal:
                AppendStyledRun(output, literal.Content.ToString(), sourceObject: inlineState.SourceObject);
                break;
            case AbbreviationInline abbreviationInline:
                RenderAbbreviationInline(abbreviationInline, output, inlineState);
                break;
            case CodeInline codeInline:
                AppendStyledRun(
                    output,
                    codeInline.Content,
                    sourceObject: inlineState.SourceObject,
                    fontFamily: MonospaceFamily,
                    background: CodeBackground);
                break;
            case LineBreakInline:
                AppendLineBreaks(output, 1, inlineState.SourceObject);
                AppendQuotePrefix(inlineState);
                AppendListIndent(inlineState, extraDepth: 0);
                break;
            case EmphasisDelimiterInline emphasisDelimiter:
                RenderEmphasisDelimiterInline(emphasisDelimiter, output, inlineState);
                break;
            case EmphasisInline emphasis:
                RenderEmphasisInline(emphasis, output, inlineState);
                break;
            case LinkInline link:
                RenderLinkInline(link, output, inlineState);
                break;
            case AutolinkInline autolink:
                RenderAutolinkInline(autolink, output, inlineState);
                break;
            case FootnoteLink footnoteLink:
                RenderFootnoteLink(footnoteLink, output, inlineState);
                break;
            case SmartyPant smartyPant:
                AppendStyledRun(output, ResolveSmartyPantText(smartyPant), sourceObject: inlineState.SourceObject);
                break;
            case HtmlInline htmlInline:
                AppendStyledRun(output, htmlInline.Tag, sourceObject: inlineState.SourceObject, fontFamily: MonospaceFamily, foreground: QuoteForeground);
                break;
            case HtmlEntityInline htmlEntity:
                AppendStyledRun(output, htmlEntity.Transcoded.ToString(), sourceObject: inlineState.SourceObject);
                break;
            case TaskList taskList:
                AppendStyledRun(output, taskList.Checked ? "[x] " : "[ ] ", sourceObject: inlineState.SourceObject);
                break;
            case ContainerInline containerInline:
                RenderInlineContainer(containerInline, output, inlineState, skipLeadingTaskInline: false);
                break;
            default:
                AppendStyledRun(output, inline.ToString(), sourceObject: inlineState.SourceObject);
                break;
        }
    }

    private static bool TryRenderInlineWithPlugins(Markdig.Syntax.Inlines.Inline inline, InlineCollection output, RenderState state)
    {
        if (state.InlineRenderingPlugins.Count == 0)
        {
            return false;
        }

        foreach (var plugin in state.InlineRenderingPlugins)
        {
            if (!plugin.CanRender(inline))
            {
                continue;
            }

            var pluginContext = new MarkdownInlineRenderingPluginContext(
                inline,
                state.ParseResult,
                state.Options,
                output,
                state.QuoteDepth,
                state.ListDepth,
                addInline: inlineItem =>
                {
                    if (MarkdownRenderedElementMetadata.GetElementInfo(inlineItem) is null)
                    {
                        var kind = inlineItem switch
                        {
                            LineBreak => MarkdownRenderedElementKind.LineBreak,
                            InlineUIContainer => MarkdownRenderedElementKind.InlineControl,
                            _ => MarkdownRenderedElementKind.Text
                        };
                        AttachElementInfo(inlineItem, state.SourceObject, kind);
                    }

                    output.Add(inlineItem);
                },
                addInlineControl: (control, hitTestHandler) => output.Add(CreateInlineControlContainer(control, state.SourceObject, MarkdownRenderedElementKind.InlineControl, hitTestHandler: hitTestHandler)),
                resolveUri: url => ResolveUri(state.Options, url));

            if (plugin.TryRender(pluginContext))
            {
                return true;
            }
        }

        return false;
    }

    private static void RenderEmphasisInline(EmphasisInline emphasis, InlineCollection output, RenderState state)
    {
        if (emphasis.DelimiterChar == '~' && emphasis.DelimiterCount >= 2)
        {
            var strike = new Span
            {
                TextDecorations = Avalonia.Media.TextDecorations.Strikethrough
            };
            AttachElementInfo(strike, state.SourceObject, MarkdownRenderedElementKind.Text);

            RenderInlineContainer(emphasis, strike.Inlines, state, skipLeadingTaskInline: false);
            output.Add(strike);
            return;
        }

        if (emphasis.DelimiterChar == '=' && emphasis.DelimiterCount >= 2)
        {
            var marked = new Span
            {
                Background = MarkedTextBackground,
                FontWeight = FontWeight.Medium
            };
            AttachElementInfo(marked, state.SourceObject, MarkdownRenderedElementKind.Text);

            RenderInlineContainer(emphasis, marked.Inlines, state, skipLeadingTaskInline: false);
            output.Add(marked);
            return;
        }

        if (emphasis.DelimiterChar == '+' && emphasis.DelimiterCount >= 2)
        {
            var inserted = new Span
            {
                Foreground = InsertedTextForeground,
                TextDecorations = Avalonia.Media.TextDecorations.Underline
            };
            AttachElementInfo(inserted, state.SourceObject, MarkdownRenderedElementKind.Text);

            RenderInlineContainer(emphasis, inserted.Inlines, state, skipLeadingTaskInline: false);
            output.Add(inserted);
            return;
        }

        if (emphasis.DelimiterCount >= 2)
        {
            var bold = new Bold();
            AttachElementInfo(bold, state.SourceObject, MarkdownRenderedElementKind.Text);
            RenderInlineContainer(emphasis, bold.Inlines, state, skipLeadingTaskInline: false);
            output.Add(bold);
            return;
        }

        if (emphasis.DelimiterCount == 1)
        {
            var italic = new Italic();
            AttachElementInfo(italic, state.SourceObject, MarkdownRenderedElementKind.Text);
            RenderInlineContainer(emphasis, italic.Inlines, state, skipLeadingTaskInline: false);
            output.Add(italic);
            return;
        }

        RenderInlineContainer(emphasis, output, state, skipLeadingTaskInline: false);
    }

    private static void RenderEmphasisDelimiterInline(EmphasisDelimiterInline emphasisDelimiter, InlineCollection output, RenderState state)
    {
        var text = ExtractInlineText(emphasisDelimiter);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        switch (emphasisDelimiter.DelimiterChar)
        {
            case '^' when emphasisDelimiter.DelimiterCount >= 2:
                AppendInlineAdornment(
                    output,
                    text,
                    state,
                    BaselineAlignment.Superscript,
                    fontSize: Math.Max(state.Options.FontSize - 2, 10));
                return;
            case '~' when emphasisDelimiter.DelimiterCount == 1:
                AppendInlineAdornment(
                    output,
                    text,
                    state,
                    BaselineAlignment.Subscript,
                    fontSize: Math.Max(state.Options.FontSize - 2, 10));
                return;
            case '=' when emphasisDelimiter.DelimiterCount >= 2:
                AppendInlineAdornment(
                    output,
                    text,
                    state,
                    BaselineAlignment.Baseline,
                    fontWeight: FontWeight.Medium,
                    background: MarkedTextBackground,
                    padding: new Thickness(2, 0));
                return;
            case '+' when emphasisDelimiter.DelimiterCount >= 2:
                AppendInlineAdornment(
                    output,
                    text,
                    state,
                    BaselineAlignment.Baseline,
                    foreground: InsertedTextForeground,
                    textDecorations: Avalonia.Media.TextDecorations.Underline);
                return;
            default:
                RenderInlineContainer(emphasisDelimiter, output, state, skipLeadingTaskInline: false);
                return;
        }
    }

    private static void RenderAbbreviationInline(AbbreviationInline abbreviationInline, InlineCollection output, RenderState state)
    {
        var label = abbreviationInline.Abbreviation?.Label;
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        AppendInlineAdornment(
            output,
            label,
            state,
            BaselineAlignment.Baseline,
            fontWeight: FontWeight.Medium,
            textDecorations: Avalonia.Media.TextDecorations.Underline,
            toolTip: abbreviationInline.Abbreviation?.Text.ToString());
    }

    private static void RenderLinkInline(LinkInline link, InlineCollection output, RenderState state)
    {
        var url = link.GetDynamicUrl?.Invoke() ?? link.Url;

        if (link.IsImage)
        {
            RenderImageInline(link, output, state, url);
            return;
        }

        if (TryResolveUri(state.Options, url, out var navigateUri) && navigateUri is not null)
        {
            var linkText = ExtractInlineText(link);
            if (string.IsNullOrWhiteSpace(linkText))
            {
                linkText = url;
            }

            var hyperlinkSpan = CreateHyperlinkSpan(state);
            RenderInlineContainer(link, hyperlinkSpan.Inlines, state, skipLeadingTaskInline: false);
            if (hyperlinkSpan.Inlines.Count == 0)
            {
                hyperlinkSpan.Inlines.Add(AttachElementInfo(
                    new Run(linkText ?? string.Empty),
                    state.SourceObject,
                    MarkdownRenderedElementKind.Text));
            }

            output.Add(hyperlinkSpan);
            return;
        }

        var fallbackText = ExtractInlineText(link);
        var fallbackSpan = new Span
        {
            Foreground = LinkForeground,
            TextDecorations = Avalonia.Media.TextDecorations.Underline
        };
        AttachElementInfo(fallbackSpan, state.SourceObject, MarkdownRenderedElementKind.Text);

        RenderInlineContainer(link, fallbackSpan.Inlines, state, skipLeadingTaskInline: false);
        if (fallbackSpan.Inlines.Count == 0 && !string.IsNullOrWhiteSpace(url))
        {
            fallbackSpan.Inlines.Add(AttachElementInfo(new Run(url), state.SourceObject, MarkdownRenderedElementKind.Text));
        }

        output.Add(fallbackSpan);

        if (!string.IsNullOrWhiteSpace(url) &&
            !string.Equals(fallbackText.Trim(), url.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            AppendStyledRun(output, $" ({url})", sourceObject: state.SourceObject, foreground: QuoteForeground);
        }
    }

    private static void RenderImageInline(LinkInline link, InlineCollection output, RenderState state, string? url)
    {
        var altText = ExtractInlineText(link);
        if (string.IsNullOrWhiteSpace(altText))
        {
            altText = "Image";
        }

        var imageHost = CreateImageHost(altText, url, state.Options);
        output.Add(CreateInlineControlContainer(imageHost, state.SourceObject, MarkdownRenderedElementKind.InlineControl));

        if (!TryResolveUri(state.Options, url, out var imageUri) || imageUri is null || !imageUri.IsAbsoluteUri)
        {
            SetImageContent(
                imageHost,
                CreateImageStatusContent(altText, url, "Unable to resolve the image source.", state.Options));
            return;
        }

        LoadImageIntoHost(imageHost, imageUri, altText, url, state.Options);
    }

    private static void RenderAutolinkInline(AutolinkInline autolink, InlineCollection output, RenderState state)
    {
        var navigateUrl = autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url;
        if (TryResolveUri(state.Options, navigateUrl, out var navigateUri) && navigateUri is not null)
        {
            AppendHyperlinkInline(output, autolink.Url, state);
            return;
        }

        var span = new Span
        {
            Foreground = LinkForeground,
            TextDecorations = Avalonia.Media.TextDecorations.Underline
        };
        AttachElementInfo(span, state.SourceObject, MarkdownRenderedElementKind.Text);

        span.Inlines.Add(AttachElementInfo(new Run(autolink.Url), state.SourceObject, MarkdownRenderedElementKind.Text));
        output.Add(span);
    }

    private static void RenderFootnoteLink(FootnoteLink footnoteLink, InlineCollection output, RenderState state)
    {
        var text = footnoteLink.IsBackLink ? " ↩" : $"[{footnoteLink.Index}]";
        AppendStyledRun(
            output,
            text,
            sourceObject: state.SourceObject,
            fontWeight: FontWeight.SemiBold,
            fontSize: Math.Max(state.Options.FontSize - 2, 10),
            foreground: LinkForeground);
    }

    private static void AppendHyperlinkInline(InlineCollection output, string text, RenderState state)
    {
        var hyperlinkSpan = CreateHyperlinkSpan(state);
        hyperlinkSpan.Inlines.Add(AttachElementInfo(new Run(text), state.SourceObject, MarkdownRenderedElementKind.Text));
        output.Add(hyperlinkSpan);
    }

    private static Span CreateHyperlinkSpan(RenderState state)
    {
        var hyperlinkSpan = new Span
        {
            Foreground = LinkForeground,
            TextDecorations = Avalonia.Media.TextDecorations.Underline
        };
        return AttachElementInfo(hyperlinkSpan, state.SourceObject, MarkdownRenderedElementKind.Text);
    }

    private static string ExtractInlineText(ContainerInline container)
    {
        var builder = new StringBuilder();

        var inline = container.FirstChild;
        while (inline is not null)
        {
            AppendInlineText(builder, inline);
            inline = inline.NextSibling;
        }

        return builder.ToString();
    }

    private static void AppendInlineText(StringBuilder builder, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case EmojiInline emojiInline:
                builder.Append(emojiInline.ToString());
                break;
            case LiteralInline literal:
                builder.Append(literal.Content.ToString());
                break;
            case AbbreviationInline abbreviationInline when abbreviationInline.Abbreviation is { } abbreviation:
                builder.Append(abbreviation.Label);
                break;
            case CodeInline codeInline:
                builder.Append(codeInline.Content);
                break;
            case LineBreakInline:
                builder.Append(' ');
                break;
            case EmphasisDelimiterInline emphasisDelimiter:
                var nestedDelimiterChild = emphasisDelimiter.FirstChild;
                while (nestedDelimiterChild is not null)
                {
                    AppendInlineText(builder, nestedDelimiterChild);
                    nestedDelimiterChild = nestedDelimiterChild.NextSibling;
                }
                break;
            case LinkInline link:
                var linkText = ExtractInlineText(link);
                if (!string.IsNullOrWhiteSpace(linkText))
                {
                    builder.Append(linkText);
                }
                else if (!string.IsNullOrWhiteSpace(link.Url))
                {
                    builder.Append(link.Url);
                }
                break;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                break;
            case HtmlInline htmlInline:
                builder.Append(htmlInline.Tag);
                break;
            case HtmlEntityInline htmlEntity:
                builder.Append(htmlEntity.Transcoded.ToString());
                break;
            case SmartyPant smartyPant:
                builder.Append(ResolveSmartyPantText(smartyPant));
                break;
            case TaskList task:
                builder.Append(task.Checked ? "[x]" : "[ ]");
                break;
            case ContainerInline nested:
                var child = nested.FirstChild;
                while (child is not null)
                {
                    AppendInlineText(builder, child);
                    child = child.NextSibling;
                }
                break;
            default:
                builder.Append(inline.ToString());
                break;
        }
    }

    private static int TryParseOrderedStart(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1;
    }

    private static string ResolveSmartyPantText(SmartyPant smartyPant)
    {
        return SmartyPantMapping.TryGetValue(smartyPant.Type, out var text) ? text : smartyPant.ToString();
    }

    private static string NormalizeMarkdownLineText(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');
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

    private static double ResolveRichBlockWidth(RenderState state)
    {
        var listIndent = state.ListDepth * 24d;
        var availableWidth = state.Options.AvailableWidth > 0
            ? state.Options.AvailableWidth - listIndent
            : 640d;

        return Math.Clamp(availableWidth, 120d, 1400d);
    }

    private static double ResolveInlineEditorWidth(RenderState state)
    {
        var availableWidth = state.Options.AvailableWidth > 0
            ? state.Options.AvailableWidth - (state.ListDepth * 24d)
            : 480d;
        return Math.Clamp(availableWidth, 180d, 720d);
    }

    private static Uri? ResolveUri(MarkdownRenderContext context, string? url)
    {
        return MarkdownUriUtilities.ResolveUri(context.BaseUri, url);
    }

    private static SelectableTextBlock CreateRenderedTextBlock(
        RenderState parentState,
        MarkdownObject? sourceObject,
        Action<RenderState> render,
        double? fontSize = null,
        FontFamily? fontFamily = null,
        IBrush? foreground = null,
        TextAlignment textAlignment = TextAlignment.Left)
    {
        var inlines = new InlineCollection();
        var nestedState = new RenderState(
            parentState.ParseResult,
            parentState.Options,
            inlines,
            quoteDepth: 0,
            listDepth: 0,
            parentState.BlockRenderingPlugins,
            parentState.InlineRenderingPlugins,
            sourceObject);

        render(nestedState);
        TrimTrailingLineBreaks(inlines);

        return new SelectableTextBlock
        {
            FontFamily = fontFamily ?? parentState.Options.FontFamily,
            FontSize = fontSize ?? parentState.Options.FontSize,
            Foreground = foreground ?? parentState.Options.Foreground,
            TextWrapping = parentState.Options.TextWrapping == TextWrapping.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = textAlignment,
            Inlines = inlines
        };
    }

    private static void AppendInlineAdornment(
        InlineCollection output,
        string text,
        RenderState state,
        BaselineAlignment baselineAlignment,
        double? fontSize = null,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null,
        FontFamily? fontFamily = null,
        IBrush? foreground = null,
        IBrush? background = null,
        TextDecorationCollection? textDecorations = null,
        Thickness? padding = null,
        string? toolTip = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = fontSize ?? state.Options.FontSize,
            FontFamily = fontFamily ?? state.Options.FontFamily,
            Foreground = foreground ?? state.Options.Foreground,
            Background = Brushes.Transparent,
            TextWrapping = TextWrapping.NoWrap
        };

        if (fontWeight.HasValue)
        {
            textBlock.FontWeight = fontWeight.Value;
        }

        if (fontStyle.HasValue)
        {
            textBlock.FontStyle = fontStyle.Value;
        }

        if (textDecorations is not null)
        {
            textBlock.TextDecorations = textDecorations;
        }

        Control control = textBlock;
        if (background is not null || padding.HasValue)
        {
            control = new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(4),
                Padding = padding ?? new Thickness(0),
                Child = textBlock
            };
        }

        if (!string.IsNullOrWhiteSpace(toolTip))
        {
            ToolTip.SetTip(control, toolTip);
        }

        MarkdownRenderedElementMetadata.SetIsTextAdornment(control, true);
        output.Add(CreateInlineControlContainer(control, state.SourceObject, MarkdownRenderedElementKind.InlineControl, baselineAlignment));
    }

    private static Control CreateRichBlockHost(Control content, RenderState state)
    {
        var host = new MarkdownRichBlockHost(ResolveRichBlockWidth(state))
        {
            Margin = new Thickness(state.ListDepth * 24d, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = state.QuoteDepth > 0 ? CreateQuoteDecoratedBlock(content, state.QuoteDepth) : content
        };
        MarkdownRenderedElementMetadata.SetStretchesToDocumentWidth(host, true);
        return host;
    }

    private static void AddRichBlockControl(RenderState state, Control content, MarkdownVisualHitTestHandler? hitTestHandler = null)
    {
        state.Output.Add(CreateInlineControlContainer(
            CreateRichBlockHost(content, state),
            state.SourceObject,
            MarkdownRenderedElementKind.BlockControl,
            hitTestHandler: hitTestHandler));
    }

    private static InlineCollection CreatePlainCodeInlines(string code, MarkdownObject sourceObject)
    {
        var inlines = new InlineCollection();
        var lines = code.Split('\n', StringSplitOptions.None);
        for (var index = 0; index < lines.Length; index++)
        {
            inlines.Add(AttachElementInfo(new Run(lines[index]), sourceObject, MarkdownRenderedElementKind.Text));
            if (index < lines.Length - 1)
            {
                inlines.Add(AttachElementInfo(new LineBreak(), sourceObject, MarkdownRenderedElementKind.LineBreak));
            }
        }

        return inlines;
    }

    private static Control CreateQuoteDecoratedBlock(Control content, int quoteDepth)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 10
        };

        var markers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        for (var index = 0; index < quoteDepth; index++)
        {
            markers.Children.Add(new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                Background = QuoteAccentBrush
            });
        }

        grid.Children.Add(markers);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    private static Control CreateMetadataCard(string title, string subtitle, Control body)
    {
        var header = new Border
        {
            Background = CodeHeaderBackground,
            Padding = new Thickness(12, 8),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = subtitle,
                        Foreground = QuoteForeground
                    }
                }
            }
        };

        var content = new StackPanel
        {
            Spacing = 0,
            Children =
            {
                header,
                new Border
                {
                    Padding = new Thickness(12, 10),
                    Child = body
                }
            }
        };

        return new MarkdownRichBlockBorder
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content
        };
    }

    private static SelectableTextBlock CreateMetadataBodyTextBlock(
        string? text,
        RenderState state,
        FontFamily? fontFamily = null,
        IBrush? foreground = null)
    {
        return new SelectableTextBlock
        {
            Text = MarkdownSourceEditing.NormalizeBlockText(text),
            TextWrapping = state.Options.TextWrapping == TextWrapping.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
            FontFamily = fontFamily ?? state.Options.FontFamily,
            FontSize = state.Options.FontSize,
            Foreground = foreground ?? state.Options.Foreground
        };
    }

    private static void PopulateHighlightedCodeInlines(InlineCollection output, string code, string? languageHint, MarkdownObject? sourceObject)
    {
        var highlightedLines = HighlightCode(code, languageHint);
        for (var lineIndex = 0; lineIndex < highlightedLines.Count; lineIndex++)
        {
            foreach (var span in highlightedLines[lineIndex].Spans)
            {
                AppendStyledRun(
                    output,
                    span.Text,
                    sourceObject: sourceObject,
                    fontWeight: span.FontWeight,
                    fontFamily: MonospaceFamily,
                    foreground: span.Foreground);
            }

            if (lineIndex < highlightedLines.Count - 1)
            {
                output.Add(AttachElementInfo(new LineBreak(), sourceObject, MarkdownRenderedElementKind.LineBreak));
            }
        }
    }

    private static MarkdownVisualHitTestHandler CreateTableHitTestHandler(Control tableBorder, IReadOnlyList<TableCellHitTarget> tableHitTargets)
    {
        return request =>
        {
            foreach (var hitTarget in tableHitTargets)
            {
                if (!TryGetLocalPoint(request.Control, request.LocalPoint, hitTarget.Border, out var localPoint) ||
                    !new Rect(hitTarget.Border.Bounds.Size).Contains(localPoint))
                {
                    continue;
                }

                return new MarkdownVisualHitTestResult
                {
                    AstNode = hitTarget.AstNode,
                    SourceSpan = hitTarget.AstNode.SourceSpan,
                    LocalHighlightRects = ResolveLocalControlBounds(hitTarget.Border, request.Control)
                };
            }

            return new MarkdownVisualHitTestResult
            {
                LocalHighlightRects = ResolveLocalControlBounds(tableBorder, request.Control)
            };
        };
    }

    private static MarkdownVisualHitTestHandler CreateCodeBlockHitTestHandler(
        CodeBlock block,
        Control headerBorder,
        Control bodyBorder,
        SelectableTextBlock codeText,
        IReadOnlyList<CodeLineHitTarget> lineHitTargets)
    {
        return request =>
        {
            if (TryGetLocalPoint(request.Control, request.LocalPoint, headerBorder, out var headerPoint) &&
                new Rect(headerBorder.Bounds.Size).Contains(headerPoint))
            {
                return new MarkdownVisualHitTestResult
                {
                    LocalHighlightRects = ResolveLocalControlBounds(headerBorder, request.Control)
                };
            }

            if (TryGetLocalPoint(request.Control, request.LocalPoint, codeText, out var codePoint) &&
                new Rect(codeText.Bounds.Size).Contains(codePoint) &&
                codeText.TextLayout is { } textLayout)
            {
                foreach (var lineHitTarget in lineHitTargets)
                {
                    var lineRects = ResolveTextHighlightRects(textLayout, lineHitTarget.RenderedStart, lineHitTarget.RenderedLength);
                    if (!ContainsPoint(lineRects, codePoint))
                    {
                        continue;
                    }

                    return new MarkdownVisualHitTestResult
                    {
                        SourceSpan = lineHitTarget.SourceSpan ?? MarkdownSourceSpan.FromMarkdig(block.Span),
                        LocalHighlightRects = TranslateLocalRects(codeText, request.Control, lineRects)
                    };
                }
            }

            if (TryGetLocalPoint(request.Control, request.LocalPoint, bodyBorder, out var bodyPoint) &&
                new Rect(bodyBorder.Bounds.Size).Contains(bodyPoint))
            {
                return new MarkdownVisualHitTestResult
                {
                    LocalHighlightRects = ResolveLocalControlBounds(bodyBorder, request.Control)
                };
            }

            return null;
        };
    }

    private static List<CodeLineHitTarget> BuildCodeLineHitTargets(string code, CodeBlock block)
    {
        var normalizedLines = code.Split('\n', StringSplitOptions.None);
        var lineHitTargets = new List<CodeLineHitTarget>(normalizedLines.Length);
        var renderedStart = 0;

        for (var index = 0; index < normalizedLines.Length; index++)
        {
            MarkdownSourceSpan? sourceSpan = null;
            if (index < block.Lines.Count)
            {
                var line = block.Lines.Lines[index];
                if (line.Position >= 0 && line.Slice.Length > 0)
                {
                    sourceSpan = new MarkdownSourceSpan(line.Position, line.Slice.Length);
                }
            }

            var renderedLength = normalizedLines[index].Length;
            lineHitTargets.Add(new CodeLineHitTarget(renderedStart, renderedLength, sourceSpan));
            renderedStart += renderedLength;
            if (index < normalizedLines.Length - 1)
            {
                renderedStart++;
            }
        }

        return lineHitTargets;
    }

    private static IReadOnlyList<Rect> ResolveTextHighlightRects(TextLayout textLayout, int start, int length)
    {
        if (length > 0)
        {
            return textLayout.HitTestTextRange(start, length).ToArray();
        }

        if (start < 0)
        {
            return Array.Empty<Rect>();
        }

        return [textLayout.HitTestTextPosition(start)];
    }

    private static IReadOnlyList<Rect> ResolveLocalControlBounds(Control control, Control root)
    {
        if (control.TranslatePoint(default, root) is not { } topLeft)
        {
            return Array.Empty<Rect>();
        }

        return [new Rect(topLeft, control.Bounds.Size)];
    }

    private static IReadOnlyList<Rect> TranslateLocalRects(Control source, Control root, IReadOnlyList<Rect> rects)
    {
        var translatedRects = new List<Rect>(rects.Count);
        foreach (var rect in rects)
        {
            if (source.TranslatePoint(rect.TopLeft, root) is not { } topLeft)
            {
                continue;
            }

            translatedRects.Add(new Rect(topLeft, rect.Size));
        }

        return translatedRects;
    }

    private static bool TryGetLocalPoint(Control source, Point sourcePoint, Control target, out Point targetPoint)
    {
        if (source.TranslatePoint(sourcePoint, target) is { } translatedPoint)
        {
            targetPoint = translatedPoint;
            return true;
        }

        targetPoint = default;
        return false;
    }

    private static bool ContainsPoint(IReadOnlyList<Rect> rects, Point point)
    {
        for (var index = 0; index < rects.Count; index++)
        {
            if (rects[index].Inflate(1).Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    private static List<HighlightedCodeLine> HighlightCode(string code, string? languageHint)
    {
        var normalized = NormalizeLanguageHint(languageHint);
        var lines = code.Split('\n', StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return [new HighlightedCodeLine([])];
        }

        return ResolveCodeLanguageFamily(normalized) switch
        {
            CodeLanguageFamily.Json => HighlightJson(lines),
            CodeLanguageFamily.Markup => HighlightMarkup(lines),
            CodeLanguageFamily.Shell => HighlightShell(lines),
            CodeLanguageFamily.Sql => HighlightSql(lines),
            CodeLanguageFamily.CStyle => HighlightCStyle(lines),
            _ => HighlightPlainText(lines)
        };
    }

    private static List<HighlightedCodeLine> HighlightPlainText(IEnumerable<string> lines)
    {
        return lines
            .Select(static line => new HighlightedCodeLine([new HighlightedCodeSpan(line, null, null)]))
            .ToList();
    }

    private static List<HighlightedCodeLine> HighlightCStyle(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);
        var inBlockComment = false;

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (inBlockComment)
                {
                    var end = line.IndexOf("*/", index, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        index = line.Length;
                        continue;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 2)], CodeCommentForeground);
                    index = end + 2;
                    inBlockComment = false;
                    continue;
                }

                if (line[index] == '#' && line[..index].All(char.IsWhiteSpace))
                {
                    AddHighlightedSpan(spans, line[index..], CodeKeywordForeground, FontWeight.SemiBold);
                    break;
                }

                if (MatchesToken(line, index, "//"))
                {
                    AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                    break;
                }

                if (MatchesToken(line, index, "/*"))
                {
                    var end = line.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        inBlockComment = true;
                        break;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 2)], CodeCommentForeground);
                    index = end + 2;
                    continue;
                }

                if (line[index] == '@' && index + 1 < line.Length && line[index + 1] == '"')
                {
                    var end = FindVerbatimStringEnd(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeStringForeground);
                    index = end;
                    continue;
                }

                if (line[index] is '"' or '\'')
                {
                    var end = FindQuotedStringEnd(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeStringForeground);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = ConsumeNumber(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeNumberForeground);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(line[index]))
                {
                    var end = ConsumeIdentifier(line, index);
                    var word = line[index..end];
                    var (foreground, fontWeight) = ClassifyCodeIdentifier(word);
                    AddHighlightedSpan(spans, word, foreground, fontWeight);
                    index = end;
                    continue;
                }

                AddHighlightedSpan(
                    spans,
                    line[index].ToString(),
                    char.IsPunctuation(line[index]) && !char.IsWhiteSpace(line[index]) ? CodePunctuationForeground : null);
                index++;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static List<HighlightedCodeLine> HighlightJson(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (MatchesToken(line, index, "//"))
                {
                    AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                    break;
                }

                if (line[index] == '"')
                {
                    var end = FindQuotedStringEnd(line, index);
                    var lookahead = SkipWhitespace(line, end);
                    var brush = lookahead < line.Length && line[lookahead] == ':' ? CodePropertyForeground : CodeStringForeground;
                    AddHighlightedSpan(spans, line[index..end], brush);
                    index = end;
                    continue;
                }

                if (line[index] == '-' || char.IsDigit(line[index]))
                {
                    var end = ConsumeJsonNumber(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeNumberForeground);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(line[index]))
                {
                    var end = ConsumeIdentifier(line, index);
                    var word = line[index..end];
                    var isLiteral = LiteralWords.Contains(word);
                    AddHighlightedSpan(spans, word, isLiteral ? CodeKeywordForeground : null, isLiteral ? FontWeight.SemiBold : null);
                    index = end;
                    continue;
                }

                AddHighlightedSpan(
                    spans,
                    line[index].ToString(),
                    "{}[]:,".Contains(line[index], StringComparison.Ordinal) ? CodePunctuationForeground : null);
                index++;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static List<HighlightedCodeLine> HighlightMarkup(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);
        var inComment = false;

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (inComment)
                {
                    var end = line.IndexOf("-->", index, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        index = line.Length;
                        continue;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 3)], CodeCommentForeground);
                    index = end + 3;
                    inComment = false;
                    continue;
                }

                if (MatchesToken(line, index, "<!--"))
                {
                    var end = line.IndexOf("-->", index + 4, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        inComment = true;
                        break;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 3)], CodeCommentForeground);
                    index = end + 3;
                    continue;
                }

                if (line[index] == '<')
                {
                    AddHighlightedSpan(spans, "<", CodePunctuationForeground);
                    index++;

                    if (index < line.Length && line[index] == '/')
                    {
                        AddHighlightedSpan(spans, "/", CodePunctuationForeground);
                        index++;
                    }

                    var tagEnd = index;
                    while (tagEnd < line.Length && (char.IsLetterOrDigit(line[tagEnd]) || line[tagEnd] is ':' or '-' or '_'))
                    {
                        tagEnd++;
                    }

                    if (tagEnd > index)
                    {
                        AddHighlightedSpan(spans, line[index..tagEnd], CodeTagForeground, FontWeight.SemiBold);
                        index = tagEnd;
                    }

                    while (index < line.Length && line[index] != '>')
                    {
                        if (char.IsWhiteSpace(line[index]))
                        {
                            AddHighlightedSpan(spans, line[index].ToString());
                            index++;
                            continue;
                        }

                        if (line[index] == '/')
                        {
                            AddHighlightedSpan(spans, "/", CodePunctuationForeground);
                            index++;
                            continue;
                        }

                        var attributeStart = index;
                        while (index < line.Length && (char.IsLetterOrDigit(line[index]) || line[index] is ':' or '-' or '_'))
                        {
                            index++;
                        }

                        if (index > attributeStart)
                        {
                            AddHighlightedSpan(spans, line[attributeStart..index], CodeAttributeForeground);
                        }

                        var whitespaceStart = index;
                        index = SkipWhitespace(line, index);
                        if (index > whitespaceStart)
                        {
                            AddHighlightedSpan(spans, line[whitespaceStart..index]);
                        }

                        if (index < line.Length && line[index] == '=')
                        {
                            AddHighlightedSpan(spans, "=", CodePunctuationForeground);
                            index++;
                            var valueWhitespaceStart = index;
                            index = SkipWhitespace(line, index);
                            if (index > valueWhitespaceStart)
                            {
                                AddHighlightedSpan(spans, line[valueWhitespaceStart..index]);
                            }

                            if (index < line.Length && line[index] is '"' or '\'')
                            {
                                var valueEnd = FindQuotedStringEnd(line, index);
                                AddHighlightedSpan(spans, line[index..valueEnd], CodeStringForeground);
                                index = valueEnd;
                            }
                        }

                        if (index == attributeStart)
                        {
                            AddHighlightedSpan(spans, line[index].ToString(), CodePunctuationForeground);
                            index++;
                        }
                    }

                    if (index < line.Length && line[index] == '>')
                    {
                        AddHighlightedSpan(spans, ">", CodePunctuationForeground);
                        index++;
                    }

                    continue;
                }

                var nextTag = line.IndexOf('<', index);
                if (nextTag < 0)
                {
                    AddHighlightedSpan(spans, line[index..]);
                    break;
                }

                AddHighlightedSpan(spans, line[index..nextTag]);
                index = nextTag;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static List<HighlightedCodeLine> HighlightShell(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (line[index] == '#' && line[..index].All(static ch => char.IsWhiteSpace(ch)))
                {
                    AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                    break;
                }

                if (line[index] is '"' or '\'')
                {
                    var end = FindQuotedStringEnd(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeStringForeground);
                    index = end;
                    continue;
                }

                if (line[index] == '$')
                {
                    var end = index + 1;
                    while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '_' or '{' or '}'))
                    {
                        end++;
                    }

                    AddHighlightedSpan(spans, line[index..end], CodePropertyForeground);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = ConsumeNumber(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeNumberForeground);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(line[index]))
                {
                    var end = ConsumeIdentifier(line, index);
                    var word = line[index..end];
                    AddHighlightedSpan(spans, word, ShellKeywords.Contains(word) ? CodeKeywordForeground : null, ShellKeywords.Contains(word) ? FontWeight.SemiBold : null);
                    index = end;
                    continue;
                }

                AddHighlightedSpan(spans, line[index].ToString(), char.IsPunctuation(line[index]) && !char.IsWhiteSpace(line[index]) ? CodePunctuationForeground : null);
                index++;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static List<HighlightedCodeLine> HighlightSql(IReadOnlyList<string> lines)
    {
        var highlighted = new List<HighlightedCodeLine>(lines.Count);
        var inBlockComment = false;

        foreach (var line in lines)
        {
            var spans = new List<HighlightedCodeSpan>();
            var index = 0;

            while (index < line.Length)
            {
                if (inBlockComment)
                {
                    var end = line.IndexOf("*/", index, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        index = line.Length;
                        continue;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 2)], CodeCommentForeground);
                    index = end + 2;
                    inBlockComment = false;
                    continue;
                }

                if (MatchesToken(line, index, "--"))
                {
                    AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                    break;
                }

                if (MatchesToken(line, index, "/*"))
                {
                    var end = line.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        AddHighlightedSpan(spans, line[index..], CodeCommentForeground);
                        inBlockComment = true;
                        break;
                    }

                    AddHighlightedSpan(spans, line[index..(end + 2)], CodeCommentForeground);
                    index = end + 2;
                    continue;
                }

                if (line[index] == '\'')
                {
                    var end = FindQuotedStringEnd(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeStringForeground);
                    index = end;
                    continue;
                }

                if (char.IsDigit(line[index]))
                {
                    var end = ConsumeNumber(line, index);
                    AddHighlightedSpan(spans, line[index..end], CodeNumberForeground);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(line[index]))
                {
                    var end = ConsumeIdentifier(line, index);
                    var word = line[index..end];
                    AddHighlightedSpan(spans, word, SqlKeywords.Contains(word) ? CodeKeywordForeground : null, SqlKeywords.Contains(word) ? FontWeight.SemiBold : null);
                    index = end;
                    continue;
                }

                AddHighlightedSpan(spans, line[index].ToString(), char.IsPunctuation(line[index]) && !char.IsWhiteSpace(line[index]) ? CodePunctuationForeground : null);
                index++;
            }

            highlighted.Add(new HighlightedCodeLine(spans));
        }

        return highlighted;
    }

    private static (IBrush? Foreground, FontWeight? FontWeight) ClassifyCodeIdentifier(string word)
    {
        if (LiteralWords.Contains(word))
        {
            return (CodeKeywordForeground, FontWeight.SemiBold);
        }

        if (CommonCodeKeywords.Contains(word))
        {
            return (CodeKeywordForeground, FontWeight.SemiBold);
        }

        if (TypeLikeWords.Contains(word) || char.IsUpper(word[0]))
        {
            return (CodeTypeForeground, null);
        }

        return (null, null);
    }

    private static void AddHighlightedSpan(List<HighlightedCodeSpan> spans, string text, IBrush? foreground = null, FontWeight? fontWeight = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (spans.Count > 0)
        {
            var previous = spans[^1];
            if (ReferenceEquals(previous.Foreground, foreground) && previous.FontWeight == fontWeight)
            {
                spans[^1] = previous with { Text = previous.Text + text };
                return;
            }
        }

        spans.Add(new HighlightedCodeSpan(text, foreground, fontWeight));
    }

    private static bool MatchesToken(string line, int index, string token)
    {
        return index + token.Length <= line.Length &&
               string.Compare(line, index, token, 0, token.Length, StringComparison.Ordinal) == 0;
    }

    private static int SkipWhitespace(string line, int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsIdentifierStart(char ch)
    {
        return char.IsLetter(ch) || ch is '_' or '$';
    }

    private static int ConsumeIdentifier(string line, int index)
    {
        var end = index + 1;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '_' or '$'))
        {
            end++;
        }

        return end;
    }

    private static int ConsumeNumber(string line, int index)
    {
        var end = index + 1;
        while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] is '.' or '_' or 'x' or 'X'))
        {
            end++;
        }

        return end;
    }

    private static int ConsumeJsonNumber(string line, int index)
    {
        var end = index + 1;
        while (end < line.Length && (char.IsDigit(line[end]) || line[end] is '.' or 'e' or 'E' or '+' or '-'))
        {
            end++;
        }

        return end;
    }

    private static int FindQuotedStringEnd(string line, int startIndex)
    {
        var delimiter = line[startIndex];
        var end = startIndex + 1;

        while (end < line.Length)
        {
            if (line[end] == '\\')
            {
                end = Math.Min(end + 2, line.Length);
                continue;
            }

            end++;
            if (line[end - 1] == delimiter)
            {
                break;
            }
        }

        return end;
    }

    private static int FindVerbatimStringEnd(string line, int startIndex)
    {
        var end = startIndex + 2;
        while (end < line.Length)
        {
            if (line[end] == '"')
            {
                if (end + 1 < line.Length && line[end + 1] == '"')
                {
                    end += 2;
                    continue;
                }

                end++;
                break;
            }

            end++;
        }

        return end;
    }

    private static CodeLanguageFamily ResolveCodeLanguageFamily(string normalizedLanguageHint)
    {
        return normalizedLanguageHint switch
        {
            "text" or "plain" or "plaintext" or "md" or "markdown" => CodeLanguageFamily.PlainText,
            "json" or "jsonc" => CodeLanguageFamily.Json,
            "xml" or "xaml" or "axaml" or "html" or "htm" or "svg" => CodeLanguageFamily.Markup,
            "bash" or "sh" or "shell" or "zsh" or "pwsh" or "powershell" or "ps1" => CodeLanguageFamily.Shell,
            "sql" or "sqlite" or "postgres" or "postgresql" or "mysql" or "tsql" => CodeLanguageFamily.Sql,
            "cs" or "csharp" or "c#" or "js" or "javascript" or "ts" or "typescript" or "tsx" or "jsx" or
            "java" or "go" or "rust" or "rs" or "cpp" or "c++" or "c" or "h" or "hpp" or "swift" or "kotlin" =>
                CodeLanguageFamily.CStyle,
            _ => CodeLanguageFamily.CStyle
        };
    }

    private static string NormalizeLanguageHint(string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(languageHint))
        {
            return "text";
        }

        var trimmed = languageHint.Trim();
        var separatorIndex = trimmed.IndexOfAny([' ', '\t', ',', ';', '{', '(']);
        var normalized = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        return normalized.Trim().Trim('.').ToLowerInvariant();
    }

    private static string FormatLanguageLabel(string? languageHint)
    {
        return NormalizeLanguageHint(languageHint) switch
        {
            "axaml" => "AXAML",
            "cs" or "csharp" or "c#" => "C#",
            "cpp" or "c++" => "C++",
            "html" => "HTML",
            "js" or "javascript" => "JavaScript",
            "json" or "jsonc" => "JSON",
            "md" or "markdown" => "Markdown",
            "ps1" or "powershell" or "pwsh" => "PowerShell",
            "rs" or "rust" => "Rust",
            "sh" or "bash" or "shell" => "Shell",
            "sql" => "SQL",
            "ts" or "typescript" => "TypeScript",
            "xaml" => "XAML",
            "xml" => "XML",
            "text" or "plain" or "plaintext" => "Plain text",
            var other when !string.IsNullOrWhiteSpace(other) => other.ToUpperInvariant(),
            _ => "Plain text"
        };
    }

    private static bool TryResolveUri(MarkdownRenderContext context, string? url, out Uri? resolvedUri)
    {
        return MarkdownUriUtilities.TryResolveUri(context.BaseUri, url, out resolvedUri);
    }

    private static void AppendQuotePrefix(RenderState state)
    {
        if (state.QuoteDepth <= 0)
        {
            return;
        }

        for (var index = 0; index < state.QuoteDepth; index++)
        {
            AppendStyledRun(state.Output, "│ ", sourceObject: state.SourceObject, fontWeight: FontWeight.SemiBold, foreground: QuoteForeground);
        }
    }

    private static void AppendListIndent(RenderState state, int extraDepth)
    {
        var depth = state.ListDepth + extraDepth;
        if (depth <= 0)
        {
            return;
        }

        AppendStyledRun(state.Output, new string(' ', depth * 2), sourceObject: state.SourceObject);
    }

    private static void AppendLineBreaks(InlineCollection output, int count, MarkdownObject? sourceObject = null)
    {
        for (var index = 0; index < count; index++)
        {
            output.Add(AttachElementInfo(new LineBreak(), sourceObject, MarkdownRenderedElementKind.LineBreak));
        }
    }

    private static void AppendStyledRun(
        InlineCollection output,
        string? text,
        MarkdownObject? sourceObject = null,
        FontWeight? fontWeight = null,
        FontStyle? fontStyle = null,
        double? fontSize = null,
        FontFamily? fontFamily = null,
        IBrush? foreground = null,
        IBrush? background = null,
        TextDecorationCollection? textDecorations = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var run = new Run(text);

        if (fontWeight.HasValue)
        {
            run.FontWeight = fontWeight.Value;
        }

        if (fontStyle.HasValue)
        {
            run.FontStyle = fontStyle.Value;
        }

        if (fontSize.HasValue)
        {
            run.FontSize = fontSize.Value;
        }

        if (fontFamily is not null)
        {
            run.FontFamily = fontFamily;
        }

        if (foreground is not null)
        {
            run.Foreground = foreground;
        }

        if (background is not null)
        {
            run.Background = background;
        }

        if (textDecorations is not null)
        {
            run.TextDecorations = textDecorations;
        }

        output.Add(AttachElementInfo(run, sourceObject, MarkdownRenderedElementKind.Text));
    }

    private static T AttachElementInfo<T>(T element, MarkdownObject? sourceObject, MarkdownRenderedElementKind kind)
        where T : AvaloniaObject
    {
        var elementInfo = MarkdownRenderedElementMetadata.CreateElementInfo(sourceObject, kind);
        if (elementInfo is not null)
        {
            MarkdownRenderedElementMetadata.SetElementInfo(element, elementInfo);
        }

        return element;
    }

    private static InlineUIContainer CreateInlineControlContainer(
        Control control,
        MarkdownObject? sourceObject,
        MarkdownRenderedElementKind kind,
        BaselineAlignment baselineAlignment = BaselineAlignment.Center,
        MarkdownVisualHitTestHandler? hitTestHandler = null)
    {
        AttachElementInfo(control, sourceObject, kind);
        if (hitTestHandler is not null)
        {
            MarkdownRenderedElementMetadata.SetVisualHitTestHandler(control, hitTestHandler);
        }

        return AttachElementInfo(
            new InlineUIContainer(control)
            {
                BaselineAlignment = baselineAlignment
            },
            sourceObject,
            kind);
    }

    private static void TrimTrailingLineBreaks(InlineCollection inlines)
    {
        while (inlines.Count > 0 && inlines[inlines.Count - 1] is LineBreak)
        {
            inlines.RemoveAt(inlines.Count - 1);
        }
    }

    private static Border CreateImageHost(string altText, string? originalUrl, MarkdownRenderContext context)
    {
        return new Border
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = ResolveMaxImageWidth(context),
            Child = CreateImageStatusContent(altText, originalUrl, "Loading image…", context)
        };
    }

    private static Control CreateImageStatusContent(string altText, string? originalUrl, string status, MarkdownRenderContext context)
    {
        var panel = new StackPanel
        {
            Spacing = 4
        };

        panel.Children.Add(new TextBlock
        {
            Text = altText,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = context.Foreground
        });
        panel.Children.Add(new TextBlock
        {
            Text = status,
            Foreground = QuoteForeground,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(originalUrl) &&
            !originalUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            panel.Children.Add(new TextBlock
            {
                Text = originalUrl,
                FontFamily = MonospaceFamily,
                FontSize = Math.Max(context.FontSize - 1, 11),
                Foreground = QuoteForeground,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private static Control CreateLoadedImageContent(Bitmap bitmap, string altText, MarkdownRenderContext context)
    {
        var panel = new StackPanel
        {
            Spacing = 6
        };

        panel.Children.Add(new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = ResolveMaxImageWidth(context),
            MaxHeight = DefaultMaxImageHeight
        });

        if (!string.IsNullOrWhiteSpace(altText) &&
            !string.Equals(altText, "Image", StringComparison.Ordinal))
        {
            panel.Children.Add(new TextBlock
            {
                Text = altText,
                FontSize = Math.Max(context.FontSize - 1, 11),
                Foreground = QuoteForeground,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private static double ResolveMaxImageWidth(MarkdownRenderContext context)
    {
        return context.AvailableWidth > 0
            ? Math.Clamp(context.AvailableWidth - 24, 120, 680)
            : DefaultMaxImageWidth;
    }

    private static void LoadImageIntoHost(Border host, Uri imageUri, string altText, string? originalUrl, MarkdownRenderContext context)
    {
        if (imageUri.IsFile)
        {
            LoadFileImage(host, imageUri, altText, originalUrl, context);
            return;
        }

        if (string.Equals(imageUri.Scheme, "data", StringComparison.OrdinalIgnoreCase))
        {
            LoadDataUriImage(host, imageUri, altText, originalUrl, context);
            return;
        }

        if (string.Equals(imageUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(imageUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            _ = LoadRemoteImageAsync(host, imageUri, altText, originalUrl, context);
            return;
        }

        SetImageContent(host, CreateImageStatusContent(altText, originalUrl, $"Unsupported image source: {imageUri.Scheme}", context));
    }

    private static void LoadFileImage(Border host, Uri imageUri, string altText, string? originalUrl, MarkdownRenderContext context)
    {
        if (!File.Exists(imageUri.LocalPath))
        {
            SetImageContent(host, CreateImageStatusContent(altText, originalUrl, "Image file not found.", context));
            return;
        }

        try
        {
            var bitmap = new Bitmap(imageUri.LocalPath);
            ApplyLoadedImage(host, bitmap, altText, context);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            SetImageContent(host, CreateImageStatusContent(altText, originalUrl, $"Unable to load image ({ex.Message}).", context));
        }
    }

    private static void LoadDataUriImage(Border host, Uri imageUri, string altText, string? originalUrl, MarkdownRenderContext context)
    {
        var uriText = imageUri.OriginalString;
        var commaIndex = uriText.IndexOf(',');
        if (commaIndex < 0)
        {
            SetImageContent(host, CreateImageStatusContent(altText, originalUrl, "Invalid data URI image.", context));
            return;
        }

        var metadata = uriText[..commaIndex];
        if (!metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            SetImageContent(host, CreateImageStatusContent(altText, originalUrl, "Only base64 data URI images are supported.", context));
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(uriText[(commaIndex + 1)..]);
            using var memoryStream = new MemoryStream(bytes);
            var bitmap = new Bitmap(memoryStream);
            ApplyLoadedImage(host, bitmap, altText, context);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            SetImageContent(host, CreateImageStatusContent(altText, originalUrl, $"Unable to decode image ({ex.Message}).", context));
        }
    }

    private static async Task LoadRemoteImageAsync(Border host, Uri imageUri, string altText, string? originalUrl, MarkdownRenderContext context)
    {
        try
        {
            using var response = await HttpClient.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
            memoryStream.Position = 0;

            var bitmap = new Bitmap(memoryStream);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyLoadedImage(host, bitmap, altText, context));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!context.IsCurrentRender(context.RenderGeneration))
                {
                    return;
                }

                SetImageContent(host, CreateImageStatusContent(altText, originalUrl, $"Unable to load image ({ex.Message}).", context));
            });
        }
    }

    private static void ApplyLoadedImage(Border host, Bitmap bitmap, string altText, MarkdownRenderContext context)
    {
        if (!context.IsCurrentRender(context.RenderGeneration))
        {
            bitmap.Dispose();
            return;
        }

        context.ResourceTracker.Track(bitmap);
        SetImageContent(host, CreateLoadedImageContent(bitmap, altText, context));
    }

    private static void SetImageContent(Border host, Control content)
    {
        host.Child = content;
    }

    private sealed record HighlightedCodeLine(List<HighlightedCodeSpan> Spans);

    private sealed record HighlightedCodeSpan(string Text, IBrush? Foreground, FontWeight? FontWeight);

    private enum CodeLanguageFamily
    {
        PlainText,
        CStyle,
        Json,
        Markup,
        Shell,
        Sql
    }

    private sealed class RenderState(
        MarkdownParseResult parseResult,
        MarkdownRenderContext options,
        InlineCollection output,
        int quoteDepth,
        int listDepth,
        IReadOnlyList<IMarkdownBlockRenderingPlugin> blockRenderingPlugins,
        IReadOnlyList<IMarkdownInlineRenderingPlugin> inlineRenderingPlugins,
        MarkdownObject? sourceObject)
    {
        public MarkdownParseResult ParseResult { get; } = parseResult;

        public MarkdownRenderContext Options { get; } = options;

        public InlineCollection Output { get; } = output;

        public int QuoteDepth { get; } = quoteDepth;

        public int ListDepth { get; } = listDepth;

        public IReadOnlyList<IMarkdownBlockRenderingPlugin> BlockRenderingPlugins { get; } = blockRenderingPlugins;

        public IReadOnlyList<IMarkdownInlineRenderingPlugin> InlineRenderingPlugins { get; } = inlineRenderingPlugins;

        public MarkdownObject? SourceObject { get; } = sourceObject;

        public RenderState WithQuoteDepth(int value) => new(
            ParseResult,
            Options,
            Output,
            value,
            ListDepth,
            BlockRenderingPlugins,
            InlineRenderingPlugins,
            SourceObject);

        public RenderState WithListDepth(int value) => new(
            ParseResult,
            Options,
            Output,
            QuoteDepth,
            value,
            BlockRenderingPlugins,
            InlineRenderingPlugins,
            SourceObject);

        public RenderState WithSource(MarkdownObject? value) => new(
            ParseResult,
            Options,
            Output,
            QuoteDepth,
            ListDepth,
            BlockRenderingPlugins,
            InlineRenderingPlugins,
            value);
    }

    private sealed class TableRowData(bool isHeader, List<TableCellData> cells)
    {
        public bool IsHeader { get; } = isHeader;

        public List<TableCellData> Cells { get; } = cells;
    }

    private sealed class TableCellData(TableCell? cell, string plainText)
    {
        public static TableCellData Empty { get; } = new(null, string.Empty);

        public TableCell? Cell { get; } = cell;

        public string PlainText { get; } = plainText;
    }

    private sealed record TableCellHitTarget(Border Border, MarkdownAstNodeInfo AstNode);

    private sealed record CodeLineHitTarget(int RenderedStart, int RenderedLength, MarkdownSourceSpan? SourceSpan);

    private sealed class MarkdownRichBlockHost(double preferredWidth) : Decorator
    {
        private readonly double _preferredWidth = preferredWidth;

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = ResolveWidth(availableSize.Width);
            if (Child is null)
            {
                return new Size(width, 0);
            }

            Child.Measure(new Size(width, availableSize.Height));
            return new Size(width, Child.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Child?.Arrange(new Rect(finalSize));
            return finalSize;
        }

        private double ResolveWidth(double availableWidth)
        {
            if (!double.IsInfinity(availableWidth) && availableWidth > 0)
            {
                return Math.Min(availableWidth, _preferredWidth);
            }

            return _preferredWidth;
        }
    }
}
