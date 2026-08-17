---
title: "API Reference"
---

# API Reference

This reference covers the exported ProMarkdown API by assembly and responsibility. Signatures are source-backed and grouped for application developers and extension authors.

## Core assembly

- [Controls API](controls-api/) — `MarkdownTextBlock`, edit operations, and control events
- [Services and Helpers API](services-api/) — service factories, concrete implementations, theming, and rendering helpers
- [Rendering and Source Map API](rendering-models-api/) — render requests/results, AST metadata, maps, hit testing, and resource ownership
- [Editing API](editing-api/) — editor services, sessions, preferences, templates, and UI helpers

## Contributor reference

- [Repository Structure](repository-structure/) — solution projects and source layout

API types use two namespaces:

```csharp
using ProMarkdown.Controls;
using ProMarkdown.Services;
```

Each optional package adds its own `ProMarkdown.Plugin.*` namespace.
