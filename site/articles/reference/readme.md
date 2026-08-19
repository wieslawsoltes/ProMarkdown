---
title: "API Reference"
---

# API Reference

This reference covers the exported ProMarkdown API by assembly and responsibility. Signatures are source-backed and grouped for application developers and extension authors.

## Core assembly

- [Controls API](controls-api/) — `MarkdownTextBlock`, selection commands, render activity, edit operations, and control events
- [Services and Helpers API](services-api/) — service factories, image loading, selection, theming, and rendering helpers
- [Rendering and Source Map API](rendering-models-api/) — render requests/results, cancellation and async activity, AST metadata, maps, hit testing, and resource ownership
- [Editing API](editing-api/) — editor services, sessions, preferences, templates, and UI helpers
- [Extension API](extension-api/) — parser, block, inline, editor, template, registry, and context contracts

## Optional assemblies

- [Plugin Package API](package-api/) — every shipped plugin entry point and stable editor ID

## Contributor reference

- [Repository Structure](repository-structure/) — solution projects and source layout

API types use two namespaces:

```csharp
using ProMarkdown.Controls;
using ProMarkdown.Services;
```

Each optional package adds its own `ProMarkdown.Plugin.*` namespace.
