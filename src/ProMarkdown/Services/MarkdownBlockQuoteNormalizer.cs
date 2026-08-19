using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Syntax;

namespace ProMarkdown.Services;

internal static class MarkdownBlockQuoteNormalizer
{
    private const string QuotePrefix = "│ ";
    private const double RailWidth = 3;

    public static void Normalize(
        InlineCollection inlines,
        MarkdownParseResult parseResult,
        MarkdownRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        for (var index = 0; index < inlines.Count; index++)
        {
            if (!IsLineStart(inlines, index) || !IsQuotePrefix(inlines[index], parseResult))
                continue;

            var quoteLines = ExtractQuoteLines(inlines, index, parseResult, out var trailingBreaks);
            inlines.Insert(index, new InlineUIContainer(CreateQuoteGrid(quoteLines, context)));

            var insertIndex = index + 1;
            for (var breakIndex = 0; breakIndex < trailingBreaks.Count; breakIndex++)
                inlines.Insert(insertIndex++, trailingBreaks[breakIndex]);

            index = insertIndex - 1;
        }
    }

    private static List<QuoteLine> ExtractQuoteLines(
        InlineCollection inlines,
        int index,
        MarkdownParseResult parseResult,
        out List<LineBreak> trailingBreaks)
    {
        var lines = new List<QuoteLine>();
        trailingBreaks = [];

        while (index < inlines.Count && IsQuotePrefix(inlines[index], parseResult))
        {
            var depth = 0;
            Block? semanticBlock = null;
            while (index < inlines.Count && IsQuotePrefix(inlines[index], parseResult))
            {
                semanticBlock ??= ResolveSemanticBlock(
                    MarkdownRenderedElementMetadata.GetElementInfo(inlines[index])?.AstNode.Node,
                    parseResult);
                inlines.RemoveAt(index);
                depth++;
            }

            var content = new InlineCollection();
            while (index < inlines.Count && inlines[index] is not LineBreak)
            {
                var inline = inlines[index];
                inlines.RemoveAt(index);
                content.Add(inline);
            }

            lines.Add(new QuoteLine(depth, content, semanticBlock));

            var lineBreaks = new List<LineBreak>();
            while (index < inlines.Count && inlines[index] is LineBreak lineBreak)
            {
                inlines.RemoveAt(index);
                lineBreaks.Add(lineBreak);
            }

            if (lineBreaks.Count == 1 && index < inlines.Count && IsQuotePrefix(inlines[index], parseResult))
                continue;

            trailingBreaks = lineBreaks;
            break;
        }

        return lines;
    }

    private static Grid CreateQuoteGrid(
        IReadOnlyList<QuoteLine> lines,
        MarkdownRenderContext context)
    {
        var maxDepth = 1;
        for (var index = 0; index < lines.Count; index++)
            maxDepth = Math.Max(maxDepth, lines[index].Depth);

        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        MarkdownRenderedElementMetadata.SetStretchesToDocumentWidth(grid, true);

        var spacing = Math.Max(8, context.FontSize * 0.7);
        for (var depth = 0; depth < maxDepth; depth++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(RailWidth)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(spacing)));
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var semanticFlowGroups = new Dictionary<Block, object>(ReferenceEqualityComparer.Instance);
        var pendingLineBreaks = 0;
        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (row > 0)
                pendingLineBreaks += GetLineBreaksBefore(lines, row);

            object? flowGroup = null;
            if (line.SemanticBlock is { } semanticBlock)
            {
                if (!semanticFlowGroups.TryGetValue(semanticBlock, out flowGroup))
                {
                    flowGroup = new object();
                    semanticFlowGroups.Add(semanticBlock, flowGroup);
                }
            }

            MarkdownSelectionNormalizer.IsolateTextSegments(
                line.Content,
                context,
                flowGroup: flowGroup);

