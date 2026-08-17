# Markdown Definition List AST Plan

## Goal

Move definition-list support from direct Markdig-node rendering to a dedicated semantic AST that drives richer rendering and structured preview editing.

## Why Definition Lists

Definition lists are the strongest remaining recently added block-format family for a dedicated AST-backed implementation:

- they have real structure: entries, one or more terms, and block content
- they currently render, but only through direct Markdig traversal
- they do not have any block templates or editor plugins
- they are large enough to justify a separate plugin project without bloating the core markdown assembly

## Current State

The markdown layer already comes from Markdig:

- `DefinitionList`
- `DefinitionItem`
- `DefinitionTerm`

Today, `MarkdownInlineRenderingService` renders those nodes directly by:

- collecting terms from each item
- indenting the definition body
- rendering terms as bold text

This is functionally correct, but it is not semantic enough for rich editing or polished glossary-style layouts.

## Target Architecture

### 1. Dedicated Plugin Project

Create a new plugin project:

- `src/CodexGui.Markdown.Plugin.DefinitionLists/`

This plugin will own:

- definition-list AST
- definition-list parser
- AST-backed rendering
- definition-list templates
- definition-list editor plugin

### 2. Definition List AST

Introduce an internal AST that models:

- a definition-list document
- ordered entries
- one or more term nodes per entry
- definition markdown/content payload per entry
- diagnostics for malformed or incomplete entries

### 3. Parsing

Keep Markdig responsible for markdown-level definition-list parsing, then convert `DefinitionList` nodes into the internal AST.

The parser should also support parsing raw definition-list markdown for editor previews and round-tripping.

### 4. Rendering

Render definition lists from the AST using a more intentional layout:

- grouped card/surface for the whole list
- distinct left-side term rail / column
- rich definition body rendering
- support for multiple terms per entry
- diagnostics panel when parsing finds malformed entries

### 5. Editing

Add full preview-editing support for the whole definition list block:

- definition-list block templates
- a dedicated `MarkdownEditorFeature.DefinitionList`
- a definition-list editor plugin
- structured item editing with:
  - terms editor
  - definition body editor
  - add/remove entry actions
  - live preview

## Files Expected to Change

- `src/CodexGui.Markdown/Services/MarkdownRenderContracts.cs`
- `src/CodexGui.Markdown/Services/MarkdownInlineRenderingService.cs`
- `src/CodexGui.Markdown.Sample/CodexGui.Markdown.Sample.csproj`
- `src/CodexGui.Markdown.Sample/Views/MainWindow.axaml.cs`
- `CodexGui.slnx`
- new plugin project files under `src/CodexGui.Markdown.Plugin.DefinitionLists/`

## Implementation Note

The completed implementation lives in a dedicated plugin project:

- `src/CodexGui.Markdown.Plugin.DefinitionLists/`

Core markdown still keeps its simpler built-in definition-list fallback renderer, while the sample and any plugin-aware consumer can opt into the richer AST-backed rendering and editing experience by registering `DefinitionListMarkdownPlugin`.

## Validation

- `dotnet build CodexGui.slnx --nologo --verbosity minimal`
- `dotnet test --solution CodexGui.slnx`
- ensure the sample exercises:
  - multiple terms per entry
  - inline markdown inside terms
  - multi-paragraph definition bodies
  - nested lists/code in definitions
