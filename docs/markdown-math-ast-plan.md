# Markdown Math AST Plan

## Goal

Move markdown math support from styled raw text to a fully parsed internal math AST that drives both rendering and editing.

## Math Format Analysis

The markdown layer already comes from Markdig:

- inline math supports `$...$` and single-line `$$...$$`
- block math uses fenced `$$` delimiters on their own lines
- source spans are preserved through `UsePreciseSourceLocation()`
- the markdown AST exposes `MathInline` and `MathBlock`

This means the missing layer is not markdown parsing. The missing layer is semantic parsing of the math payload itself.

## Target Architecture

### 1. Math AST

Introduce an internal AST for math content with node kinds for:

- expressions / rows
- identifiers, numbers, operators, text
- commands and symbols
- grouped expressions
- fractions
- radicals
- script attachments
- accents
- delimiter groups
- matrix and alignment environments
- diagnostics / error nodes

### 2. Math Parser

Add a dedicated parser that consumes the math payload from `MathInline.Content` or `MathBlock.Lines` and produces:

- AST root
- diagnostics
- normalized source text

The parser should understand common TeX-style constructs including:

- `\frac`
- `\sqrt` and optional index
- `^` and `_`
- brace groups
- command-based symbols such as Greek letters and large operators
- `\text`, `\mathrm`, `\operatorname`
- `\left...\right...`
- matrix-like `\begin{...}...\end{...}` environments

### 3. Native Rendering

Render the AST with native Avalonia controls:

- horizontal math rows
- fraction stacks with rules
- radical layouts
- script layouts
- accent overlays
- matrix grids
- block formula surfaces
- inline formula controls

### 4. Editing

Add math editor support for:

- block math (`MathBlock`)
- inline math (`MathInline`)

This requires:

- a new `MarkdownEditorFeature.Math`
- math block templates
- math editor plugins
- inline editor injection in the markdown renderer so inline formulas can be edited in place

## Files Expected to Change

- `src/CodexGui.Markdown/Services/MarkdownRenderContracts.cs`
- `src/CodexGui.Markdown/Services/MarkdownInlineRenderingService.cs`
- `src/CodexGui.Markdown/Services/MarkdownBuiltInEditorPlugins.cs`
- `src/CodexGui.Markdown/Services/MarkdownSourceEditing.cs`
- new math parser / AST / renderer service files under `src/CodexGui.Markdown.Plugin.Math/`
- `src/CodexGui.Markdown.Sample/Views/MainWindow.axaml.cs`

## Implementation Note

The completed implementation now lives in a dedicated plugin project:

- `src/CodexGui.Markdown.Plugin.Math/`

Core markdown still owns the shared render/edit contracts and inline-editor pipeline, while math parsing, rendering, templates, and editor plugins are registered through `MathMarkdownPlugin`.

## Validation

- `dotnet build CodexGui.slnx --nologo --verbosity minimal`
- `dotnet test --solution CodexGui.slnx`
- ensure the sample contains inline and block formulas that exercise fractions, roots, scripts, matrices, and text operators
