---
title: "MarkdownTextBlock"
---

# MarkdownTextBlock

`ProMarkdown.Controls.MarkdownTextBlock` is the primary control for application views. It derives from `SelectableTextBlock`, renders a source-mapped Avalonia inline tree, and coordinates selection, links, editing, task lists, and render-resource lifetime.

## Typical XAML

```xml
<markdown:MarkdownTextBlock Markdown="{Binding DocumentMarkdown}"
                            BaseUri="{Binding DocumentBaseUri}"
                            FontSize="14"
                            Padding="16"
                            TextWrapping="Wrap"
                            ThemePalette="{Binding MarkdownPalette}" />
```

Place wrapped documents in a vertically scrolling container and leave horizontal scrolling disabled. The control observes its own bounds and effective viewport so tables, code blocks, and embedded controls receive a useful available width.

## Markdown properties

| Property | Purpose |
| --- | --- |
| `Markdown` | Source text. Changing it reparses and rerenders the document. |
| `BaseUri` | Base for relative links and images. |
| `ThemePalette` | Semantic document brushes. If null, a built-in palette is selected from `Foreground`. |
| `RenderController` | Parsing and rendering pipeline. Supply a custom instance to activate plugins. |
| `HitTestingService` | Maps pointer or rendered-text positions back to Markdown source. |
| `EditingService` | Resolves source elements to editor plugins and creates editor controls. |
| `EditorPreferences` | Selects a preferred editor ID for each editor feature. |
| `EditorPresentationMode` | Displays editors inline with content or in a card surface. |
| `IsEditingEnabled` | Allows `TryBeginEdit` to start an editor session. |
| `IsTaskListInteractive` | Allows rendered task checkboxes to accept input when a command is present. |
| `TaskListToggleCommand` | Receives a `MarkdownTaskListToggleRequest` containing updated source. |

`RenderController`, `HitTestingService`, `EditingService`, and `EditorPreferences` reject null assignments. Changing rendering, editing, text, theme, or layout properties triggers the appropriate rerender or layout refresh.

## Render state

After a successful render, the control exposes:

- `LastRenderResult` — rendered Avalonia inlines, parse result, source maps, and resource tracker
- `LastParseResult` — the Markdig document, original source, transformed source, and parent map
- `LastRenderMap` — mappings from rendered text and visual controls to Markdown AST nodes
- `ActiveEditorSession` — the editor currently replacing a rendered element, or null

These values are null before the first non-empty attached render and after detaching from the visual tree. Treat them as snapshots: a Markdown, width, theme, or service change can replace them.

## Methods and events

| Member | Use |
| --- | --- |
| `HitTestMarkdown(Point)` | Resolve a control-relative point to source and AST metadata. |
| `TryBeginEdit(Point)` | Hit test a point and begin an editor session if a plugin supports it. |
| `TryBeginEdit(MarkdownHitTestResult)` | Begin editing from a previously resolved hit. |
| `CancelEdit()` | End the active session with the `UserRequested` reason. |
| `MarkdownEdited` | Observe replacement, insertion, or removal and synchronize persisted source. |
| `MarkdownEditCanceled` | Observe cancellation caused by the user, a Markdown change, or disabled editing. |

Editing is opt-in and gesture-neutral. Set `IsEditingEnabled`, then call `TryBeginEdit` from the pointer, keyboard, or command interaction that fits your application. See [Source-Aware Editing](editing/).

## Inherited text behavior

The renderer reads `FontFamily`, `FontSize`, `Foreground`, and `TextWrapping` from the control. Selection and copy behavior come from `SelectableTextBlock`; ProMarkdown adds document-segment normalization so selection continues across rich block controls.
