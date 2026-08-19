---
title: "Theming"
---

# Theming

`MarkdownThemePalette` centralizes semantic brushes for document surfaces. It covers ordinary foregrounds and borders, code, tables, marked and inserted text, links, block quotes, alerts, syntax-highlighted tokens, math, and Mermaid diagrams.

## Built-in palettes

```csharp
markdownView.ThemePalette = MarkdownThemePalette.Light;
// or
markdownView.ThemePalette = MarkdownThemePalette.Dark;
```

When `ThemePalette` is null, `MarkdownTextBlock` calls `MarkdownThemePalette.Resolve(Foreground)`. A light solid foreground selects the dark palette; other foregrounds select the light palette.

For applications with an explicit theme mode, assigning `Light` or `Dark` is more predictable than inference:

```csharp
markdownView.ThemePalette = isDarkTheme
    ? MarkdownThemePalette.Dark
    : MarkdownThemePalette.Light;
```

## Custom palette

Palette properties use semantic roles rather than renderer-specific control names:

```csharp
using Avalonia.Media;
using ProMarkdown.Services;

var palette = new MarkdownThemePalette
{
    IsDark = true,
    Foreground = new SolidColorBrush(Color.Parse("#F4F4F5")),
    MutedForeground = new SolidColorBrush(Color.Parse("#A1A1AA")),
    Accent = new SolidColorBrush(Color.Parse("#8B5CF6")),
    HyperlinkForeground = new SolidColorBrush(Color.Parse("#A78BFA")),
    Surface = new SolidColorBrush(Color.Parse("#18181B")),
    SurfaceRaised = new SolidColorBrush(Color.Parse("#27272A")),
    Border = new SolidColorBrush(Color.Parse("#3F3F46"))
};

markdownView.ThemePalette = palette;
```

Any property you do not initialize keeps its light-palette default. For a fully coherent dark design, set all roles used by your enabled features or start from the built-in dark values.

## Palette roles

- **Document:** `Foreground`, `MutedForeground`, `Accent`, `HyperlinkForeground`
- **Surfaces:** `Surface`, `SurfaceRaised`, `Border`, `QuoteBorder`
- **Code and tables:** `InlineCodeBackground`, `CodeHeaderBackground`, `TableHeaderBackground`, `TableAlternateRowBackground`
- **Text semantics:** `MarkedTextBackground`, `InsertedTextForeground`
- **Alerts:** Note, Tip, Important, Warning, and Caution accent/background pairs
- **Syntax tokens:** keyword, type, string, comment, number, property, tag, attribute, and punctuation foregrounds

`Foreground` is the ordinary document color and is also used for neutral Mermaid strokes and arrows. `Accent` is for emphasized UI and semantic accents. Hyperlinks use `HyperlinkForeground`, so applications that expect links to follow their accent should assign both roles to the same brush.

Changing the palette rerenders the document. Core rendering, math, syntax highlighting, Mermaid, and the other official plugins consume the palette directly while constructing their controls. ProMarkdown does not walk the completed visual tree to replace arbitrary colors, so controls created by third-party plugins retain the brushes selected by those plugins.

This direct palette model also means official plugin controls receive the correct colors before they are attached, avoiding a visible post-render color correction.

## Avalonia theme integration

Expose the current `MarkdownThemePalette` from your theme service or view model and bind it like any other view state. Keep palette construction in shared theme infrastructure rather than scattering brushes across individual Markdown views.

When an application changes theme at runtime, replace the palette instance or assign the appropriate themed resource. `MarkdownTextBlock` rerenders for the new palette, and Mermaid regenerates its SVG using the new semantic colors. A custom `IMermaidSvgRenderer` receives a background-thread-safe palette snapshot in each `MermaidSvgRenderRequest`.
