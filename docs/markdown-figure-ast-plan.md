# Markdown Figure AST Implementation Plan

## Problem

`ProMarkdown` currently renders `Figure` and `FigureCaption` nodes directly in the core renderer. The fallback view works, but figures still lack a dedicated semantic AST, markdown rebuild helpers, block templates, and a structured editor for preserving opening and closing fence captions separately from the figure body.

## Current Figure Format

- Markdig parses fenced figure blocks opened and closed with `^` fences.
- Example:

  ```md
  ^^^ Preview surface
  ![Preview image](https://example.com/image.png)
  
  Supporting markdown can live inside the figure body.
  ^^^ Figure captions can also live on the closing fence.
  ```

- Markdig emits:
  - `Figure`
  - `FigureCaption`
- `FigureCaption` blocks come from the trailing text on the opening or closing fence line.
- Source spans are already available through `UsePreciseSourceLocation()`.

## Proposed Approach

1. Keep Markdig responsible for markdown-level figure parsing.
2. Add a dedicated `ProMarkdown.Plugin.Figures` project for the richer figure experience.
3. Build an internal figure AST that captures the figure body markdown, leading caption, trailing caption, fence length, source spans, and diagnostics.
4. Render figures from that AST using a richer editorial surface with caption regions and nested markdown previews.
5. Add figure block templates plus a structured editor for lead caption, body markdown, and trailing caption.
6. Update the sample and docs to demonstrate AST-backed figure rendering and editing.

## AST Scope

- source markdown for the full figure block
- leading and trailing fence captions
- normalized body markdown
- source spans for the full figure and body content
- preserved fence length for safe markdown rebuilding
- diagnostics for empty figures or malformed fence structure

## Todos

- `analyze-figure-format`: inspect current figure parsing/rendering and confirm the plugin architecture.
- `write-figure-plan`: capture the figure AST/rendering/editor plan in the session workspace and repository docs.
- `implement-figure-ast`: add the figure AST plus parser and markdown rebuild helpers.
- `implement-figure-rendering`: add plugin-backed figure rendering, templates, and structured editing.
- `validate-figure-feature`: build and test the solution after the figure plugin changes.

## Notes

- Preserve Markdig as the figure parser instead of replacing the fence syntax.
- Keep the core figure renderer as a safe fallback for non-plugin consumers.
- Prefer a behavior-safe rollout: the plugin should provide the richer figure experience without destabilizing the existing markdown engine.

## Completion

- Added `src/ProMarkdown.Plugin.Figures/` with a figure AST, parser, renderer, templates, and a structured block editor plugin.
- Modeled figure syntax around Markdig's `^^^` fences, preserving opening and closing fence captions separately from the figure body markdown.
- Registered the new figure plugin in the sample and expanded the sample markdown to demonstrate plugin-backed figure editing.
- Validation succeeded with:
  - `dotnet build ProMarkdown.slnx --nologo --verbosity minimal`
  - `dotnet test --solution ProMarkdown.slnx`
