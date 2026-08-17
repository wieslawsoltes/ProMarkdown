# Markdown Syntax Highlighting Plugin Refactor Plan

## Problem

`ProMarkdown` still contains a large built-in code syntax highlighting implementation inside `MarkdownInlineRenderingService`. The feature works well, but the highlighting rules, token classification, and language-family heuristics are feature-specific rendering logic that can move into a dedicated plugin while the core library keeps a plain-text code-block fallback.

## Current Remaining Core Feature

- built-in code syntax highlighting
  - JSON, markup, shell, SQL, and C-style token classification
  - language-label normalization and family resolution
  - inline span generation for highlighted code blocks

## Proposed Approach

1. Keep Markdig responsible for parsing fenced and indented code blocks.
2. Add a dedicated `ProMarkdown.Plugin.SyntaxHighlighting` project with:
   - the built-in language-family highlighter
   - code-token span generation
   - a block rendering plugin for code blocks
3. Keep `ProMarkdown.Plugin.TextMate` higher priority so it still handles supported grammars first.
4. Preserve the simpler core code-block renderer as a plain-text fallback for consumers that register no optional syntax-highlighting plugins.
5. Register the new plugin in the sample app and add sample content that exercises the built-in highlighter on a language alias that TextMate does not claim.

## Notes

- This refactor is about moving feature-specific highlighting logic out of core, not removing code-block rendering from the default markdown experience.
- The fallback path should still preserve code-block chrome, hit testing, and readable monospaced text.
- `TextMate` should remain the preferred renderer when it recognizes a grammar; the new plugin should fill the gap for unsupported-but-known aliases such as `postgresql` or `tsql`.

## Todos

- `analyze-more-plugin-candidates`: verify the best next plugin extraction target after containers and footers.
- `implement-next-plugin-refactor`: add the syntax-highlighting plugin project, move highlighting logic, keep core fallback behavior, and wire the sample.
- `validate-next-plugin-refactor`: build and test the solution after the refactor.

## Completion

- Added `src/ProMarkdown.Plugin.SyntaxHighlighting/` with the extracted built-in language-family syntax highlighter and code-block rendering plugin.
- Added `src/ProMarkdown/Services/MarkdownCodeBlockRendering.cs` so the syntax-highlighting plugin, `TextMate` plugin, and the core fallback renderer share the same code-block surface and hit-testing behavior.
- Simplified the core `MarkdownInlineRenderingService` code-block path so core remains a plain-text fallback while optional plugins provide richer highlighting.
- Updated `ProMarkdown.Sample` to register `SyntaxHighlightingMarkdownPlugin` and added a `postgresql` code fence to exercise the non-TextMate highlighting path.
- Validation succeeded with:
  - `dotnet build ProMarkdown.slnx --nologo --verbosity minimal`
  - `dotnet test --solution ProMarkdown.slnx`
