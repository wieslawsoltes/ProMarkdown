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

Each CLR property below has a corresponding public styled-property identifier where shown.

| CLR property | Type | Styled property | Notes |
| --- | --- | --- | --- |
| `Markdown` | `string?` | `MarkdownProperty` | Source text. |
| `BaseUri` | `Uri?` | `BaseUriProperty` | Base for relative links and images. |
| `IsEditingEnabled` | `bool` | `IsEditingEnabledProperty` | Permits editor sessions. |
| `EditorPresentationMode` | `MarkdownEditorPresentationMode` | `EditorPresentationModeProperty` | `Inline` by default. |
| `ThemePalette` | `MarkdownThemePalette?` | `ThemePaletteProperty` | Semantic render palette. |
| `IsTaskListInteractive` | `bool` | `IsTaskListInteractiveProperty` | Enables task input when a command exists. |
| `TaskListToggleCommand` | `ICommand?` | `TaskListToggleCommandProperty` | Receives `MarkdownTaskListToggleRequest`. |

### Service and state properties

| Property | Type | Access |
| --- | --- | --- |
| `RenderController` | `IMarkdownRenderController` | get/set |
| `HitTestingService` | `IMarkdownHitTestingService` | get/set |
| `EditingService` | `IMarkdownEditingService` | get/set |
| `EditorPreferences` | `MarkdownEditorPreferences` | get/set |
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
```

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
