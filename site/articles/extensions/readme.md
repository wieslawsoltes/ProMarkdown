---
title: "Extending ProMarkdown"
---

# Extending ProMarkdown

ProMarkdown separates extension responsibilities into parser, block-renderer, inline-renderer, editor, and block-template contracts. An `IMarkdownPlugin` registers any combination of these capabilities through `MarkdownPluginRegistry`.

Use the extension APIs when an application needs syntax, native Avalonia visuals, editor experiences, or insertion templates that do not belong in the core library.

## Extension paths

- [Plugin Model and Registration](plugin-model/) explains composition, ordering, and service setup.
- [Parser Plugins](parser-plugins/) configure Markdig or transform source before parsing.
- [Rendering Plugins](rendering-plugins/) turn AST nodes into native Avalonia text or controls.
- [Editor Plugins](editor-plugins/) resolve source-mapped nodes and commit source replacements.
- [Block Templates](block-templates/) add reusable insertion choices to editor surfaces.

The complete contract signatures are listed in the [API Reference](../reference/).
