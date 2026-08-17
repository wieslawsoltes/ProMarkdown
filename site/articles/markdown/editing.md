---
title: "Source-Aware Editing"
---

# Source-Aware Editing

ProMarkdown edits the original Markdown span associated with a rendered element. Editors do not mutate the AST or rendered controls directly; they commit replacement Markdown, and `MarkdownTextBlock` produces the updated complete source.

## Enable and start editing

```csharp
markdownView.IsEditingEnabled = true;

private void OnPreviewPressed(object? sender, PointerPressedEventArgs args)
{
    if (!args.GetCurrentPoint(markdownView).Properties.IsLeftButtonPressed ||
        markdownView.ActiveEditorSession is not null)
    {
        return;
    }

    if (markdownView.TryBeginEdit(args.GetPosition(markdownView)))
    {
        args.Handled = true;
    }
}
```

`IsEditingEnabled` permits editing but does not impose a gesture. Applications can start an editor from a click, double click, keyboard command, context menu, or a cached `MarkdownHitTestResult`.

`TryBeginEdit` returns false when editing is disabled, the point has no source-mapped element, or no registered editor plugin accepts that element.

## Persist edits

Handle `MarkdownEdited` and update the view model or document store:

```csharp
markdownView.MarkdownEdited += (_, args) =>
{
    DocumentMarkdown = args.UpdatedMarkdown;
    SelectSource(args.RevealStart, args.RevealLength);
};
```

`MarkdownEditedEventArgs` provides:

- `Session` — selected editor ID, feature, source span, title, and AST node kind
- `ReplacementMarkdown` — replacement for the original session span
- `UpdatedMarkdown` — the complete updated document
- `RevealStart` and `RevealLength` — the range an external source editor should reveal
- `Operation` — `Replace`, `InsertBlockBefore`, `InsertBlockAfter`, or `RemoveBlock`

The control updates its own `Markdown` value before raising the event. Still update the bound source explicitly unless your binding is configured to propagate target changes.

## Cancellation

Call `CancelEdit()` to cancel the active session. `MarkdownEditCanceled` reports the session and one of these reasons:

| Reason | Cause |
| --- | --- |
| `UserRequested` | `CancelEdit()` or an editor's cancel action |
| `MarkdownChanged` | New external Markdown arrived while a session was active |
| `EditingDisabled` | `IsEditingEnabled` changed to false |

This makes document refresh and mode changes deterministic instead of applying an editor result to stale source.

## Presentation modes

`EditorPresentationMode` controls how registered editor plugins compose their UI:

- `Inline` keeps text-oriented editors close to the rendered line or block.
- `Card` uses a labeled editor surface with explicit actions.

Plugins receive the selected mode through `MarkdownEditorPluginContext.PresentationMode` and can use `MarkdownEditorUiFactory.CreateEditorSurface` to follow the built-in behavior.

## Choose a preferred editor

Multiple editor plugins may support the same feature. Use `MarkdownEditorPreferences` to select an editor ID:

```csharp
markdownView.EditorPreferences = new MarkdownEditorPreferences()
    .PreferEditor(
        MarkdownEditorFeature.Code,
        TextMateMarkdownPlugin.TextMateCodeEditorId);
```

Preferences are keyed by `MarkdownEditorFeature`. `PreferEditor` and `ClearPreference` mutate and return the same preferences instance; assign a new or cloned instance to the control when you want an immediate rerender.

Built-in editor IDs are exposed by `MarkdownBuiltInEditorIds` for paragraphs, headings, lists, tables, code, YAML front matter, abbreviations, link references, and footnotes. Optional packages expose their own IDs where selection is useful.
