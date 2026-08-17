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

| Package | Version | Downloads | Description |
| --- | --- | --- | --- |
| [`ProMarkdown`](https://www.nuget.org/packages/ProMarkdown) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown?logo=nuget) | Core Avalonia Markdown rendering and editing primitives. |
| [`ProMarkdown.Plugin.Alerts`](https://www.nuget.org/packages/ProMarkdown.Plugin.Alerts) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.Alerts?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.Alerts?logo=nuget) | Alert block parsing, rendering, and editing. |
| [`ProMarkdown.Plugin.CustomContainers`](https://www.nuget.org/packages/ProMarkdown.Plugin.CustomContainers) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.CustomContainers?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.CustomContainers?logo=nuget) | Custom-container parsing, rendering, and editing. |
| [`ProMarkdown.Plugin.DefinitionLists`](https://www.nuget.org/packages/ProMarkdown.Plugin.DefinitionLists) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.DefinitionLists?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.DefinitionLists?logo=nuget) | Definition-list parsing, rendering, and editing. |
| [`ProMarkdown.Plugin.Figures`](https://www.nuget.org/packages/ProMarkdown.Plugin.Figures) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.Figures?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.Figures?logo=nuget) | Figure block parsing, rendering, and editing. |
| [`ProMarkdown.Plugin.Footers`](https://www.nuget.org/packages/ProMarkdown.Plugin.Footers) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.Footers?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.Footers?logo=nuget) | Footer block parsing, rendering, and editing. |
| [`ProMarkdown.Plugin.Math`](https://www.nuget.org/packages/ProMarkdown.Plugin.Math) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.Math?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.Math?logo=nuget) | Inline and block math parsing, rendering, and editing. |
| [`ProMarkdown.Plugin.Mermaid`](https://www.nuget.org/packages/ProMarkdown.Plugin.Mermaid) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.Mermaid?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.Mermaid?logo=nuget) | Mermaid diagram rendering. |
| [`ProMarkdown.Plugin.SyntaxHighlighting`](https://www.nuget.org/packages/ProMarkdown.Plugin.SyntaxHighlighting) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.SyntaxHighlighting?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.SyntaxHighlighting?logo=nuget) | Built-in syntax highlighting for code blocks. |
| [`ProMarkdown.Plugin.TextMate`](https://www.nuget.org/packages/ProMarkdown.Plugin.TextMate) | ![NuGet Version](https://img.shields.io/nuget/v/ProMarkdown.Plugin.TextMate?logo=nuget) | ![NuGet Downloads](https://img.shields.io/nuget/dt/ProMarkdown.Plugin.TextMate?logo=nuget) | TextMate-backed highlighting and editor integration. |

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

The published documentation is available at [wieslawsoltes.github.io/ProMarkdown](https://wieslawsoltes.github.io/ProMarkdown/). The Lunet source lives under `site/`.

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
