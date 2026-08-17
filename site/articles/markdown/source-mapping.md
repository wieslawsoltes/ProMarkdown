---
title: "Source Mapping and Hit Testing"
---

# Source Mapping and Hit Testing

Every rendered text segment and rich control is mapped back to a Markdig AST node and source span. Use this for hover highlights, source-editor synchronization, inspection tools, context commands, and custom editing gestures.

## Hit test a point

```csharp
var point = pointerEvent.GetPosition(markdownView);
var hit = markdownView.HitTestMarkdown(point);

if (hit is not null)
{
    var source = hit.SourceSpan.Slice(hit.ParseResult.OriginalMarkdown);
    ShowSourceRange(hit.SourceSpan.Start, hit.SourceSpan.Length);
    ShowHighlight(hit.HighlightRects);
}
```

`MarkdownHitTestResult` exposes:

| Property | Meaning |
| --- | --- |
| `AstNode` | Node object, node kind, source span, line, and column. |
| `SourceSpan` | Effective source range, including a plugin override when supplied. |
| `MatchedSourceText` | The effective span sliced from the original source. |
| `ElementKind` | Text, line break, inline control, or block control. |
| `RenderedTextPosition` | Position in the rendered text stream when applicable. |
| `Visual` | Rich control associated with the hit, when applicable. |
| `HighlightRects` | Control-relative rectangles suitable for a hover overlay. |
| `HighlightBounds` | Union of the highlight rectangles, when any exist. |
| `ParseResult` | The source document and AST parent map used for the render. |

## Source spans

`MarkdownSourceSpan` uses a zero-based `Start` and a `Length`. `End` is inclusive, while `EndExclusive` is suitable for ranges and slicing. `Empty` has no valid range. Use `Contains`, `Slice`, or `FromMarkdig` instead of repeating boundary calculations.

Parser plugins can transform source before Markdig parses it. `MarkdownParseResult.UsesOriginalSourceSpans` is true only when parsed and original source are identical. Extensions that transform source must preserve offsets or provide accurate custom AST spans if consumers depend on editing or selection.

## Inspect the latest render

`MarkdownTextBlock.LastRenderMap` contains:

- `TextEntries` — rendered text ranges mapped to AST nodes and element kinds
- `VisualEntries` — rich Avalonia controls mapped to AST nodes and optional plugin hit-test handlers

Use `TryGetTextEntry` or `TryGetVisualEntry` for direct lookup. `LastParseResult.ParentMap` and `TryGetParent` let tools walk from an inline node to its enclosing block.

## Service-level hit testing

`IMarkdownHitTestingService.HitTestTextPosition` maps a rendered-text position without requiring a pointer:

```csharp
var result = markdownView.LastRenderResult;
if (result is not null)
{
    var hit = markdownView.HitTestingService.HitTestTextPosition(result, textPosition);
}
```

Custom render controls can call `IMarkdownHitTestingService.HitTest` with a `MarkdownHitTestRequest` containing the host, point, render result, text layout, and padding. `MarkdownTextBlock.HitTestMarkdown` already supplies those details.
