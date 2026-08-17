using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using Markdig.Syntax;

namespace CodexGui.Markdown.Services;

internal static class MarkdownSelectionNormalizer
{
    private const double FontSizeTolerance = 0.01;

    public static void IsolateTextSegments(
        InlineCollection inlines,
        MarkdownRenderContext context,
        MarkdownParseResult? parseResult = null,
        int leadingLineBreaks = 0,
        object? flowGroup = null)
    {
        MoveInlineTextAdornmentsIntoFlow(inlines, context.FontSize);
        LiftNestedInlineControls(inlines);
        IsolateScaledText(inlines, context.FontSize, context.AvailableWidth);

        var fallbackFlowGroup = flowGroup ?? new object();
        Dictionary<Block, object>? semanticFlowGroups = parseResult is null
            ? null
            : new Dictionary<Block, object>(ReferenceEqualityComparer.Instance);
        var pendingLineBreaks = leadingLineBreaks;
        for (var index = 0; index < inlines.Count; index++)
        {
            if (inlines[index] is LineBreak)
            {
                if (flowGroup is null)
                    fallbackFlowGroup = new object();
                pendingLineBreaks++;
                continue;
            }

            if (inlines[index] is InlineUIContainer { Child: SelectableTextBlock existingText })
            {
                var existingFlowGroup = ResolveFlowGroup(
                    existingText.Inlines,
                    inlines[index],
                    parseResult,
                    semanticFlowGroups,
                    flowGroup ?? fallbackFlowGroup);
                MarkdownDocumentSelection.RegisterSegment(existingText, existingFlowGroup, pendingLineBreaks);
                pendingLineBreaks = 0;
                continue;
            }

            if (inlines[index] is InlineUIContainer { Child: { } child })
            {
                var childFlowGroup = ResolveFlowGroup(
                    null,
                    inlines[index],
                    parseResult,
                    semanticFlowGroups,
                    flowGroup ?? fallbackFlowGroup);
                MarkdownDocumentSelection.RegisterFlowGroupForUnassignedSegments(
                    child,
                    childFlowGroup);
                if (MarkdownDocumentSelection.RegisterLineBreaksBeforeFirstSegment(child, pendingLineBreaks))
                    pendingLineBreaks = 0;

                continue;
            }

            var segmentInlines = new InlineCollection();
            while (index < inlines.Count && inlines[index] is not (LineBreak or InlineUIContainer))
            {
                var inline = inlines[index];
                inlines.RemoveAt(index);
                segmentInlines.Add(inline);
            }

            var selectableText = new MarkdownWrappingSelectableTextBlock
            {
                FontFamily = context.FontFamily,
                FontSize = context.FontSize,
                Foreground = context.Foreground,
                Inlines = segmentInlines,
                TextWrapping = TextWrapping.Wrap
            };
            selectableText.SetToolTipRanges(CreateToolTipRanges(segmentInlines));
            selectableText.SetRenderMap(MarkdownRenderMapBuilder.Build(segmentInlines));
            var segmentFlowGroup = ResolveFlowGroup(
                segmentInlines,
                null,
                parseResult,
                semanticFlowGroups,
                flowGroup ?? fallbackFlowGroup);
            MarkdownDocumentSelection.RegisterSegment(selectableText, segmentFlowGroup, pendingLineBreaks);
            pendingLineBreaks = 0;
            SetInitialWrappingWidth(selectableText, context.AvailableWidth);
            inlines.Insert(index, new InlineUIContainer(selectableText));
        }
    }

    private static object ResolveFlowGroup(
        InlineCollection? inlines,
        AvaloniaObject? metadataSource,
        MarkdownParseResult? parseResult,
        Dictionary<Block, object>? semanticFlowGroups,
        object fallbackFlowGroup)
    {
        if (parseResult is null || semanticFlowGroups is null)
            return fallbackFlowGroup;

        var node = metadataSource is null
            ? null
            : MarkdownRenderedElementMetadata.GetElementInfo(metadataSource)?.AstNode.Node;
        node ??= FindFirstMarkdownNode(inlines);
        var block = ResolveSemanticBlock(node, parseResult);
        if (block is null)
            return fallbackFlowGroup;

        if (semanticFlowGroups.TryGetValue(block, out var flowGroup))
            return flowGroup;

        flowGroup = new object();
        semanticFlowGroups.Add(block, flowGroup);
        return flowGroup;
    }

