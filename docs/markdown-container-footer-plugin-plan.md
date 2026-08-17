# Markdown Container and Footer Plugin Refactor Plan

## Problem

`ProMarkdown` still contains feature-specific rendering logic for generic custom containers and footers inside the core markdown renderer. These blocks already work, but they do not yet follow the richer plugin-backed architecture used by Mermaid, Math, Definition Lists, Alerts, and Figures.

## Current Remaining Core Features

- `CustomContainer`
  - generic fenced `:::` containers
  - info token plus optional arguments
  - rendered as a neutral callout surface in core
- `FooterBlock`
  - `^^`-prefixed footer lines
  - rendered as a simple top-bordered block in core

## Proposed Approach

1. Keep Markdig responsible for markdown-level parsing for both features.
2. Add a dedicated `ProMarkdown.Plugin.CustomContainers` project with:
   - internal AST
   - parser
   - richer rendering
   - block templates
   - structured editor
3. Add a dedicated `ProMarkdown.Plugin.Footers` project with:
   - internal AST
   - parser
   - footer rendering
   - block templates
   - structured editor
4. Preserve the simpler core rendering as fallback for non-plugin consumers.
5. Register the new plugins in the sample app and extend the sample markdown to demonstrate both features.

## Notes

- Mermaid custom-container support is safe because Mermaid intercepts its own syntax at parse time and produces `MermaidDiagramBlock` rather than `CustomContainer`.
- Shared callout visuals should continue to come from `MarkdownCalloutRendering`.
- The goal is architectural cleanup and richer editing, not a breaking removal of default core behavior.

## Todos

- `analyze-plugin-candidates`: identify the remaining safe plugin extraction targets in the markdown core library.
- `plan-plugin-refactor`: capture the container/footer refactor strategy in docs and the session workspace.
- `implement-plugin-refactor`: add the new plugin projects, parsing, rendering, editor support, and sample wiring.
- `validate-plugin-refactor`: build and test the solution after the refactor.

## Completion

- Added `src/ProMarkdown.Plugin.CustomContainers/` with a custom-container AST, parser, renderer, templates, and a structured block editor plugin.
- Added `src/ProMarkdown.Plugin.Footers/` with a footer AST, parser, renderer, templates, and a structured block editor plugin.
- Registered the new plugins in the sample and expanded the sample markdown to demonstrate plugin-backed custom-container and footer editing.
- Validation succeeded with:
  - `dotnet build ProMarkdown.slnx --nologo --verbosity minimal`
  - `dotnet test --solution ProMarkdown.slnx`
