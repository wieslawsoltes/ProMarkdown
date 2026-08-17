---
title: "Plugin Model and Registration"
---

# Plugin Model and Registration

An `IMarkdownPlugin` is a composition boundary. Its single `Register` method adds one or more focused capabilities to `MarkdownPluginRegistry`.

```csharp
using ProMarkdown.Services;

public sealed class ProductMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry
            .AddParserPlugin(new ProductParserPlugin())
            .AddBlockRenderingPlugin(new ProductBlockRenderer())
            .AddInlineRenderingPlugin(new ProductInlineRenderer())
            .AddBlockTemplateProvider(new ProductTemplateProvider())
            .AddEditorPlugin(new ProductEditorPlugin());
    }
}
```

A plugin does not need to implement every category. Keep parsing, rendering, editing, and template logic in separate classes so each can be tested and ordered independently.

## Registry categories

| Registration method | Contract | Responsibility |
| --- | --- | --- |
| `AddParserPlugin` | `IMarkdownParserPlugin` | Configure Markdig and optionally transform source. |
| `AddBlockRenderingPlugin` | `IMarkdownBlockRenderingPlugin` | Render a block AST node as native Avalonia content. |
| `AddInlineRenderingPlugin` | `IMarkdownInlineRenderingPlugin` | Render an inline AST node as an inline or embedded control. |
| `AddEditorPlugin` | `IMarkdownEditorPlugin` | Resolve source-mapped nodes and create an editor control. |
| `AddBlockTemplateProvider` | `IMarkdownBlockTemplateProvider` | Supply insert-before/after templates to editors. |
| `AddPlugin` | `IMarkdownPlugin` | Invoke another composite plugin's registration. |

All registration methods reject null and return the registry, so fluent registration is safe.

## Ordering and fallback

Each focused contract has an `Order` property with a default value of zero. Lower values run first.

- Parser plugins configure the pipeline and transform source in ascending order.
- Block and inline renderers are queried in ascending order; the first matching plugin that returns true owns that node.
- Editor candidates first compete by nearest AST match, then preferred editor ID, then ascending order.
- Block-template providers are queried in ascending order.

Return false from a renderer when it cannot produce a result so later plugins or the core renderer can handle the node. Use a negative order only when the extension must precede built-in or general fallback behavior.

## Create matching services

Pass the plugin to the controller and editing service:

```csharp
IMarkdownPlugin[] plugins = [new ProductMarkdownPlugin()];

markdownView.RenderController = MarkdownRenderingServices.CreateController(plugins);
markdownView.EditingService = MarkdownRenderingServices.CreateEditingService(plugins);
```

The factory adds core editor plugins and the thematic-break renderer before registering your list. It then builds immutable ordered service collections. Reuse those services rather than rebuilding them for each document.

## Extension design checklist

- Keep source spans accurate if editing or source mapping matters.
- Render native Avalonia controls; do not block the UI thread for expensive work.
- Track subscriptions and disposable resources through the provided context.
- Check `IsCurrentRender()` before publishing delayed render results.
- Resolve URLs through the context so `BaseUri` behavior stays consistent.
- Give editor IDs and template IDs stable, package-qualified values.
- Return false when another renderer should get a chance to handle the node.