    private static MarkdownObject? FindFirstMarkdownNode(InlineCollection? inlines)
    {
        if (inlines is null)
            return null;

        foreach (var inline in inlines)
        {
            if (MarkdownRenderedElementMetadata.GetElementInfo(inline)?.AstNode.Node is { } node)
                return node;
            if (inline is Span span && FindFirstMarkdownNode(span.Inlines) is { } nestedNode)
                return nestedNode;
        }

        return null;
    }

    private static Block? ResolveSemanticBlock(MarkdownObject? node, MarkdownParseResult parseResult)
    {
        for (var current = node; current is not null;)
        {
            if (current is Block block)
                return block;
            if (!parseResult.TryGetParent(current, out current))
                break;
        }

        return null;
    }

    private static void MoveInlineTextAdornmentsIntoFlow(InlineCollection inlines, double baseFontSize)
    {
        for (var index = 0; index < inlines.Count; index++)
        {
            if (inlines[index] is Span nestedSpan)
            {
                MoveInlineTextAdornmentsIntoFlow(nestedSpan.Inlines, baseFontSize);
                continue;
            }

            if (inlines[index] is not InlineUIContainer { Child: { } child } container ||
                !TryGetTextAdornment(child, out var textBlock, out var background))
            {
                continue;
            }

            var toolTip = ToolTip.GetTip(child) ?? ToolTip.GetTip(textBlock);
            Span replacement = toolTip is null ? new Span() : new MarkdownToolTipSpan(toolTip);
            replacement.Background = background ?? textBlock.Background;
            replacement.FontFamily = textBlock.FontFamily;
            replacement.FontFeatures = textBlock.FontFeatures;
            replacement.FontStretch = textBlock.FontStretch;
            replacement.FontStyle = textBlock.FontStyle;
            replacement.FontWeight = textBlock.FontWeight;
            replacement.Foreground = textBlock.Foreground;
            replacement.LetterSpacing = textBlock.LetterSpacing;
            replacement.TextDecorations = textBlock.TextDecorations;
            replacement.BaselineAlignment = container.BaselineAlignment;
            if (Math.Abs(textBlock.FontSize - baseFontSize) >= FontSizeTolerance)
                replacement.FontSize = textBlock.FontSize;

            var elementInfo = MarkdownRenderedElementMetadata.GetElementInfo(container) ??
                              MarkdownRenderedElementMetadata.GetElementInfo(child) ??
                              MarkdownRenderedElementMetadata.GetElementInfo(textBlock);
            if (elementInfo is not null)
                MarkdownRenderedElementMetadata.SetElementInfo(replacement, elementInfo);

            if (textBlock.Inlines is { Count: > 0 } adornmentInlines)
                replacement.Inlines.AddRange(MoveInlines(adornmentInlines));
            else if (!string.IsNullOrEmpty(textBlock.Text))
            {
                var run = new Run(textBlock.Text);
                if (elementInfo is not null)
                    MarkdownRenderedElementMetadata.SetElementInfo(run, elementInfo);
                replacement.Inlines.Add(run);
            }

            inlines[index] = replacement;
        }
    }

    private static bool TryGetTextAdornment(
        Control control,
        out TextBlock textBlock,
        out IBrush? background)
    {
        if (!MarkdownRenderedElementMetadata.GetIsTextAdornment(control))
        {
            textBlock = null!;
            background = null;
            return false;
        }

        switch (control)
        {
            case TextBlock directText when directText is not SelectableTextBlock:
                textBlock = directText;
                background = null;
                return true;
            case Border { Child: TextBlock borderedText } border
                when borderedText is not SelectableTextBlock:
                textBlock = borderedText;
                background = border.Background;
                return true;
            default:
                textBlock = null!;
                background = null;
                return false;
        }
    }

    private static IReadOnlyList<MarkdownTextToolTipRange> CreateToolTipRanges(InlineCollection inlines)
    {
        var ranges = new List<MarkdownTextToolTipRange>();
        var textOffset = 0;
        CollectToolTipRanges(inlines, null, ranges, ref textOffset);
        return ranges;
    }

    private static void CollectToolTipRanges(
        InlineCollection inlines,
        object? inheritedToolTip,
        List<MarkdownTextToolTipRange> ranges,
        ref int textOffset)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run { Text: { Length: > 0 } text }:
                    if (inheritedToolTip is not null)
                        ranges.Add(new MarkdownTextToolTipRange(textOffset, text.Length, inheritedToolTip));

