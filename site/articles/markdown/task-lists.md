---
title: "Interactive Task Lists"
---

# Interactive Task Lists

Task-list markers render as checkboxes. They are display-only by default so selection and scrolling remain predictable. To make them interactive, set both `IsTaskListInteractive` and `TaskListToggleCommand`.

```xml
<markdown:MarkdownTextBlock Markdown="{Binding DocumentMarkdown}"
                            IsTaskListInteractive="True"
                            TaskListToggleCommand="{Binding ToggleTaskCommand}"
                            TextWrapping="Wrap" />
```

The command parameter is a `MarkdownTaskListToggleRequest`:

```csharp
public ICommand ToggleTaskCommand { get; }

public DocumentViewModel()
{
    ToggleTaskCommand = new RelayCommand<MarkdownTaskListToggleRequest>(request =>
    {
        if (request is not null)
        {
            DocumentMarkdown = request.UpdatedMarkdown;
        }
    });
}
```

The example uses `RelayCommand<T>` from CommunityToolkit.Mvvm; any `ICommand` implementation works.

## Request data

| Property | Meaning |
| --- | --- |
| `SourceMarkdown` | Document before the toggle. |
| `UpdatedMarkdown` | Complete document with the task marker changed. |
| `MarkerOffset` | Zero-based source offset of the task marker. |
| `IsChecked` | New checkbox state. |
| `TaskText` | Text associated with the task item. |

The control invokes the command only when it can construct a source-safe update and `CanExecute` accepts the request. If execution is rejected, the checkbox returns to its previous visual state.

Treat `UpdatedMarkdown` as the authoritative source replacement. Assign it to the same state that supplies the control's `Markdown` binding; the resulting rerender keeps source, task controls, and external editors synchronized.
