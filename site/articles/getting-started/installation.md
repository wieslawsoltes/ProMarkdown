---
title: "Installation"
---

# Installation

ProMarkdown targets .NET 10 and Avalonia 12. Add the core package to the application project that owns the Markdown view:

```bash
dotnet add package ProMarkdown
```

The core package contains `MarkdownTextBlock`, the default Markdig pipeline, rendering and editing services, source maps, hit testing, themes, and the extension contracts. It is sufficient for CommonMark plus the advanced Markdig features enabled by the default parser.

## Optional packages

Install optional packages only for the syntax or editor integration your application uses:

| Capability | Package |
| --- | --- |
| GitHub-style alerts | `ProMarkdown.Plugin.Alerts` |
| Custom containers | `ProMarkdown.Plugin.CustomContainers` |
| Definition lists | `ProMarkdown.Plugin.DefinitionLists` |
| Figures and captions | `ProMarkdown.Plugin.Figures` |
| Footer blocks | `ProMarkdown.Plugin.Footers` |
| Inline and block math | `ProMarkdown.Plugin.Math` |
| Mermaid diagrams | `ProMarkdown.Plugin.Mermaid` |
| Built-in code highlighting | `ProMarkdown.Plugin.SyntaxHighlighting` |
| TextMate-backed code editing | `ProMarkdown.Plugin.TextMate` |

For example:

```bash
dotnet add package ProMarkdown.Plugin.Alerts
dotnet add package ProMarkdown.Plugin.SyntaxHighlighting
```

Installing a plugin package does not activate it. Create the plugin instances and pass the same plugin set to the render controller and editing service. The [Plugin Ecosystem](../markdown/plugin-ecosystem/) guide shows the complete setup.

## XAML namespace

Import the controls namespace in an Avalonia view:

```xml
xmlns:markdown="using:ProMarkdown.Controls"
```

The service and model APIs live in `ProMarkdown.Services`:

```csharp
using ProMarkdown.Controls;
using ProMarkdown.Services;
```

Continue with [Render Your First Document](first-document/).