                    textOffset += text.Length;
                    break;
                case MarkdownToolTipSpan toolTipSpan:
                    CollectToolTipRanges(toolTipSpan.Inlines, toolTipSpan.ToolTip, ranges, ref textOffset);
                    break;
                case Span span:
                    CollectToolTipRanges(span.Inlines, inheritedToolTip, ranges, ref textOffset);
                    break;
                case LineBreak:
                case InlineUIContainer:
                    textOffset++;
                    break;
            }
        }
    }

    private static void IsolateScaledText(
        InlineCollection inlines,
        double baseFontSize,
        double availableWidth)
    {
        for (var index = 0; index < inlines.Count; index++)
        {
            if (inlines[index] is not Span { Inlines.Count: > 0 } span ||
                !span.IsSet(TextElement.FontSizeProperty) ||
                Math.Abs(span.FontSize - baseFontSize) < FontSizeTolerance)
            {
                continue;
            }

            if (ContainsInlineControl(span.Inlines))
            {
                var replacements = SplitScaledSpan(span, availableWidth);
                inlines.RemoveAt(index);
                for (var replacementIndex = 0; replacementIndex < replacements.Count; replacementIndex++)
                    inlines.Insert(index + replacementIndex, replacements[replacementIndex]);

                index += replacements.Count - 1;
                continue;
            }

            inlines[index] = CreateScaledTextContainer(
                span,
                MoveInlines(span.Inlines),
                availableWidth);
        }
    }

    private static bool ContainsInlineControl(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is InlineUIContainer ||
                inline is Span span && ContainsInlineControl(span.Inlines))
            {
                return true;
            }
        }

        return false;
    }

    private static void LiftNestedInlineControls(InlineCollection inlines)
    {
        var flattened = new List<Inline>();
        MoveAndLiftInlineControls(inlines, flattened);
        inlines.AddRange(flattened);
    }

    private static IReadOnlyList<Inline> SplitScaledSpan(Span span, double availableWidth)
    {
        var flattenedInlines = new List<Inline>();
        MoveAndLiftInlineControls(span.Inlines, flattenedInlines);

        var replacements = new List<Inline>();
        var textInlines = new InlineCollection();
        foreach (var inline in flattenedInlines)
        {
            if (inline is InlineUIContainer)
            {
                AddScaledTextContainer(replacements, span, textInlines, availableWidth);
                replacements.Add(inline);
                textInlines = new InlineCollection();
            }
            else
            {
                textInlines.Add(inline);
            }
        }

        AddScaledTextContainer(replacements, span, textInlines, availableWidth);
        return replacements;
    }

    private static void MoveAndLiftInlineControls(
        InlineCollection source,
        ICollection<Inline> output)
    {
        while (source.Count > 0)
        {
            var inline = source[0];
            source.RemoveAt(0);
            if (inline is not Span nestedSpan || !ContainsInlineControl(nestedSpan.Inlines))
            {
                output.Add(inline);
                continue;
            }

            var nestedInlines = new List<Inline>();
            MoveAndLiftInlineControls(nestedSpan.Inlines, nestedInlines);
            var textInlines = new InlineCollection();
            foreach (var nestedInline in nestedInlines)
            {
                if (nestedInline is InlineUIContainer)
                {
                    AddClonedSpan(output, nestedSpan, textInlines);
                    output.Add(nestedInline);
                    textInlines = new InlineCollection();
                }
                else
                {
                    textInlines.Add(nestedInline);
                }
            }

            AddClonedSpan(output, nestedSpan, textInlines);
        }
    }

    private static void AddClonedSpan(
        ICollection<Inline> output,
        Span source,
        InlineCollection inlines)
    {
        if (inlines.Count == 0)
            return;

        var clone = CloneSpan(source);
        clone.Inlines.AddRange(inlines);
        output.Add(clone);
    }

    private static Span CloneSpan(Span source)
    {
        Span clone = source switch
        {
            MarkdownToolTipSpan toolTipSpan => new MarkdownToolTipSpan(toolTipSpan.ToolTip),
            Bold => new Bold(),
            Italic => new Italic(),
            _ => new Span()
        };

        if (source.IsSet(TextElement.BackgroundProperty))
            clone.Background = source.Background;
        if (source.IsSet(TextElement.FontFamilyProperty))
            clone.FontFamily = source.FontFamily;
        if (source.IsSet(TextElement.FontFeaturesProperty))
            clone.FontFeatures = source.FontFeatures;
        if (source.IsSet(TextElement.FontSizeProperty))
            clone.FontSize = source.FontSize;
        if (source.IsSet(TextElement.FontStretchProperty))
            clone.FontStretch = source.FontStretch;
        if (source.IsSet(TextElement.FontStyleProperty))
            clone.FontStyle = source.FontStyle;
        if (source.IsSet(TextElement.FontWeightProperty))
            clone.FontWeight = source.FontWeight;
        if (source.IsSet(TextElement.ForegroundProperty))
            clone.Foreground = source.Foreground;
        if (source.IsSet(TextElement.LetterSpacingProperty))
            clone.LetterSpacing = source.LetterSpacing;
        if (source.IsSet(Inline.TextDecorationsProperty))
            clone.TextDecorations = source.TextDecorations;
        if (source.IsSet(Inline.BaselineAlignmentProperty))
            clone.BaselineAlignment = source.BaselineAlignment;

        var elementInfo = MarkdownRenderedElementMetadata.GetElementInfo(source);
        if (elementInfo is not null)
            MarkdownRenderedElementMetadata.SetElementInfo(clone, elementInfo);

        return clone;
    }

    private static void AddScaledTextContainer(
        ICollection<Inline> output,
        Span span,
        InlineCollection textInlines,
        double availableWidth)
    {
        if (textInlines.Count > 0)
            output.Add(CreateScaledTextContainer(span, textInlines, availableWidth));
    }

    private static InlineUIContainer CreateScaledTextContainer(
        Span span,
        InlineCollection textInlines,
        double availableWidth)
    {
        var selectableText = new MarkdownWrappingSelectableTextBlock
        {
            Background = span.Background,
            FontFamily = span.FontFamily,
            FontFeatures = span.FontFeatures,
            FontSize = span.FontSize,
            FontStretch = span.FontStretch,
            FontStyle = span.FontStyle,
            FontWeight = span.FontWeight,
            Foreground = span.Foreground,
            Inlines = textInlines,
            LetterSpacing = span.LetterSpacing,
            TextDecorations = span.TextDecorations,
            TextWrapping = TextWrapping.Wrap
        };
        selectableText.SetToolTipRanges(CreateToolTipRanges(selectableText.Inlines));
        selectableText.SetRenderMap(MarkdownRenderMapBuilder.Build(selectableText.Inlines));
        MarkdownDocumentSelection.RegisterSegment(selectableText);
        SetInitialWrappingWidth(selectableText, availableWidth);

        return new InlineUIContainer(selectableText)
        {
            BaselineAlignment = span.BaselineAlignment
        };
    }

    private static InlineCollection MoveInlines(InlineCollection source)
    {
        var target = new InlineCollection();
        while (source.Count > 0)
        {
            var inline = source[0];
            source.RemoveAt(0);
            target.Add(inline);
        }

        return target;
    }

    private static void SetInitialWrappingWidth(SelectableTextBlock control, double availableWidth)
    {
        if (double.IsFinite(availableWidth) && availableWidth > 0)
            control.MaxWidth = availableWidth;
    }
}

