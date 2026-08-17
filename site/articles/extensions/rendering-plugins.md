---
title: "Rendering Plugins"
---

# Rendering Plugins

Rendering plugins claim Markdig AST nodes and append native Avalonia content to the current output. Implement a block renderer, an inline renderer, or both.

## Inline renderer example

This renderer gives inline code a custom foreground while preserving normal source mapping:

```csharp
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Markdig.Syntax.Inlines;
using ProMarkdown.Services;

public sealed class ProductInlineCodeRenderer : IMarkdownInlineRenderingPlugin
{
    public int Order => -10;

    public bool CanRender(Inline inline) => inline is CodeInline;

    public bool TryRender(MarkdownInlineRenderingPluginContext context)
    {
        if (context.Inline is not CodeInline code)
        {
            return false;
        }

        context.AddInline(new Run(code.Content.ToString())
        {
            Foreground = context.RenderContext.ThemePalette?.Accent
        });
        return true;
    }
}
```

Register it through an `IMarkdownPlugin` with `AddInlineRenderingPlugin`.

## Block renderer pattern

`IMarkdownBlockRenderingPlugin` receives `MarkdownBlockRenderingPluginContext`:

```csharp
public bool TryRender(MarkdownBlockRenderingPluginContext context)
{
    if (context.Block is not HeadingBlock { Level: 1 } heading)
    {
        return false;
    }

    var sourceSpan = MarkdownSourceSpan.FromMarkdig(heading.Span);
    var headingText = sourceSpan
        .Slice(context.ParseResult.OriginalMarkdown)
        .Trim()
        .TrimStart('#')
        .Trim();

    var view = new Border
    {
        Padding = new Thickness(12),
        MaxWidth = context.AvailableWidth,
        Child = new TextBlock
        {
            Text = headingText,
            FontSize = context.RenderContext.FontSize * 1.8,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        }
    };

    context.AddBlockControl(view);
    return true;
}
```

The block context exposes `Block`, `ParseResult`, `RenderContext`, `Output`, quote/list depth, and available width. Inline context exposes the corresponding `Inline` node and lets you append an Avalonia `Inline` or embedded control.

## Context helpers

- `AddBlockControl` and `AddInlineControl` register the visual in `MarkdownRenderMap` automatically.
- `AddInline` appends an Avalonia document inline while preserving the current AST association.
- `ResolveUri` applies the render context's `BaseUri` rules.
- `TrackResource` joins a disposable resource to the render lifetime.
- `IsCurrentRender` tells asynchronous work whether its originating generation is still active.

Do not mutate `Output` later from a background thread. Create a placeholder control synchronously, perform expensive work away from the UI thread, then marshal only the minimal visual update to Avalonia's dispatcher after checking the render generation.

## Fine-grained visual hit testing

Pass a `MarkdownVisualHitTestHandler` to `AddBlockControl` or `AddInlineControl` when different regions of one rich control correspond to different source spans:

```csharp
context.AddBlockControl(view, request =>
{
    var region = view.ResolveRegion(request.LocalPoint);
    return region is null
        ? null
        : new MarkdownVisualHitTestResult
        {
            SourceSpan = region.SourceSpan,
            LocalHighlightRects = [region.Bounds]
        };
});
```

The handler can override the AST node, rendered element kind, source span, and local highlight rectangles. Omitted values fall back to the visual's default map entry.

## Fallback rules

`CanRender` should be a fast type or metadata check. `TryRender` should return:

- true after it has completely handled the node
- false when input is unsupported or another renderer should try

Throw only for invalid extension state or a failure that the application cannot represent. For user-authored content errors, prefer a diagnostic control so the rest of the document remains usable.
