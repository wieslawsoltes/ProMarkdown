---
title: "Theming"
---

# Theming

`MarkdownThemePalette` centralizes semantic brushes for document surfaces. It covers ordinary foregrounds and borders, code, tables, marked and inserted text, links, block quotes, alerts, and syntax-highlighted tokens.

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

Changing the palette rerenders the document so plugin controls and normalized built-in content receive the new semantic brushes.

## Avalonia theme integration

Expose the current `MarkdownThemePalette` from your theme service or view model and bind it like any other view state. Keep palette construction in shared theme infrastructure rather than scattering brushes across individual Markdown views.
