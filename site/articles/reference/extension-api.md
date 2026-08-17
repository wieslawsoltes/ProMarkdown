---
title: "Extension API"
---

# Extension API

Namespace: `ProMarkdown.Services`

These are the complete contracts used to compose ProMarkdown and author plugins. All `Order` defaults are zero; lower values run first. See the [Extension Authoring](../extensions/) section for worked examples.

## Rendering service contracts

```csharp
public interface IMarkdownParsingService
{
    MarkdownParseResult Parse(string markdown);
}

public interface IMarkdownInlineRenderingService
{
    MarkdownRenderResult Render(
        MarkdownParseResult parseResult,
        MarkdownRenderContext context);
}

public interface IMarkdownRenderController
{
    MarkdownRenderResult Render(MarkdownRenderRequest request);
}

public interface IMarkdownHitTestingService
{
    MarkdownHitTestResult? HitTest(MarkdownHitTestRequest request);
    MarkdownHitTestResult? HitTestTextPosition(
        MarkdownRenderResult renderResult,
        int textPosition);
}

public interface IMarkdownEditingService
{
    MarkdownEditorSession? ResolveSession(MarkdownEditorResolveRequest request);
    Control? CreateEditor(MarkdownEditorRenderRequest request);
}
```

Applications normally obtain these from `MarkdownRenderingServices`; implement them only when replacing a pipeline stage.

## Plugin bundle

```csharp
public interface IMarkdownPlugin
{
    void Register(MarkdownPluginRegistry registry);
}
```

An `IMarkdownPlugin` is a convenient package that registers any combination of the more focused contracts.

## Parser plugin

```csharp
public interface IMarkdownParserPlugin
{
    int Order => 0;
    void Configure(MarkdownPipelineBuilder builder) { }
    string TransformMarkdown(string markdown) => markdown;
}
```

`TransformMarkdown` plugins run in order before parsing. Then all `Configure` methods build the shared Markdig pipeline once. A transform that changes the source also makes original-source editing unavailable because Markdig spans no longer address the original string.

## Block rendering plugin

```csharp
public interface IMarkdownBlockRenderingPlugin
{
    int Order => 0;
    bool CanRender(Block block);
    bool TryRender(MarkdownBlockRenderingPluginContext context);
}
```

The first ordered plugin that returns `true` from `TryRender` owns the block. Returning `false` allows later plugins or the built-in renderer to handle it.

### MarkdownBlockRenderingPluginContext

The framework constructs this type. Read-only properties are `Block`, `ParseResult`, `RenderContext`, `Output`, `QuoteDepth`, `ListDepth`, and `AvailableWidth`.

```csharp
public void AddBlockControl(
    Control control,
    MarkdownVisualHitTestHandler? hitTestHandler = null);
public Uri? ResolveUri(string? url);
public void TrackResource(IDisposable resource);
public bool IsCurrentRender();
```

Use `AddBlockControl` instead of directly inserting a control so the render map and optional detailed hit handler are registered. Use `TrackResource` for subscriptions, timers, and other disposable state. Check `IsCurrentRender` before publishing asynchronous results.

## Inline rendering plugin

```csharp
public interface IMarkdownInlineRenderingPlugin
{
    int Order => 0;
    bool CanRender(Markdig.Syntax.Inlines.Inline inline);
    bool TryRender(MarkdownInlineRenderingPluginContext context);
}
```

Selection follows the same first-success rule as block rendering.

### MarkdownInlineRenderingPluginContext

Read-only properties are `Inline`, `ParseResult`, `RenderContext`, `Output`, `QuoteDepth`, and `ListDepth`.

```csharp
public void AddInline(Avalonia.Controls.Documents.Inline inline);
public void AddInlineControl(
    Control control,
    MarkdownVisualHitTestHandler? hitTestHandler = null);
public Uri? ResolveUri(string? url);
public void TrackResource(IDisposable resource);
public bool IsCurrentRender();
```

## Editor plugin

```csharp
public interface IMarkdownEditorPlugin
{
    string EditorId { get; }
    MarkdownEditorFeature Feature { get; }
    int Order => 0;
    bool TryResolveTarget(
        MarkdownEditorResolveContext context,
        out MarkdownEditorTarget? target);
    Control? CreateEditor(MarkdownEditorPluginContext context);
}
```

The editing service considers targets nearest to the hit node first, then a preferred editor ID for that feature, then `Order`. `EditorId` must be stable and unique within the configured plugin list.

## Block template provider

```csharp
public interface IMarkdownBlockTemplateProvider
{
    int Order => 0;
    IEnumerable<MarkdownBlockTemplate> GetTemplates(
        MarkdownBlockTemplateContext context);
}
```

Templates from providers are ordered and combined for the active editor. See [Block Templates](../extensions/block-templates/).

## MarkdownPluginRegistry

```csharp
public sealed class MarkdownPluginRegistry
{
    public IReadOnlyList<IMarkdownParserPlugin> ParserPlugins { get; }
    public IReadOnlyList<IMarkdownBlockRenderingPlugin> BlockRenderingPlugins { get; }
    public IReadOnlyList<IMarkdownInlineRenderingPlugin> InlineRenderingPlugins { get; }
    public IReadOnlyList<IMarkdownEditorPlugin> EditorPlugins { get; }
    public IReadOnlyList<IMarkdownBlockTemplateProvider> BlockTemplateProviders { get; }

    public MarkdownPluginRegistry AddPlugin(IMarkdownPlugin plugin);
    public MarkdownPluginRegistry AddParserPlugin(IMarkdownParserPlugin plugin);
    public MarkdownPluginRegistry AddBlockRenderingPlugin(IMarkdownBlockRenderingPlugin plugin);
    public MarkdownPluginRegistry AddInlineRenderingPlugin(IMarkdownInlineRenderingPlugin plugin);
    public MarkdownPluginRegistry AddEditorPlugin(IMarkdownEditorPlugin plugin);
    public MarkdownPluginRegistry AddBlockTemplateProvider(IMarkdownBlockTemplateProvider provider);
}
```

All registration methods are fluent and preserve registration order. `MarkdownRenderingServices.CreateController` consumes parser and renderer lists; `CreateEditingService` consumes editor and template lists. Pass the same plugin collection to both factories when a package supplies rendering and editing together.
