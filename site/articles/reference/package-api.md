---
title: "Plugin Package API"
---

# Plugin Package API

Each optional NuGet package exposes one parameterless `IMarkdownPlugin` entry point. Add instances to the same plugin collection supplied to `MarkdownRenderingServices.CreateController` and `CreateEditingService`.

```csharp
IMarkdownPlugin[] plugins =
[
    new AlertsMarkdownPlugin(),
    new MathMarkdownPlugin()
];

markdown.RenderController = MarkdownRenderingServices.CreateController(plugins);
markdown.EditingService = MarkdownRenderingServices.CreateEditingService(plugins);
```

For package installation, syntax, behavior, and combination examples, see [Plugin Ecosystem](../markdown/plugin-ecosystem/).

## Package entry points

| NuGet package | Namespace | Public plugin class | Registered capabilities |
| --- | --- | --- | --- |
| `ProMarkdown.Plugin.Alerts` | `ProMarkdown.Plugin.Alerts` | `AlertsMarkdownPlugin` | Alert block renderer, template provider, editor |
| `ProMarkdown.Plugin.CustomContainers` | `ProMarkdown.Plugin.CustomContainers` | `CustomContainersMarkdownPlugin` | Container block renderer, template provider, editor |
| `ProMarkdown.Plugin.DefinitionLists` | `ProMarkdown.Plugin.DefinitionLists` | `DefinitionListMarkdownPlugin` | Definition-list renderer, template provider, editor |
| `ProMarkdown.Plugin.Figures` | `ProMarkdown.Plugin.Figures` | `FiguresMarkdownPlugin` | Figure renderer, template provider, editor |
| `ProMarkdown.Plugin.Footers` | `ProMarkdown.Plugin.Footers` | `FootersMarkdownPlugin` | Footer renderer, template provider, editor |
| `ProMarkdown.Plugin.Math` | `ProMarkdown.Plugin.Math` | `MathMarkdownPlugin` | Block and inline math renderers, template provider, block and inline editors |
| `ProMarkdown.Plugin.Mermaid` | `ProMarkdown.Plugin.Mermaid` | `MermaidMarkdownPlugin` | Parser, diagram renderer, template provider, editor |
| `ProMarkdown.Plugin.SyntaxHighlighting` | `ProMarkdown.Plugin.SyntaxHighlighting` | `SyntaxHighlightingMarkdownPlugin` | Built-in code-block renderer |
| `ProMarkdown.Plugin.TextMate` | `ProMarkdown.Plugin.TextMate` | `TextMateMarkdownPlugin` | TextMate code renderer and AvaloniaEdit code editor |

Every plugin class has a public parameterless constructor and this method:

```csharp
public void Register(MarkdownPluginRegistry registry);
```

Registration is normally performed by the service factories. Call `Register` directly only when assembling a `MarkdownPluginRegistry` yourself.

## Stable editor identifiers

Use these public constants when selecting a preferred editor:

| Type | Constant | Feature |
| --- | --- | --- |
| `AlertsMarkdownEditorIds` | `Block` | `Alert` |
| `CustomContainerMarkdownEditorIds` | `Block` | `CustomContainer` |
| `DefinitionListMarkdownEditorIds` | `Block` | `DefinitionList` |
| `FiguresMarkdownEditorIds` | `Block` | `Figure` |
| `FooterMarkdownEditorIds` | `Block` | `Footer` |
| `MathMarkdownEditorIds` | `Block` | block `Math` |
| `MathMarkdownEditorIds` | `Inline` | inline `Math` |
| `MermaidMarkdownPlugin` | `MermaidEditorId` | `Mermaid` |
| `TextMateMarkdownPlugin` | `TextMateCodeEditorId` | `Code` |

Example:

```csharp
markdown.EditorPreferences
    .PreferEditor(
        MarkdownEditorFeature.Math,
        MathMarkdownEditorIds.Inline)
    .PreferEditor(
        MarkdownEditorFeature.Code,
        TextMateMarkdownPlugin.TextMateCodeEditorId);
```

`SyntaxHighlightingMarkdownPlugin` is rendering-only and does not expose an editor ID.

## Mermaid public API

`MermaidMarkdownPlugin` also supports host configuration and renderer injection:

```csharp
public MermaidMarkdownPlugin(MermaidMarkdownPluginOptions options);
public MermaidMarkdownPlugin(
    IMermaidSvgRenderer renderer,
    MermaidMarkdownPluginOptions? options = null);
```

`IMermaidSvgRenderer.RenderAsync` receives a `MermaidSvgRenderRequest` with `Source`, a background-thread-safe `Palette` snapshot, `FontFamily`, and `FontSize`, plus the render cancellation token. The plugin sanitizes returned SVG before displaying it.

`MermaidMarkdownPluginOptions` has init-only `AccessibleName`, `ErrorText`, `RetryText`, and optional `ActivateLinkAsync` properties. Link activation is restricted to absolute HTTPS URIs even when a callback is supplied.

`MermaidDiagramControl` is a public, styleable `TemplatedControl` created by the plugin. It exposes the styled `ThemePalette` property and read-only direct properties for `IsLoading`, `HasImage`, `HasError`, `ErrorText`, `SourceText`, `SourceFontSize`, `RetryText`, and `RetryCommand`. The control owns its SVG resources and implements `IDisposable`; normal plugin rendering registers it with the render resource tracker automatically.

## Composition notes

- Plugin classes retain any resources they reuse across renders; construct them once and reuse the plugin list.
- `TextMateMarkdownPlugin` has a lower renderer order than `SyntaxHighlightingMarkdownPlugin`, so TextMate gets the first opportunity. It returns `false` for unsupported language hints and Mermaid aliases, allowing a later renderer to handle the block.
- `MermaidMarkdownPlugin` recognizes `mermaid`, `mmd`, `mermaidjs`, and `diagram-mermaid` fenced language aliases as well as its documented custom-container form.
- Stable editor constants are API identifiers. Display labels, visual styling, and internal renderer/editor implementation types are not public contracts.
