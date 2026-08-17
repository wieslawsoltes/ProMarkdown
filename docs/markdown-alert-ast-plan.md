# Markdown Alert AST Implementation Plan

## Problem

`ProMarkdown` currently renders `AlertBlock` nodes directly in the core renderer. The visual fallback works, but alerts still lack a dedicated semantic AST, markdown rebuild helpers, block templates, and a structured editor for switching alert kinds without manually rewriting quoted markdown.

## Current Alert Format

- GitHub-style alert blocks use quoted markdown with an alert marker on the first line.
- Example:

  ```md
  > [!WARNING]
  > Review this change before shipping it.
  > 
  > - keep nested markdown intact
  > - preserve quoted body lines
  ```

- Markdig emits:
  - `AlertBlock`
- Source spans are already available through `UsePreciseSourceLocation()`.

## Proposed Approach

1. Keep Markdig responsible for markdown-level alert parsing.
2. Add a dedicated `ProMarkdown.Plugin.Alerts` project for the richer alert experience.
3. Build an internal alert AST that captures the alert kind, normalized body markdown, source spans, and diagnostics.
4. Extract shared callout surface helpers into core so both fallback rendering and the new plugin can reuse the same Fluent callout styling.
5. Add alert block templates plus a structured block editor with kind selection and live preview.
6. Update the sample and docs to demonstrate AST-backed alert rendering and editing.

## AST Scope

- alert kind token
- normalized body markdown
- source span for the full alert block
- source span for the quoted body region
- diagnostics for invalid headers or empty bodies
- helpers for rebuilding valid alert markdown from structured editor state

## Todos

- `analyze-alert-format`: inspect current alert parsing/rendering and confirm the plugin architecture.
- `write-alert-plan`: capture the alert AST/rendering/editor plan in the session workspace and repository docs.
- `implement-alert-ast`: add the alert AST plus parser and markdown rebuild helpers.
- `implement-alert-rendering`: add plugin-backed alert rendering, templates, and structured editing.
- `validate-alert-feature`: build and test the solution after the alert plugin changes.

## Notes

- Preserve Markdig as the markdown parser instead of replacing alert syntax parsing.
- Keep the core alert renderer as a safe fallback for non-plugin consumers.
- Reuse shared callout presentation logic so alert visuals stay consistent between fallback and plugin-backed rendering.

## Completion

- Added `src/ProMarkdown.Plugin.Alerts/` with an alert AST, parser, renderer, templates, and a structured block editor plugin.
- Extracted shared callout surface helpers into `src/ProMarkdown/Services/MarkdownCalloutRendering.cs` so fallback rendering and plugin rendering reuse the same visual system.
- Registered the new alert plugin in the markdown sample and expanded the sample alert content to demonstrate AST-backed alert editing.
- Validation succeeded with:
  - `dotnet build ProMarkdown.slnx --nologo --verbosity minimal`
  - `dotnet test --solution ProMarkdown.slnx`
