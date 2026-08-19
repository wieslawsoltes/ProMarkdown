---
title: "Controls API"
---

# Controls API

Namespace: `ProMarkdown.Controls`

## MarkdownTextBlock

```csharp
public sealed class MarkdownTextBlock : SelectableTextBlock
```

Primary Markdown rendering and editing control. See the [control guide](../markdown/markdown-text-block/) for lifecycle and usage.

### Avalonia properties

Each CLR property below has a corresponding public styled- or direct-property identifier where shown.

| CLR property | Type | Styled property | Notes |
| --- | --- | --- | --- |
| `Markdown` | `string?` | `MarkdownProperty` | Source text. |
| `BaseUri` | `Uri?` | `BaseUriProperty` | Base for relative links and images. |
| `IsEditingEnabled` | `bool` | `IsEditingEnabledProperty` | Permits editor sessions. |
| `EditorPresentationMode` | `MarkdownEditorPresentationMode` | `EditorPresentationModeProperty` | `Inline` by default. |
| `ThemePalette` | `MarkdownThemePalette?` | `ThemePaletteProperty` | Semantic render palette. |
| `ImageOptions` | `MarkdownImageOptions` | `ImageOptionsProperty` | Source policy and resource limits; defaults to `MarkdownImageOptions.Default`. |
| `CanCopyDocumentSelection` | `bool` | `CanCopyDocumentSelectionProperty` | Read-only document-selection command state. |
| `IsRendering` | `bool` | `IsRenderingProperty` | Read-only activity state for the current render generation. |
| `IsTaskListInteractive` | `bool` | `IsTaskListInteractiveProperty` | Enables task input when a command exists. |
| `TaskListToggleCommand` | `ICommand?` | `TaskListToggleCommandProperty` | Receives `MarkdownTaskListToggleRequest`. |

### Service and state properties

| Property | Type | Access |
| --- | --- | --- |
| `RenderController` | `IMarkdownRenderController` | get/set |
| `HitTestingService` | `IMarkdownHitTestingService` | get/set |
| `EditingService` | `IMarkdownEditingService` | get/set |
| `EditorPreferences` | `MarkdownEditorPreferences` | get/set |
| `ImageLoader` | `IMarkdownImageLoader` | get/set |
| `CopySelectionCommand` | `ICommand` | get |
| `SelectAllCommand` | `ICommand` | get |
| `LastRenderResult` | `MarkdownRenderResult?` | get |
| `LastParseResult` | `MarkdownParseResult?` | get |
| `LastRenderMap` | `MarkdownRenderMap?` | get |
| `ActiveEditorSession` | `MarkdownEditorSession?` | get |

### Methods

```csharp
MarkdownHitTestResult? HitTestMarkdown(Point point);
bool TryBeginEdit(Point point);
bool TryBeginEdit(MarkdownHitTestResult hitTestResult);
void CancelEdit();
```

### Events

```csharp
event EventHandler<MarkdownEditedEventArgs>? MarkdownEdited;
event EventHandler<MarkdownEditCanceledEventArgs>? MarkdownEditCanceled;
event EventHandler? SelectionChanged;
event EventHandler? RenderCompleted;
```

`SelectionChanged` is raised for document-wide selection changes, including selection spanning rich block controls. `RenderCompleted` is raised after synchronous rendering and every asynchronous operation registered for the active generation have completed. Replaced or detached generations are canceled and cannot complete the current generation.

## MarkdownSelection

Namespace: `ProMarkdown.Services`

```csharp
public static bool CanCopy(MarkdownTextBlock? control);
public static Task CopyAsync(MarkdownTextBlock? control);
public static void SelectAll(MarkdownTextBlock? control);
public static string GetSelectedText(MarkdownTextBlock? control);
public static string GetDocumentText(MarkdownTextBlock? control);
public static Task CopyDocumentTextAsync(MarkdownTextBlock? control);
```

The document-text methods include every rendered segment without changing the active selection. Null controls return empty text or a completed no-op task as appropriate.

### IMarkdownInputBoundary

Implement this marker interface on nested controls that own their routed pointer and keyboard input. Document-wide selection does not intercept input originating within an input boundary. `MermaidDiagramControl` implements it so diagram links, keyboard navigation, and text selection in its error surface remain independent from the surrounding document selection.

## MarkdownEditedEventArgs

Created after the control commits a source change.

```csharp
public MarkdownEditedEventArgs(
    MarkdownEditorSession session,
    string replacementMarkdown,
    string updatedMarkdown,
    int revealStart,
    int revealLength,
    MarkdownEditOperation operation);
```

Read-only properties: `Session`, `ReplacementMarkdown`, `UpdatedMarkdown`, `RevealStart`, `RevealLength`, and `Operation`.

## MarkdownEditOperation

```csharp
public enum MarkdownEditOperation
{
    Replace,
    InsertBlockBefore,
    InsertBlockAfter,
    RemoveBlock
}
```

## MarkdownEditCanceledEventArgs

```csharp
public MarkdownEditCanceledEventArgs(
    MarkdownEditorSession session,
    MarkdownEditCancellationReason reason);
```

Read-only properties: `Session` and `Reason`.

## MarkdownEditCancellationReason

```csharp
public enum MarkdownEditCancellationReason
{
    UserRequested,
    MarkdownChanged,
    EditingDisabled
}
```
