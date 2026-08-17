---
title: "Services and Helpers API"
---

# Services and Helpers API

Namespace: `ProMarkdown.Services`

## MarkdownRenderingServices

Factory and shared-default entry point.

```csharp
public static IMarkdownRenderController DefaultController { get; }
public static IMarkdownHitTestingService DefaultHitTestingService { get; }
public static IMarkdownEditingService DefaultEditingService { get; }

public static IMarkdownRenderController CreateDefaultController();
public static IMarkdownRenderController CreateController(params IMarkdownPlugin[] plugins);
public static IMarkdownRenderController CreateController(IEnumerable<IMarkdownPlugin>? plugins);
public static IMarkdownEditingService CreateEditingService(params IMarkdownPlugin[] plugins);
public static IMarkdownEditingService CreateEditingService(IEnumerable<IMarkdownPlugin>? plugins);
public static IMarkdownHitTestingService CreateHitTestingService();
```

See [Rendering Services](../markdown/rendering-services/) for composition and reuse guidance.

## Parsing and rendering implementations

### MarkdownParsingService

```csharp
public MarkdownParsingService(IEnumerable<IMarkdownParserPlugin>? parserPlugins = null);
public MarkdownParseResult Parse(string markdown);
```

Implements `IMarkdownParsingService`. Parser plugins are ordered by `Order`; the Markdig pipeline is constructed once.

### MarkdownInlineRenderingService

```csharp
public MarkdownInlineRenderingService(
    IEnumerable<IMarkdownBlockRenderingPlugin>? blockRenderingPlugins = null,
    IEnumerable<IMarkdownInlineRenderingPlugin>? inlineRenderingPlugins = null);

public MarkdownRenderResult Render(
    MarkdownParseResult parseResult,
    MarkdownRenderContext context);
```

Implements `IMarkdownInlineRenderingService` and renders the AST into Avalonia inlines plus source maps.

### MarkdownRenderController

```csharp
public MarkdownRenderController(
    IMarkdownParsingService parsingService,
    IMarkdownInlineRenderingService inlineRenderingService);

public MarkdownRenderResult Render(MarkdownRenderRequest request);
```

Implements `IMarkdownRenderController` and applies the post-render theme, borders, tasks, block quotes, and selection normalizers.

### MarkdownHitTestingService

```csharp
public MarkdownHitTestingService();
public MarkdownHitTestResult? HitTest(MarkdownHitTestRequest request);
public MarkdownHitTestResult? HitTestTextPosition(
    MarkdownRenderResult renderResult,
    int textPosition);
```

Implements `IMarkdownHitTestingService`.

### MarkdownEditingService

```csharp
public MarkdownEditingService(
    IEnumerable<IMarkdownEditorPlugin>? editorPlugins = null,
    IEnumerable<IMarkdownBlockTemplateProvider>? blockTemplateProviders = null);

public MarkdownEditorSession? ResolveSession(MarkdownEditorResolveRequest request);
public Control? CreateEditor(MarkdownEditorRenderRequest request);
```

Implements `IMarkdownEditingService`. See the [Editing API](editing-api/) for related models.

## MarkdownThemePalette

Semantic brush collection used by core and plugin renderers.

```csharp
public static MarkdownThemePalette Light { get; }
public static MarkdownThemePalette Dark { get; }
public static MarkdownThemePalette Resolve(IBrush? foreground);
```

All instance properties are init-only:

| Category | Properties |
| --- | --- |
| Mode and text | `IsDark`, `Foreground`, `MutedForeground`, `Accent`, `HyperlinkForeground` |
| Surfaces | `Surface`, `SurfaceRaised`, `Border`, `QuoteBorder` |
| Code and tables | `InlineCodeBackground`, `CodeHeaderBackground`, `TableHeaderBackground`, `TableAlternateRowBackground` |
| Text semantics | `MarkedTextBackground`, `InsertedTextForeground` |
| Alerts | `NoteAccent`, `NoteBackground`, `TipAccent`, `TipBackground`, `ImportantAccent`, `ImportantBackground`, `WarningAccent`, `WarningBackground`, `CautionAccent`, `CautionBackground` |
| Syntax | `CodeKeywordForeground`, `CodeTypeForeground`, `CodeStringForeground`, `CodeCommentForeground`, `CodeNumberForeground`, `CodePropertyForeground`, `CodeTagForeground`, `CodeAttributeForeground`, `CodePunctuationForeground` |

See [Theming](../markdown/theming/) for examples.

## MarkdownCodeBlockRendering

Shared code-surface helpers for rendering plugins.

```csharp
public static IBrush DefaultCodeTextForeground { get; }
public static string NormalizeCode(string? code);
public static string NormalizeLanguageHint(string? languageHint);
public static string FormatLanguageLabel(string? languageHint);

public static MarkdownCodeBlockSurface CreateSurface(
    CodeBlock block,
    string code,
    InlineCollection inlines,
    string? languageHint,
    string? metaText,
    MarkdownRenderContext renderContext,
    IBrush? textForeground = null,
    IBrush? metaForeground = null);
```

`MarkdownCodeBlockSurface` is a positional record containing `Control` and optional `MarkdownVisualHitTestHandler`. It supports record equality, cloning, and deconstruction.

## MarkdownCalloutRendering

Shared callout presentation helpers:

```csharp
public static MarkdownCalloutPresentation ResolvePresentation(
    string? kind,
    string? fallbackTitle = null);

public static Control CreateCalloutSurface(
    string title,
    string? subtitle,
    Control body,
    IBrush accentBrush,
    IBrush background);

public static string FormatLabel(string? value);
```

`MarkdownCalloutPresentation` is a positional record struct with `Title`, `AccentBrush`, and `Background`, including value equality and deconstruction.
