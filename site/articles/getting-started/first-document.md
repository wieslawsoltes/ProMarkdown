---
title: "Render Your First Document"
---

# Render Your First Document

`MarkdownTextBlock` is the primary application-facing control. Bind its `Markdown` property and let it use the built-in render, editing, and hit-testing services.

## XAML

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:markdown="using:ProMarkdown.Controls">
  <ScrollViewer VerticalScrollBarVisibility="Auto">
    <markdown:MarkdownTextBlock Markdown="{Binding DocumentMarkdown}"
                                FontSize="14"
                                Padding="16"
                                TextWrapping="Wrap" />
  </ScrollViewer>
</UserControl>
```

`MarkdownTextBlock` derives from Avalonia's `SelectableTextBlock`, so rendered text supports ordinary selection and copy behavior. The control also honors inherited text properties such as `FontFamily`, `FontSize`, `Foreground`, `Padding`, and `TextWrapping`.

## C#

```csharp
using Avalonia.Controls;
using Avalonia.Media;
using ProMarkdown.Controls;

var document = new MarkdownTextBlock
{
    Markdown = "# Hello\n\nRendered with **ProMarkdown**.",
    FontSize = 14,
    Padding = new Avalonia.Thickness(16),
    TextWrapping = TextWrapping.Wrap
};
```

Assigning a new value to `Markdown` reparses and rerenders the document. A null, empty, or whitespace-only value clears the rendered content.

## Relative links and images

Set `BaseUri` when document links or images use relative URLs:

```xml
<markdown:MarkdownTextBlock Markdown="{Binding DocumentMarkdown}"
                            BaseUri="https://docs.example.com/guide/"
                            TextWrapping="Wrap" />
```

Relative targets are resolved against `BaseUri`. Absolute `http`, `https`, and `mailto` links continue to work without it. Pointer clicks open links through Avalonia's top-level launcher, while pointer drags remain available for text selection.

## What the default pipeline supports

The default parser enables precise source locations, Markdig advanced extensions, emoji and smileys, SmartyPants typography, and YAML front matter. ProMarkdown adds layout normalization, source mapping, text selection, link handling, and optional source-aware editors around that parser.

Continue with the [User Guide](../markdown/) to configure plugins, editing, themes, hit testing, and interactive task lists.
