---
title: "Rendering Services"
---

# Rendering Services

Most applications configure services once and assign them to every `MarkdownTextBlock` that uses the same feature set.

## Default services

`MarkdownRenderingServices` exposes shared defaults:

```csharp
IMarkdownRenderController controller = MarkdownRenderingServices.DefaultController;
IMarkdownEditingService editing = MarkdownRenderingServices.DefaultEditingService;
IMarkdownHitTestingService hitTesting = MarkdownRenderingServices.DefaultHitTestingService;
```

The default parser enables precise Markdig source locations, advanced extensions, emoji and smileys, SmartyPants, and YAML front matter. The default registry also includes the built-in editor set and thematic-break renderer.

`MarkdownTextBlock` uses these shared services automatically, so no setup is required for the core feature set.

## Configure plugins

Create one plugin list and use it for both rendering and editing:

```csharp
using ProMarkdown.Plugin.Alerts;
using ProMarkdown.Plugin.SyntaxHighlighting;
using ProMarkdown.Services;

IMarkdownPlugin[] plugins =
[
    new AlertsMarkdownPlugin(),
    new SyntaxHighlightingMarkdownPlugin()
];

var controller = MarkdownRenderingServices.CreateController(plugins);
var editing = MarkdownRenderingServices.CreateEditingService(plugins);
var hitTesting = MarkdownRenderingServices.CreateHitTestingService();
```

Assign them to a control:

```csharp
markdownView.RenderController = controller;
markdownView.EditingService = editing;
markdownView.HitTestingService = hitTesting;
```

Keep the plugin collection stable and reuse the resulting services. Constructing a controller builds a Markdig pipeline and orders registered parser and renderer plugins; it is configuration work rather than per-document work.

## Service composition

The public abstractions are deliberately small:

- `IMarkdownParsingService.Parse` produces a `MarkdownParseResult`.
- `IMarkdownInlineRenderingService.Render` converts a parse result and render context into a `MarkdownRenderResult`.
- `IMarkdownRenderController.Render` owns the complete parse, render, normalization, and source-map pipeline.
- `IMarkdownHitTestingService` resolves pointer or rendered-text positions.
- `IMarkdownEditingService` resolves and creates source-aware editors.

For custom composition, instantiate `MarkdownParsingService`, `MarkdownInlineRenderingService`, and `MarkdownRenderController` directly. Plugin authors normally use `MarkdownPluginRegistry` and the factory methods instead because those preserve the built-in editors and normalizers.

## Render requests and resource ownership

Lower-level consumers call `IMarkdownRenderController.Render` with a `MarkdownRenderRequest` containing source and a `MarkdownRenderContext`. The context supplies typography, width, base URI, theme, image policy and loading, render generation, cancellation, resource tracking, and optional editor state.

Each render owns a `MarkdownRenderResourceTracker`. Dispose the previous result's tracker when replacing or removing a render; plugins use the same tracker for subscriptions, asynchronous image resources, and other disposable state. `MarkdownTextBlock` performs this lifecycle automatically.

`RenderGeneration` and `IsCurrentRender` let delayed plugin work verify that its originating render is still current before mutating a control. `CancellationToken` signals replacement or teardown of the generation.

## Asynchronous plugin work

Plugins that outlive the synchronous `Render` call must register their activity:

```csharp
var operation = context.RenderContext.BeginAsyncOperation();
try
{
    await RenderContentAsync(context.RenderContext.CancellationToken);
}
finally
{
    operation.Dispose();
}
```

The completion lease keeps `MarkdownTextBlock.IsRendering` true until it is disposed. Activity is generation-scoped: cancellation or late disposal from an obsolete generation cannot alter the active generation. Continue to use `ResourceTracker` for subscriptions, controls, bitmaps, and other resources that must be disposed with the render.

When a plugin embeds another `MarkdownTextBlock`, call `context.RenderContext.TrackNestedRendering(nestedControl)`. The parent remains active until the nested render completes or is canceled, and the tracking subscription is owned by the parent resource tracker.

Image loading and the Mermaid plugin use this mechanism, so their downloads and diagram renders are included in `IsRendering` and `RenderCompleted` automatically.
