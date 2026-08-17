---
title: "Editing API"
---

# Editing API

Namespace: `ProMarkdown.Services`

These models select an editor for a hit-tested AST node, construct its UI, and commit a source operation. See [Editing](../markdown/editing/) for the application workflow and [Editor Plugins](../extensions/editor-plugins/) for implementation guidance.

## Features and presentation

### MarkdownEditorFeature

Values: `Paragraph`, `Heading`, `TextStyle`, `List`, `Table`, `YamlFrontMatter`, `Alert`, `CustomContainer`, `Figure`, `DefinitionList`, `Abbreviation`, `Footer`, `Code`, `LinkReference`, `Footnote`, `Math`, and `Mermaid`.

### MarkdownEditorPresentationMode

- `Inline` replaces the rendered block with an editor styled to fit the document flow.
- `Card` uses a bordered editor card with a header and footer.

## Preferences and editor selection

### MarkdownEditorPreferences

```csharp
public IReadOnlyDictionary<MarkdownEditorFeature, string> PreferredEditors { get; }
public MarkdownEditorPreferences PreferEditor(MarkdownEditorFeature feature, string editorId);
public MarkdownEditorPreferences ClearPreference(MarkdownEditorFeature feature);
public bool TryGetPreferredEditor(MarkdownEditorFeature feature, out string? editorId);
public MarkdownEditorPreferences Clone();
```

`PreferEditor` and `ClearPreference` are fluent. `Clone` makes an independent copy.

### MarkdownEditorResolveRequest

Required init-only properties: `MarkdownHitTestResult HitTestResult` and `MarkdownEditorPreferences Preferences`.

### MarkdownEditorResolveContext

```csharp
public required MarkdownHitTestResult HitTestResult { get; init; }
public MarkdownObject Node { get; }
public MarkdownParseResult ParseResult { get; }
public MarkdownEditorPreferences Preferences { get; init; }
public IEnumerable<MarkdownObject> EnumerateSelfAndAncestors();
public bool TryFindAncestor<TMarkdownObject>(
    out TMarkdownObject? markdownObject,
    out int depth) where TMarkdownObject : MarkdownObject;
public bool TryFindAncestor(
    Func<MarkdownObject, bool> predicate,
    out MarkdownObject? markdownObject,
    out int depth);
```

Depth zero is the hit node. Editor plugins use depth to prefer the nearest eligible ancestor.

### MarkdownEditorTarget

```csharp
public MarkdownEditorTarget(
    MarkdownEditorFeature feature,
    MarkdownAstNodeInfo astNode,
    int matchDepth,
    string title);
```

Read-only properties: `Feature`, `AstNode`, derived `SourceSpan`, `MatchDepth`, and `Title`.

### MarkdownEditorSession

```csharp
public MarkdownEditorSession(
    string editorId,
    MarkdownEditorFeature feature,
    MarkdownSourceSpan sourceSpan,
    string title,
    string nodeKind);
```

Read-only properties: `EditorId`, `Feature`, `SourceSpan`, `Title`, and `NodeKind`.

## Editor rendering

### MarkdownEditorRenderRequest

All properties are required init-only:

| Property | Type |
| --- | --- |
| `Node` | `MarkdownObject` |
| `ParseResult` | `MarkdownParseResult` |
| `RenderContext` | `MarkdownRenderContext` |
| `Session` | `MarkdownEditorSession` |
| `Preferences` | `MarkdownEditorPreferences` |
| `AvailableWidth` | `double` |
| `CommitReplacement` | `Action<string>` |
| `InsertBlockBefore` | `Action<MarkdownBlockTemplate, string>` |
| `InsertBlockAfter` | `Action<MarkdownBlockTemplate, string>` |
| `RemoveBlock` | `Action` |
| `CancelEdit` | `Action` |

### MarkdownEditorPluginContext

The framework constructs this context for `IMarkdownEditorPlugin.CreateEditor`.

Read-only properties: `Node`, `ParseResult`, `RenderContext`, `Session`, `Preferences`, `Feature`, `PresentationMode`, `SourceSpan`, complete `Markdown`, sliced `SourceText`, `AvailableWidth`, and `BlockTemplates`.

