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

Implements `IMarkdownRenderController` and applies the post-render rich-border, task-list, block-quote, and document-selection normalizers. Core and official plugin colors are assigned directly from `MarkdownThemePalette` during rendering rather than rewritten afterward.

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
public MarkdownThemePalette WithForeground(IBrush foreground);
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

## Image loading

### MarkdownImageSourceKinds

Flags enum with `None`, `Data`, `File`, `Remote`, and `All` values.

### MarkdownImageOptions

```csharp
public const long DefaultMaximumBytes;
public const long DefaultMaximumPixelCount;
public static readonly TimeSpan DefaultRemoteTimeout;

public static MarkdownImageOptions Default { get; }
public static MarkdownImageOptions BlockRemote { get; }
public static MarkdownImageOptions EmbeddedOnly { get; }

public MarkdownImageSourceKinds AllowedSourceKinds { get; init; }
public long MaximumBytes { get; init; }
public long MaximumPixelCount { get; init; }
public TimeSpan RemoteTimeout { get; init; }
```

The defaults are 8 MiB, 64 × 1024 × 1024 decoded pixels, and 15 seconds. `Default` allows data, file, HTTP, and HTTPS sources; `BlockRemote` allows data and files; `EmbeddedOnly` allows only data URIs.

### Loader contracts

```csharp
public sealed class MarkdownImageLoadRequest
{
    public required Uri Source { get; init; }
    public required MarkdownImageOptions Options { get; init; }
}

public interface IMarkdownImageLoader
{
    Task<Bitmap> LoadAsync(
        MarkdownImageLoadRequest request,
        CancellationToken cancellationToken);
}

public sealed class DefaultMarkdownImageLoader : IMarkdownImageLoader
{
    public static DefaultMarkdownImageLoader Instance { get; }
}
```

The default loader validates the policy, encoded byte count, and decoded pixel count. Custom loaders should honor the request options and cancellation token. See [Image Loading and Security](../markdown/image-loading/) for integration guidance.

## MarkdownSelection

Document-wide selection and clipboard helpers:

```csharp
public static bool CanCopy(MarkdownTextBlock? control);
public static Task CopyAsync(MarkdownTextBlock? control);
public static void SelectAll(MarkdownTextBlock? control);
public static string GetSelectedText(MarkdownTextBlock? control);
public static string GetDocumentText(MarkdownTextBlock? control);
public static Task CopyDocumentTextAsync(MarkdownTextBlock? control);
```

The complete-document operations do not modify the active selection.

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
    string fallbackTitle);

public static Control CreateCalloutSurface(
    string title,
    string? subtitle,
    Control body,
    IBrush accentBrush,
    IBrush background);

public static Control CreateCalloutSurface(
    string title,
    string? subtitle,
    Control body,
    IBrush accentBrush,
    IBrush background,
    MarkdownThemePalette palette);

public static MarkdownCalloutPresentation ResolvePresentation(
    string? kind,
    string fallbackTitle,
    MarkdownThemePalette palette);

public static string FormatLabel(string value);
```

The overloads without a palette remain available for compatibility and use the built-in light palette. Official plugins pass the active render palette so borders, muted text, and semantic callout colors remain theme-correct.

`MarkdownCalloutPresentation` is a positional record struct with `Title`, `AccentBrush`, and `Background`, including value equality and deconstruction.
