# Markdown Rendering Expansion Plan

## Current Engine

`ProMarkdown` parses markdown with `Markdig` through `MarkdownParsingService`, using `UsePreciseSourceLocation()` and `UseAdvancedExtensions()`. Rendering is handled by `MarkdownInlineRenderingService`, which walks the parsed AST and produces Avalonia `InlineCollection` content for `MarkdownTextBlock`.

The renderer is already plugin-first:

- parser plugins can extend the `Markdig` pipeline
- block and inline rendering plugins can intercept AST nodes
- editor plugins and block template providers extend in-place editing

This architecture is solid. The remaining work is mostly about filling in native visuals for extension nodes the parser already emits.

## Supported Today

- headings, paragraphs, blockquotes, ordered and unordered lists
- task list markers
- tables
- fenced and indented code blocks
- inline emphasis, bold, and strikethrough
- links, autolinks, images, and footnote links
- footnote groups
- HTML blocks and inlines rendered safely as literal text
- Mermaid via plugin
- TextMate code highlighting via plugin

## Remaining Renderer Gaps

The parser emits additional node types that do not yet have first-class visuals:

### Block nodes

- `AlertBlock`
- `CustomContainer`
- `MathBlock`
- `DefinitionList`, `DefinitionItem`, `DefinitionTerm`
- `Figure`, `FigureCaption`
- `FooterBlock`
- metadata blocks that should be suppressed rather than shown literally:
  - `Abbreviation`
  - `YamlFrontMatterBlock`

### Inline nodes

- `AbbreviationInline`
- `MathInline`
- `EmphasisDelimiterInline` for superscript and subscript
- style helpers for punctuation-oriented extensions such as smart quotes

## Implementation Scope

This change will:

1. add native block rendering for alerts, custom containers, math, definition lists, figures, and footer content
2. add inline rendering for abbreviations, inline math, superscript, and subscript
3. suppress metadata-only blocks such as abbreviation definitions and YAML front matter
4. preserve safe HTML behavior
5. update the sample markdown showcase to exercise the new features

## File Plan

- `src/ProMarkdown/Services/MarkdownInlineRenderingService.cs`
  - add block handlers
  - add inline handlers
  - add shared helper surfaces and styling
- `src/ProMarkdown.Sample/Views/MainWindow.axaml.cs`
  - add showcase content for the new rendering coverage

## Validation Plan

- run `dotnet test --solution ProMarkdown.slnx`
- confirm the sample markdown now demonstrates alerts, containers, math, abbreviations, and superscript/subscript rendering

## Safety and UX Constraints

- do not render raw HTML as executable or interactive web content
- stay within the existing `MarkdownTextBlock` and rich-block-host model
- keep visuals Fluent-aligned, restrained, and consistent with the current markdown surfaces