```csharp
public void CommitReplacement(string markdown);
public void InsertBlockBefore(MarkdownBlockTemplate template, string currentBlockMarkdown);
public void InsertBlockAfter(MarkdownBlockTemplate template, string currentBlockMarkdown);
public void RemoveBlock();
public void CancelEdit();
public void TrackResource(IDisposable resource);
```

### MarkdownEditorState

The render context consumes this state to replace an active rendered block with its editor. Its init-only properties are:

- required `EditingService`, `Preferences`, and `PresentationMode`;
- optional `ActiveSession`;
- required callbacks `CommitReplacement`, `InsertBlockBefore`, `InsertBlockAfter`, `RemoveBlock`, and `CancelEdit`.

The first four callbacks receive the current `MarkdownEditorSession`; insertion callbacks also receive the template and current block Markdown.

## Block templates

### MarkdownBlockTemplate

```csharp
public MarkdownBlockTemplate(
    string templateId,
    string label,
    MarkdownEditorFeature feature,
    string markdown,
    string? description = null);
```

The five constructor values are exposed as read-only `TemplateId`, `Label`, `Feature`, `Markdown`, and `Description` properties.

### MarkdownBlockTemplateContext

Required init-only properties: `Node`, `ParseResult`, `RenderContext`, `Session`, and `Preferences`.

## Built-in editor IDs

`MarkdownBuiltInEditorIds` exposes stable string constants for `Paragraph`, `Heading`, `List`, `Table`, `Code`, `YamlFrontMatter`, `Abbreviation`, `LinkReference`, and `Footnote`. Use these constants with `MarkdownEditorPreferences.PreferEditor`; do not copy their literal string values.

Optional packages expose their own IDs in the [Plugin Package API](package-api/).

## MarkdownEditorUiFactory

Reusable Avalonia UI helpers for custom editor plugins.

Brush properties: `EditorBackground`, `InputBackground`, `BorderBrush`, `HeaderBackground`, `SectionBackground`, and `EditorForeground`.

### Complete method list

```csharp
public static Control CreateEditorSurface(
    MarkdownEditorPluginContext context,
    string title,
    string subtitle,
    Control body,
    Action apply,
    Action cancel,
    Control? focusTarget = null,
    bool preferInlineTextLayout = false,
    Func<string>? buildBlockMarkdownForActions = null);

public static Control CreateEditorCard(
    MarkdownEditorPluginContext context,
    string title,
    string subtitle,
    Control body,
    Action apply,
    Action cancel,
    Control? focusTarget = null,
    Func<string>? buildBlockMarkdownForActions = null);

public static void ApplyInlineParagraphStyle(TextBox textBox, double fontSize);
public static void ApplyInlineHeadingStyle(TextBox textBox, double baseFontSize, int level);
public static void ApplyInlineListItemStyle(TextBox textBox, double fontSize);
public static void ApplyInlineTableCellStyle(TextBox textBox, double fontSize);
public static void ApplyInlineMetadataStyle(TextBox textBox, double fontSize);
public static void ApplyInlineCodeStyle(TextBox textBox, double fontSize);

public static TextBlock CreateInfoText(string text);
public static TextBlock CreateFieldLabel(string text);
public static TextBox CreateTextEditor(string? text, bool acceptsReturn, double minHeight);
public static TextBox CreateCodeEditor(string? text);
public static Button CreatePrimaryButton(string text, Action onClick);
public static Button CreateSecondaryButton(string text, Action onClick);
public static Control CreateTextStyleToolbar(Func<TextBox?> resolveTextBox);
public static Control CreateCompactTextStyleToolbar(Func<TextBox?> resolveTextBox);
```

Pass `buildBlockMarkdownForActions` to surface the standard insert-before, insert-after, and remove actions supplied by block templates.

## Task-list edit request

`MarkdownTaskListToggleRequest` is the value passed to `MarkdownTextBlock.TaskListToggleCommand`:

```csharp
public MarkdownTaskListToggleRequest(
    string sourceMarkdown,
    string updatedMarkdown,
    int markerOffset,
    bool isChecked,
    string taskText);
```

The constructor values are exposed as read-only `SourceMarkdown`, `UpdatedMarkdown`, `MarkerOffset`, `IsChecked`, and `TaskText` properties. See [Task Lists](../markdown/task-lists/) for command handling.
