---
title: "Editor Plugins"
---

# Editor Plugins

An `IMarkdownEditorPlugin` resolves a hit AST node to an editable source span, then creates the Avalonia control used for that session.

## Contract

```csharp
public interface IMarkdownEditorPlugin
{
    string EditorId { get; }
    MarkdownEditorFeature Feature { get; }
    int Order => 0;
    bool TryResolveTarget(
        MarkdownEditorResolveContext context,
        out MarkdownEditorTarget? target);
    Control? CreateEditor(MarkdownEditorPluginContext context);
}
```

Use a stable `EditorId`; applications can persist it in `MarkdownEditorPreferences`. Choose the closest existing `MarkdownEditorFeature`, since preferences and insertion templates are grouped by feature.

## Resolve a target

```csharp
using Markdig.Syntax;
using ProMarkdown.Services;

public bool TryResolveTarget(
    MarkdownEditorResolveContext context,
    out MarkdownEditorTarget? target)
{
    target = null;
    if (!context.TryFindAncestor<ParagraphBlock>(out var paragraph, out var depth) ||
        paragraph is null)
    {
        return false;
    }

    var span = MarkdownSourceSpan.FromMarkdig(paragraph.Span);
    if (span.IsEmpty)
    {
        return false;
    }

    var nodeInfo = new MarkdownAstNodeInfo(
        paragraph,
        span,
        paragraph.Line,
        paragraph.Column);

    target = new MarkdownEditorTarget(
        MarkdownEditorFeature.Paragraph,
        nodeInfo,
        depth,
        "Product paragraph");
    return true;
}
```

`EnumerateSelfAndAncestors`, generic `TryFindAncestor`, and predicate-based `TryFindAncestor` use the parse result's parent map. `MatchDepth` should be the returned depth: the editing service favors the nearest matching node before applying editor preferences and order.

Editing is available only when the parse result uses original source spans and the target span is non-empty.

## Create an editor

```csharp
public Control? CreateEditor(MarkdownEditorPluginContext context)
{
    var textBox = MarkdownEditorUiFactory.CreateTextEditor(
        context.SourceText,
        acceptsReturn: true,
        minHeight: 120);

    string BuildMarkdown() => textBox.Text ?? string.Empty;

    return MarkdownEditorUiFactory.CreateEditorSurface(
        context,
        "Edit product paragraph",
        "Product editor",
        textBox,
        () => context.CommitReplacement(BuildMarkdown()),
        context.CancelEdit,
        textBox,
        buildBlockMarkdownForActions: BuildMarkdown);
}
```

`MarkdownEditorPluginContext` provides the current node, complete Markdown, selected source text, parse and render context, preferences, available width, presentation mode, active session, and applicable block templates.

Commit through the context rather than assigning the control's `Markdown` property. The context preserves source replacement boundaries and raises `MarkdownEdited` with the correct operation and reveal range.

## Editing actions

- `CommitReplacement(markdown)` replaces the session span.
- `InsertBlockBefore(template, currentBlockMarkdown)` preserves the current block and inserts template Markdown before it.
- `InsertBlockAfter(template, currentBlockMarkdown)` inserts after it.
- `RemoveBlock()` removes the current span with surrounding whitespace normalization.
- `CancelEdit()` ends the session without changing source.
- `TrackResource(resource)` disposes editor subscriptions or helpers with the current render.

Use `MarkdownEditorUiFactory` for consistent fields, buttons, toolbars, inline/card surfaces, and semantic brushes. A custom editor may return any Avalonia `Control`, but it remains responsible for accessible labels, focus behavior, keyboard operation, cancellation, and commit affordances.
