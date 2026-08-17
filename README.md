# CodexGui.Markdown

Reusable Avalonia markdown rendering and editing libraries built on Markdig.

This repository contains the Markdown subsystem extracted from CodexGui. The existing project, namespace, package, and solution names are intentionally unchanged during this move.

## Highlights

- Avalonia control and service layer for rendering Markdown documents.
- Source-aware editing, selection, hit testing, and URI handling.
- Optional plugin packages for alerts, custom containers, definition lists, figures, footers, math, Mermaid diagrams, syntax highlighting, and TextMate integration.
- Standalone sample application that composes the complete Markdown stack.
- Headless rendering and Mermaid test coverage.

## Projects

| Project | Responsibility |
| --- | --- |
| `src/CodexGui.Markdown` | Core controls, parsing, rendering, editing, layout, selection, and theme services. |
| `src/CodexGui.Markdown.Plugin.Alerts` | Alert block parsing, rendering, and editing. |
| `src/CodexGui.Markdown.Plugin.CustomContainers` | Generic custom-container support. |
| `src/CodexGui.Markdown.Plugin.DefinitionLists` | Definition-list parsing, rendering, and editing. |
| `src/CodexGui.Markdown.Plugin.Figures` | Figure and caption support. |
| `src/CodexGui.Markdown.Plugin.Footers` | Footer metadata blocks. |
| `src/CodexGui.Markdown.Plugin.Math` | Inline and block math support. |
| `src/CodexGui.Markdown.Plugin.Mermaid` | Mermaid diagram rendering. |
| `src/CodexGui.Markdown.Plugin.SyntaxHighlighting` | Built-in code syntax highlighting. |
| `src/CodexGui.Markdown.Plugin.TextMate` | TextMate-backed highlighting and editor integration. |
| `src/CodexGui.Markdown.Sample` | Desktop sample that composes the library and plugins. |
| `tests/CodexGui.Markdown.Tests` | Headless rendering and Mermaid tests. |

## NuGet packages

The packable projects retain their current package identities:

- `CodexGui.Markdown`
- `CodexGui.Markdown.Plugin.Alerts`
- `CodexGui.Markdown.Plugin.CustomContainers`
- `CodexGui.Markdown.Plugin.DefinitionLists`
- `CodexGui.Markdown.Plugin.Figures`
- `CodexGui.Markdown.Plugin.Footers`
- `CodexGui.Markdown.Plugin.Math`
- `CodexGui.Markdown.Plugin.Mermaid`
- `CodexGui.Markdown.Plugin.SyntaxHighlighting`
- `CodexGui.Markdown.Plugin.TextMate`

## Prerequisites

- .NET 10 SDK

## Build and test

```bash
dotnet restore CodexGui.slnx
dotnet build CodexGui.slnx
dotnet test --solution CodexGui.slnx
```

## Run the sample

```bash
dotnet run --project src/CodexGui.Markdown.Sample/CodexGui.Markdown.Sample.csproj
```

The sample provides an editor and preview surface with the plugin ecosystem registered together.

## Documentation

The Lunet documentation site lives under `site/`.

```bash
dotnet tool restore
bash ./check-docs.sh
```

To serve it locally:

```bash
bash ./serve-docs.sh
```

PowerShell equivalents are included for the build, check, and serve commands. Detailed Markdown implementation notes live under `docs/`.

## CI and releases

- `.github/workflows/build.yml` builds, tests, validates docs, and packs the Markdown projects.
- `.github/workflows/docs.yml` publishes the Lunet site.
- `.github/workflows/release.yml` validates and publishes the existing Markdown NuGet packages for tagged releases.

## License

MIT. See [`LICENSE`](LICENSE).