internal sealed class MarkdownWrappingSelectableTextBlock : SelectableTextBlock
{
    private static readonly object ToolTipServiceSeed = new();
    private IReadOnlyList<MarkdownTextToolTipRange> _toolTipRanges = [];
    private MarkdownRenderMap _renderMap = MarkdownRenderMap.Empty;
    private object? _activeToolTip;

    internal IReadOnlyList<MarkdownTextToolTipRange> ToolTipRanges => _toolTipRanges;

    internal MarkdownRenderMap RenderMap => _renderMap;

    public MarkdownWrappingSelectableTextBlock()
    {
        AddHandler(ToolTip.ToolTipOpeningEvent, OnToolTipOpening);
    }

    internal void SetToolTipRanges(IReadOnlyList<MarkdownTextToolTipRange> ranges)
    {
        _toolTipRanges = ranges;
        ToolTip.SetTip(this, ranges.Count > 0 ? ToolTipServiceSeed : null);
    }

    internal void SetRenderMap(MarkdownRenderMap renderMap) =>
        _renderMap = renderMap ?? throw new ArgumentNullException(nameof(renderMap));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateToolTip(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        CloseToolTip();
        base.OnPointerExited(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseToolTip();
        base.OnDetachedFromVisualTree(e);
    }

    protected override TextLayout CreateTextLayout(string? text)
    {
        if (IsMeasureValid || Inlines is not { Count: > 0 } inlines)
            return base.CreateTextLayout(text);

        // Avalonia clears its private inline run cache when MarkdownTextBlock temporarily
        // reparents embedded controls during arrange. Rebuild from the retained public
        // inline tree so rendering and hit testing remain valid without re-entering layout.
        var source = new StringBuilder();
        var styles = new List<ValueSpan<TextRunProperties>>();
        AppendInlines(inlines, source, styles);

        var maxWidth = double.IsFinite(MaxWidth) && MaxWidth > 0
            ? MaxWidth
            : double.PositiveInfinity;
        return new TextLayout(
            source.ToString(),
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Foreground,
            TextAlignment,
            TextWrapping,
            TextTrimming,
            TextDecorations,
            FlowDirection,
            maxWidth,
            double.PositiveInfinity,
            LineHeight,
            LetterSpacing,
            MaxLines,
            FontFeatures,
            styles);
    }

    private void UpdateToolTip(Point localPosition)
    {
        object? toolTip = null;
        if (_toolTipRanges.Count > 0)
        {
            var padding = Padding;
            var textPosition = new Point(
                Math.Clamp(
                    localPosition.X - padding.Left,
                    0,
                    Math.Max(TextLayout.WidthIncludingTrailingWhitespace, 0)),
                Math.Clamp(
                    localPosition.Y - padding.Top,
                    0,
                    Math.Max(TextLayout.Height, 0)));
            var hit = TextLayout.HitTestPoint(textPosition);
            if (hit.IsInside)
            {
                foreach (var range in _toolTipRanges)
                {
                    if (hit.TextPosition >= range.Start && hit.TextPosition < range.Start + range.Length)
                    {
                        toolTip = range.ToolTip;
                        break;
                    }
                }
            }
        }

        if (Equals(_activeToolTip, toolTip))
            return;

        if (toolTip is null)
        {
            CloseToolTip();
            return;
        }

        _activeToolTip = toolTip;
        ToolTip.SetTip(this, toolTip);
    }

    private void CloseToolTip()
    {
        if (ToolTip.GetIsOpen(this))
            ToolTip.SetIsOpen(this, false);

        _activeToolTip = null;
        ToolTip.SetTip(this, _toolTipRanges.Count > 0 ? ToolTipServiceSeed : null);
    }

    private void OnToolTipOpening(object? sender, CancelRoutedEventArgs args)
    {
        if (_activeToolTip is null)
        {
            args.Cancel = true;
            return;
        }

        ToolTip.SetTip(this, _activeToolTip);
    }

    private void AppendInlines(
        InlineCollection inlines,
        StringBuilder source,
        List<ValueSpan<TextRunProperties>> styles)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run { Text: { } runText } run:
                    AppendRun(run, runText, source, styles);
                    break;
                case Span span:
                    AppendInlines(span.Inlines, source, styles);
                    break;
                case LineBreak:
                    source.Append('\n');
                    break;
            }
        }
    }

    private void AppendRun(
        Run run,
        string text,
        StringBuilder source,
        List<ValueSpan<TextRunProperties>> styles)
    {
        var start = source.Length;
        source.Append(text);
        var end = source.Length;
        var selectionStart = Math.Min(SelectionStart, SelectionEnd);
        var selectionEnd = Math.Max(SelectionStart, SelectionEnd);

        AppendStyle(start, Math.Clamp(selectionStart, start, end), run, false, styles);
        AppendStyle(
            Math.Clamp(selectionStart, start, end),
            Math.Clamp(selectionEnd, start, end),
            run,
            SelectionForegroundBrush is not null,
            styles);
        AppendStyle(Math.Clamp(selectionEnd, start, end), end, run, false, styles);
    }

    private void AppendStyle(
        int start,
        int end,
        Run run,
        bool useSelectionForeground,
        List<ValueSpan<TextRunProperties>> styles)
    {
        if (end <= start)
            return;

        styles.Add(
            new ValueSpan<TextRunProperties>(
                start,
                end - start,
                new GenericTextRunProperties(
                    new Typeface(run.FontFamily, run.FontStyle, run.FontWeight, run.FontStretch),
                    run.FontSize,
                    run.TextDecorations,
                    useSelectionForeground ? SelectionForegroundBrush : run.Foreground,
                    run.Background,
                    run.BaselineAlignment,
                    null,
                    run.FontFeatures)));
    }
}

internal sealed class MarkdownToolTipSpan(object toolTip) : Span
{
    public object ToolTip { get; } = toolTip;
}

internal readonly record struct MarkdownTextToolTipRange(int Start, int Length, object ToolTip);
