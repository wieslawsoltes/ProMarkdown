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
| `ImageOptions` | Allowed image sources, byte and pixel limits, and remote timeout. |
| `ImageLoader` | Image-loading implementation. Defaults to `DefaultMarkdownImageLoader.Instance`. |
| `RenderController` | Parsing and rendering pipeline. Supply a custom instance to activate plugins. |
| `HitTestingService` | Maps pointer or rendered-text positions back to Markdown source. |
| `EditingService` | Resolves source elements to editor plugins and creates editor controls. |
| `EditorPreferences` | Selects a preferred editor ID for each editor feature. |
| `EditorPresentationMode` | Displays editors inline with content or in a card surface. |
| `IsEditingEnabled` | Allows `TryBeginEdit` to start an editor session. |
| `IsTaskListInteractive` | Allows rendered task checkboxes to accept input when a command is present. |
| `TaskListToggleCommand` | Receives a `MarkdownTaskListToggleRequest` containing updated source. |

`RenderController`, `HitTestingService`, `EditingService`, `EditorPreferences`, and `ImageLoader` reject null assignments. Changing rendering, editing, text, theme, image configuration, or layout properties triggers the appropriate rerender or layout refresh. See [Image Loading and Security](image-loading/) before displaying untrusted Markdown.

## Render state

The control exposes these render snapshots and activity properties:

- `LastRenderResult` — rendered Avalonia inlines, parse result, source maps, and resource tracker
- `LastParseResult` — the Markdig document, original source, transformed source, and parent map
- `LastRenderMap` — mappings from rendered text and visual controls to Markdown AST nodes
- `ActiveEditorSession` — the editor currently replacing a rendered element, or null
- `IsRendering` — true while the current render generation has synchronous or registered asynchronous work

These values are null before the first non-empty attached render and after detaching from the visual tree. Treat them as snapshots: a Markdown, width, theme, or service change can replace them.

`RenderCompleted` fires when all work registered for the current generation has completed. A new render cancels the previous generation; stale asynchronous completions cannot change the current generation's state. Detaching the control also cancels active work and disposes render resources.

## Methods, commands, and events

| Member | Use |
| --- | --- |
| `HitTestMarkdown(Point)` | Resolve a control-relative point to source and AST metadata. |
| `TryBeginEdit(Point)` | Hit test a point and begin an editor session if a plugin supports it. |
| `TryBeginEdit(MarkdownHitTestResult)` | Begin editing from a previously resolved hit. |
| `CancelEdit()` | End the active session with the `UserRequested` reason. |
| `CopySelectionCommand` | Copy the active rendered-text selection. Its command state follows `CanCopyDocumentSelection`. |
| `SelectAllCommand` | Select every rendered document segment. |
| `SelectionChanged` | Observe document-wide selection changes across text and rich blocks. |
| `RenderCompleted` | Observe completion of the active render generation. |
| `MarkdownEdited` | Observe replacement, insertion, or removal and synchronize persisted source. |
| `MarkdownEditCanceled` | Observe cancellation caused by the user, a Markdown change, or disabled editing. |

Editing is opt-in and gesture-neutral. Set `IsEditingEnabled`, then call `TryBeginEdit` from the pointer, keyboard, or command interaction that fits your application. See [Source-Aware Editing](editing/).

## Selection and clipboard

`MarkdownTextBlock` extends `SelectableTextBlock` with document-wide selection across rendered text and rich block controls. Pointer coordinates are translated through the visual tree, so selection remains aligned when the control is hosted in a scrolled view.

Use `CanCopyDocumentSelection`, `CopySelectionCommand`, and `SelectAllCommand` for menus, toolbars, and keyboard routing. The command state updates whenever the document selection changes.

`MarkdownSelection` also provides imperative helpers:

```csharp
string selected = MarkdownSelection.GetSelectedText(markdownView);
string document = MarkdownSelection.GetDocumentText(markdownView);

await MarkdownSelection.CopyAsync(markdownView);
await MarkdownSelection.CopyDocumentTextAsync(markdownView);
```

`GetDocumentText()` and `CopyDocumentTextAsync()` operate on the complete rendered document without changing the active selection.

## Inherited text behavior

The renderer reads `FontFamily`, `FontSize`, `Foreground`, and `TextWrapping` from the control. Standard text layout and inherited styling still come from `SelectableTextBlock`; ProMarkdown owns the cross-segment selection model and its clipboard commands.
