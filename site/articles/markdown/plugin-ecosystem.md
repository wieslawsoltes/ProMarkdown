---
title: "Plugin Ecosystem"
---

# Plugin Ecosystem

The solution includes these Markdown projects:

- `CodexGui.Markdown` — core rendering and editing services
- `CodexGui.Markdown.Plugin.Alerts` — alert or callout blocks
- `CodexGui.Markdown.Plugin.CustomContainers` — generic custom containers
- `CodexGui.Markdown.Plugin.DefinitionLists` — definition lists
- `CodexGui.Markdown.Plugin.Figures` — figures and captions
- `CodexGui.Markdown.Plugin.Footers` — footer metadata blocks
- `CodexGui.Markdown.Plugin.Math` — mathematical notation
- `CodexGui.Markdown.Plugin.Mermaid` — Mermaid diagrams
- `CodexGui.Markdown.Plugin.SyntaxHighlighting` — built-in syntax highlighting
- `CodexGui.Markdown.Plugin.TextMate` — TextMate editing and highlighting integration
- `CodexGui.Markdown.Sample` — sample application composing the stack

## Design direction

- keep the core focused
- isolate optional behavior in small projects
- make registration explicit in consumers
- share rendering and editing contracts instead of duplicating feature logic
