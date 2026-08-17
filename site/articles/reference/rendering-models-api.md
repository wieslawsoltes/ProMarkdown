---
title: "Rendering and Source Map API"
---

# Rendering and Source Map API

Namespace: `ProMarkdown.Services`

These types carry source text through parsing, rendering, source mapping, hit testing, and cleanup. For an end-to-end example, see [Source Mapping and Hit Testing](../markdown/source-mapping/).

## Render inputs

### MarkdownRenderRequest

```csharp
public string? Markdown { get; init; }
public required MarkdownRenderContext Context { get; init; }
```

### MarkdownRenderContext

Create one context per render. All properties except `ThemePalette` and `EditorState` are required initializers.

| Property | Type | Purpose |
| --- | --- | --- |
| `BaseUri` | `Uri?` | Resolves relative links and images. |
| `FontSize` | `double` | Base text size. |
| `FontFamily` | `FontFamily` | Base text family. |
| `Foreground` | `IBrush?` | Host foreground used for palette resolution. |
| `TextWrapping` | `TextWrapping` | Host wrapping behavior. |
| `ThemePalette` | `MarkdownThemePalette?` | Optional explicit semantic palette. |
| `AvailableWidth` | `double` | Width available to block renderers and editors. |
| `RenderGeneration` | `int` | Identity of this render. |
| `ResourceTracker` | `MarkdownRenderResourceTracker` | Owns render-scoped disposables. |
| `IsCurrentRender` | `Func<int, bool>` | Tests whether asynchronous work still belongs to the active generation. |
| `EditorState` | `MarkdownEditorState?` | Optional editing callbacks and active session. |

## Parse result and source spans

### MarkdownParseResult

```csharp
public MarkdownParseResult(
    MarkdownDocument document,
    string originalMarkdown,
    string parsedMarkdown,
    IReadOnlyDictionary<MarkdownObject, MarkdownObject?>? parentMap = null);

public static MarkdownParseResult Empty { get; }
public MarkdownDocument Document { get; }
public string OriginalMarkdown { get; }
public string ParsedMarkdown { get; }
public IReadOnlyDictionary<MarkdownObject, MarkdownObject?> ParentMap { get; }
public bool UsesOriginalSourceSpans { get; }
public bool TryGetParent(MarkdownObject markdownObject, out MarkdownObject? parent);
```

`UsesOriginalSourceSpans` is false when a parser plugin transformed the source before Markdig parsed it. In that case, spans refer to `ParsedMarkdown`; source-based editing is deliberately unavailable.

### MarkdownSourceSpan

```csharp
public readonly record struct MarkdownSourceSpan(int Start, int Length)
```

Members: static `Empty`; computed `End`, `EndExclusive`, and `IsEmpty`; `Contains(int)`; `Slice(string)`; and `FromMarkdig(SourceSpan)`. As a record struct it also supplies value equality and deconstruction.

### MarkdownAstNodeInfo

```csharp
public MarkdownAstNodeInfo(
    MarkdownObject node,
    MarkdownSourceSpan sourceSpan,
    int line,
    int column);
```

Read-only properties: `Node`, `SourceSpan`, `Line`, `Column`, and computed `NodeKind`.

## Render result and lifetime

### MarkdownRenderResult

```csharp
public MarkdownRenderResult(
    InlineCollection inlines,
    MarkdownRenderResourceTracker resourceTracker,
    MarkdownParseResult parseResult,
    MarkdownRenderMap renderMap);

public InlineCollection Inlines { get; }
public MarkdownRenderResourceTracker ResourceTracker { get; }
public MarkdownParseResult ParseResult { get; }
public MarkdownRenderMap RenderMap { get; }
public static MarkdownRenderResult Empty(MarkdownRenderResourceTracker resourceTracker);
```

The result owns its `ResourceTracker`. Dispose the previous tracker when replacing a manual render result. `MarkdownTextBlock` handles this automatically.

### MarkdownRenderResourceTracker

```csharp
public sealed class MarkdownRenderResourceTracker : IDisposable
{
    public void Track(IDisposable resource);
    public void Dispose();
}
```

Tracking after disposal immediately disposes the resource; repeated disposal is safe.

## Render maps

### MarkdownRenderedElementKind

```csharp
public enum MarkdownRenderedElementKind
{
    Text,
    LineBreak,
    InlineControl,
    BlockControl
}
```

### MarkdownTextSourceMapEntry

```csharp
public MarkdownTextSourceMapEntry(
    int renderedTextStart,
    int renderedTextLength,
    MarkdownAstNodeInfo astNode,
    MarkdownRenderedElementKind elementKind);
```

Read-only properties: `RenderedTextStart`, `RenderedTextLength`, computed `RenderedTextEndExclusive`, `AstNode`, and `ElementKind`. `Contains(int textPosition)` tests the half-open rendered-text range.

### MarkdownVisualSourceMapEntry

```csharp
public MarkdownVisualSourceMapEntry(
    Control control,
    MarkdownAstNodeInfo astNode,
    MarkdownRenderedElementKind elementKind,
    MarkdownVisualHitTestHandler? hitTestHandler);
```

Read-only properties: `Control`, `AstNode`, `ElementKind`, and `HitTestHandler`.

### MarkdownRenderMap

```csharp
public MarkdownRenderMap(
    IReadOnlyList<MarkdownTextSourceMapEntry> textEntries,
    IReadOnlyDictionary<Control, MarkdownVisualSourceMapEntry> visualEntries);

public static MarkdownRenderMap Empty { get; }
public IReadOnlyList<MarkdownTextSourceMapEntry> TextEntries { get; }
public IReadOnlyDictionary<Control, MarkdownVisualSourceMapEntry> VisualEntries { get; }
public bool TryGetTextEntry(int textPosition, out MarkdownTextSourceMapEntry? entry);
public bool TryGetVisualEntry(Control control, out MarkdownVisualSourceMapEntry? entry);
```

## Hit testing

### Service requests

`MarkdownHitTestRequest` has required init-only properties `Host`, `Point`, `TextLayout`, `Padding`, and `RenderResult`.

`MarkdownVisualHitTestRequest` has required init-only properties `Host`, `HitVisual`, `Control`, `HostPoint`, `LocalPoint`, `RenderResult`, and `DefaultEntry`.

### Custom visual result and handler

```csharp
public delegate MarkdownVisualHitTestResult?
    MarkdownVisualHitTestHandler(MarkdownVisualHitTestRequest request);
```

`MarkdownVisualHitTestResult` has optional init-only `AstNode`, `ElementKind`, and `SourceSpan` overrides plus `LocalHighlightRects`, which defaults to an empty list. Return `null` from a handler to use normal visual mapping.

### MarkdownHitTestResult

```csharp
public MarkdownHitTestResult(
    MarkdownAstNodeInfo astNode,
    MarkdownRenderedElementKind elementKind,
    MarkdownParseResult parseResult,
    int? renderedTextPosition = null,
    Control? visual = null,
    IReadOnlyList<Rect>? highlightRects = null,
    MarkdownSourceSpan? sourceSpanOverride = null);
```

Read-only properties are `AstNode`, `ElementKind`, `ParseResult`, `RenderedTextPosition`, `Visual`, `HighlightRects`, and `SourceSpanOverride`. Computed properties are effective `SourceSpan`, unioned `HighlightBounds`, and `MatchedSourceText`.
