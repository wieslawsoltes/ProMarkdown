---
title: "Overview"
---

# Overview

The `ProMarkdown` package provides `MarkdownTextBlock`, a source-aware Avalonia control backed by Markdig. Use the control directly for ordinary application surfaces or compose the lower-level parsing, rendering, editing, and hit-testing services when you need custom behavior.

## Choose an integration level

- **Control-first:** bind Markdown to `MarkdownTextBlock` and use the default renderer.
- **Configured control:** provide a plugin-aware render controller, editing service, theme palette, and editor preferences.
- **Service-first:** use `IMarkdownParsingService`, `IMarkdownRenderController`, or `IMarkdownHitTestingService` in a custom document surface.
- **Extension authoring:** implement parser, block renderer, inline renderer, editor, or block-template contracts and register them through `IMarkdownPlugin`.

Optional packages add alerts, custom containers, definition lists, figures, footers, math, Mermaid, syntax highlighting, and TextMate integration without expanding the core dependency set.

## What to read next

- [What's New in 0.1.1](whats-new-0.1.1/)
- [User Guide](../markdown/)
- [Plugin Ecosystem](../markdown/plugin-ecosystem/)
- [API Reference](../reference/)
- [Run the sample](running-the-sample/)
