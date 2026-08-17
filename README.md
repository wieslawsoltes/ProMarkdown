# ProMarkdown

Reusable Avalonia markdown rendering and editing libraries built on Markdig.

## Highlights

- Avalonia control and service layer for rendering Markdown documents.
- Source-aware editing, selection, hit testing, and URI handling.
- Optional plugin packages for alerts, custom containers, definition lists, figures, footers, math, Mermaid diagrams, syntax highlighting, and TextMate integration.
- Standalone sample application that composes the complete Markdown stack.
- Headless rendering and Mermaid test coverage.

## Projects

| Project | Responsibility |
| --- | --- |
| `src/ProMarkdown` | Core controls, parsing, rendering, editing, layout, selection, and theme services. |
| `src/ProMarkdown.Plugin.Alerts` | Alert block parsing, rendering, and editing. |
| `src/ProMarkdown.Plugin.CustomContainers` | Generic custom-container support. |
| `src/ProMarkdown.Plugin.DefinitionLists` | Definition-list parsing, rendering, and editing. |
| `src/ProMarkdown.Plugin.Figures` | Figure and caption support. |
| `src/ProMarkdown.Plugin.Footers` | Footer metadata blocks. |
| `src/ProMarkdown.Plugin.Math` | Inline and block math support. |
| `src/ProMarkdown.Plugin.Mermaid` | Mermaid diagram rendering. |
| `src/ProMarkdown.Plugin.SyntaxHighlighting` | Built-in code syntax highlighting. |
| `src/ProMarkdown.Plugin.TextMate` | TextMate-backed highlighting and editor integration. |
| `src/ProMarkdown.Sample` | Desktop sample that composes the library and plugins. |
| `tests/ProMarkdown.Tests` | Headless rendering and Mermaid tests. |

## NuGet packages

Published packages:

- `ProMarkdown`
- `ProMarkdown.Plugin.Alerts`
- `ProMarkdown.Plugin.CustomContainers`
- `ProMarkdown.Plugin.DefinitionLists`
- `ProMarkdown.Plugin.Figures`
- `ProMarkdown.Plugin.Footers`
- `ProMarkdown.Plugin.Math`
- `ProMarkdown.Plugin.Mermaid`
- `ProMarkdown.Plugin.SyntaxHighlighting`
- `ProMarkdown.Plugin.TextMate`

## Prerequisites

- .NET 10 SDK

## Build and test

```bash
dotnet restore ProMarkdown.slnx
dotnet build ProMarkdown.slnx
dotnet test --solution ProMarkdown.slnx
```

## Run the sample

```bash
dotnet run --project src/ProMarkdown.Sample/ProMarkdown.Sample.csproj
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
- `.github/workflows/release.yml` validates and publishes the ProMarkdown NuGet packages for tagged releases.

## License

MIT. See [`LICENSE`](LICENSE).