            var content = new TextBlock
            {
                FontFamily = context.FontFamily,
                FontSize = context.FontSize,
                Foreground = (context.ThemePalette ?? MarkdownThemePalette.Resolve(context.Foreground)).Foreground,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Inlines = line.Content,
                Margin = new Thickness(0, GetTopSpacing(lines, row, context.FontSize), 0, 0),
                TextWrapping = context.TextWrapping
            };
            Grid.SetColumn(content, line.Depth * 2);
            Grid.SetColumnSpan(content, grid.ColumnDefinitions.Count - line.Depth * 2);
            Grid.SetRow(content, row);
            grid.Children.Add(content);

            if (MarkdownDocumentSelection.RegisterLineBreaksBeforeFirstSegment(content, pendingLineBreaks))
                pendingLineBreaks = 0;
        }

        var palette = context.ThemePalette ?? MarkdownThemePalette.Resolve(context.Foreground);
        AddRails(grid, lines, maxDepth, palette.QuoteBorder);
        return grid;
    }

    private static void AddRails(
        Grid grid,
        IReadOnlyList<QuoteLine> lines,
        int maxDepth,
        IBrush? fallbackBrush)
    {
        for (var depth = 1; depth <= maxDepth; depth++)
        {
            var row = 0;
            while (row < lines.Count)
            {
                while (row < lines.Count && lines[row].Depth < depth)
                    row++;

                if (row >= lines.Count)
                    break;

                var firstRow = row;
                while (row < lines.Count && lines[row].Depth >= depth)
                    row++;

                var rail = new Border
                {
                    Background = fallbackBrush,
                    CornerRadius = new CornerRadius(RailWidth / 2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                Grid.SetColumn(rail, (depth - 1) * 2);
                Grid.SetRow(rail, firstRow);
                Grid.SetRowSpan(rail, row - firstRow);
                grid.Children.Add(rail);
            }
        }
    }

    private static double GetTopSpacing(
        IReadOnlyList<QuoteLine> lines,
        int row,
        double fontSize)
    {
        if (row == 0)
            return 0;

        var line = lines[row];
        var previous = lines[row - 1];
        if (AreSameSemanticBlock(line, previous))
            return 0;

        if (line.SemanticBlock is ListItemBlock &&
            previous.SemanticBlock is ListItemBlock &&
            line.Depth == previous.Depth)
            return Math.Max(2, fontSize * 0.25);

        return Math.Max(6, fontSize * 0.75);
    }

    private static int GetLineBreaksBefore(IReadOnlyList<QuoteLine> lines, int row)
    {
        var line = lines[row];
        var previous = lines[row - 1];
        return AreSameSemanticBlock(line, previous) ||
               line.SemanticBlock is ListItemBlock &&
               previous.SemanticBlock is ListItemBlock &&
               line.Depth == previous.Depth
            ? 1
            : 2;
    }

    private static bool AreSameSemanticBlock(QuoteLine line, QuoteLine previous) =>
        line.SemanticBlock is not null &&
        ReferenceEquals(line.SemanticBlock, previous.SemanticBlock);

    private static Block? ResolveSemanticBlock(
        MarkdownObject? node,
        MarkdownParseResult parseResult)
    {
        Block? closestBlock = null;
        for (var current = node; current is not null;)
        {
            if (current is ListItemBlock listItem)
                return listItem;
            if (closestBlock is null && current is Block block)
                closestBlock = block;
            if (!parseResult.TryGetParent(current, out current))
                break;
        }

        return closestBlock;
    }

    private static bool IsLineStart(InlineCollection inlines, int index) =>
        index == 0 || inlines[index - 1] is LineBreak;

    private static bool IsQuotePrefix(Inline inline, MarkdownParseResult parseResult) =>
        inline is Run { Text: QuotePrefix, FontWeight: var fontWeight } run &&
        fontWeight == FontWeight.DemiBold &&
        IsWithinQuote(
            MarkdownRenderedElementMetadata.GetElementInfo(run)?.AstNode.Node,
            parseResult);

    private static bool IsWithinQuote(
        MarkdownObject? node,
        MarkdownParseResult parseResult)
    {
        for (var current = node; current is not null;)
        {
            if (current is QuoteBlock)
                return true;
            if (!parseResult.TryGetParent(current, out current))
                break;
        }

        return false;
    }

    private sealed record QuoteLine(
        int Depth,
        InlineCollection Content,
        Block? SemanticBlock);
}
