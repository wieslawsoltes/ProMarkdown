---
title: "User Guide"
---

# User Guide

This guide explains how application developers consume ProMarkdown. It starts with the `MarkdownTextBlock` control, then moves through rendering configuration, source-aware editing, hit testing, theming, and extension points.

## Core package

Install `ProMarkdown` for the control and the core parsing, rendering, editing, selection, layout, theme, and hit-testing services. The default control configuration is enough for common Markdown documents.

## Optional features

Plugin packages are opt-in. Registration is explicit so applications control syntax, rendering behavior, editor selection, startup cost, and transitive dependencies.

## Advanced composition

The public service contracts let applications render outside the stock control, map rendered content back to source, provide custom editor surfaces, or introduce new Markdown syntax and visuals.

## Guide map

- [MarkdownTextBlock](markdown-text-block/) covers the primary control and its lifecycle.
- [Rendering Services](rendering-services/) configures core and plugin-aware pipelines.
- [Source-Aware Editing](editing/) explains editor sessions and persisted updates.
- [Source Mapping and Hit Testing](source-mapping/) connects rendered content to source.
- [Theming](theming/) configures semantic light, dark, and custom palettes.
- [Interactive Task Lists](task-lists/) persists checkbox changes safely.
- [Plugin Ecosystem](plugin-ecosystem/) introduces optional packages and extension points.
