---
title: "Extending ProMarkdown"
---

# Extending ProMarkdown

ProMarkdown separates extension responsibilities into parser, block-renderer, inline-renderer, editor, and block-template contracts. An `IMarkdownPlugin` registers any combination of these capabilities through `MarkdownPluginRegistry`.

Use the extension APIs when an application needs syntax, native Avalonia visuals, editor experiences, or insertion templates that do not belong in the core library.

Detailed extension-authoring guides are being added to this section. The complete contract signatures are also listed in the [API Reference](../reference/).
