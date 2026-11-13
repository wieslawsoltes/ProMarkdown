---
title: "What's New in 0.1.1"
---

# What's New in 0.1.1

ProMarkdown 0.1.1 strengthens document interaction, asynchronous rendering, image security, semantic theming, and Mermaid integration. Package and namespace names are unchanged from 0.1.0.

## Selection and clipboard

- Document selection now remains aligned when a `MarkdownTextBlock` is inside a scrolled view.
- `SelectionChanged`, `CanCopyDocumentSelection`, `CopySelectionCommand`, and `SelectAllCommand` expose selection state directly from the control.
- `MarkdownSelection.GetDocumentText()` and `CopyDocumentTextAsync()` retrieve or copy the complete rendered document without changing the active selection.

Applications that supplied selection command adapters can bind directly to the control commands.

## Image loading

- `ImageOptions` and `ImageLoader` provide first-class image policy and loading customization.
- `Default`, `BlockRemote`, and `EmbeddedOnly` policies select allowed source kinds.
- The built-in loader enforces byte, decoded-pixel, and remote-timeout limits.
- In-flight image work is canceled when a render is replaced or the control detaches.

The default still allows data, file, HTTP, and HTTPS images for compatibility. Applications rendering untrusted content should explicitly choose a restrictive policy. See [Image Loading and Security](../markdown/image-loading/).

## Render activity

- `MarkdownTextBlock.IsRendering` reports work for the active render generation.
- `RenderCompleted` fires when all synchronous and registered asynchronous work for that generation has finished.
- `MarkdownRenderContext.CancellationToken`, `BeginAsyncOperation()`, and `TrackNestedRendering()` let plugins participate in the same lifetime.

Completion leases are generation-scoped. Disposing a stale lease cannot complete a newer render.

## Semantic theming

Core rendering and official plugins now consume `MarkdownThemePalette` directly. The removed post-render color rewrite is no longer needed, and controls created by third-party plugins retain their own colors. Hyperlinks use `HyperlinkForeground`; ordinary diagram strokes and arrows use `Foreground`.

Review custom palettes to ensure every role used by enabled plugins is populated. See [Theming](../markdown/theming/).

## Mermaid

`ProMarkdown.Plugin.Mermaid` now exposes:

- `IMermaidSvgRenderer` and `MermaidSvgRenderRequest` for injected renderers
- `MermaidMarkdownPluginOptions` for localized accessible, error, and retry text and host-controlled link activation
- the styleable `MermaidDiagramControl` with loading, image, error, source, and retry state

Mermaid rendering participates in `IsRendering`, rerenders for palette changes, permits only safe HTTPS links, and retains bounded caching, sanitization, timeout, cancellation, and retry behavior. Fenced aliases, descriptor syntax, custom containers, templates, and editor registration remain available.

## Upgrade checklist

1. Update all ProMarkdown packages used by the application to `0.1.1` together.
2. Remove host-side scrolled-selection, selection-command, image-blocking, theme-normalization, or Mermaid rendering workarounds now covered upstream.
3. Select an explicit image policy for the document trust boundary.
4. Bind loading indicators to `IsRendering` instead of artificial completion delays.
5. Verify custom palettes, selection while scrolled, image policy, and Mermaid behavior in light and dark themes.
