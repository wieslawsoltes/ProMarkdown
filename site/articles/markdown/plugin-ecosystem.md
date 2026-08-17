---
title: "Plugin Ecosystem"
---

# Plugin Ecosystem

The solution includes these Markdown projects:

- `ProMarkdown` — core rendering and editing services
- `ProMarkdown.Plugin.Alerts` — alert or callout blocks
- `ProMarkdown.Plugin.CustomContainers` — generic custom containers
- `ProMarkdown.Plugin.DefinitionLists` — definition lists
- `ProMarkdown.Plugin.Figures` — figures and captions
- `ProMarkdown.Plugin.Footers` — footer metadata blocks
- `ProMarkdown.Plugin.Math` — mathematical notation
- `ProMarkdown.Plugin.Mermaid` — Mermaid diagrams
- `ProMarkdown.Plugin.SyntaxHighlighting` — built-in syntax highlighting
- `ProMarkdown.Plugin.TextMate` — TextMate editing and highlighting integration
- `ProMarkdown.Sample` — sample application composing the stack

## Design direction

- keep the core focused
- isolate optional behavior in small projects
- make registration explicit in consumers
- share rendering and editing contracts instead of duplicating feature logic
