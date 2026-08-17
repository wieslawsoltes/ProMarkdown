---
title: "Block Templates"
---

# Block Templates

`IMarkdownBlockTemplateProvider` supplies reusable Markdown blocks to active editor surfaces. Built-in editors expose these as insert-before and insert-after actions.

```csharp
using ProMarkdown.Services;

public sealed class ProductTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => 20;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(
        MarkdownBlockTemplateContext context)
    {
        yield return new MarkdownBlockTemplate(
            templateId: "product-release-note",
            label: "Release note",
            feature: MarkdownEditorFeature.Paragraph,
            markdown: "## Release note\n\nDescribe the user-visible change.",
            description: "Insert a release-note section.");
    }
}
```

Register the provider with `MarkdownPluginRegistry.AddBlockTemplateProvider`.

## Template fields

| Field | Requirement |
| --- | --- |
| `TemplateId` | Stable, non-empty identifier. Prefix it for your application or package. |
| `Label` | Short non-empty action label. |
| `Feature` | Semantic editor feature represented by the inserted block. |
| `Markdown` | Non-empty complete block source. |
| `Description` | Optional supporting text for template pickers. |

`MarkdownEditingService` ignores templates with an empty ID, label, or Markdown body. It preserves provider order and provider enumeration order for valid templates.

## Context-aware templates

`MarkdownBlockTemplateContext` contains the current AST node, parse result, render context, editor session, and preferences. Providers can vary templates by node type, current feature, theme, or selected editor:

```csharp
if (context.Session.Feature == MarkdownEditorFeature.Code)
{
    yield return new MarkdownBlockTemplate(
        "product-json-example",
        "JSON example",
        MarkdownEditorFeature.Code,
        "```json\n{\n  \"enabled\": true\n}\n```");
}
```

Keep providers deterministic and inexpensive because templates are collected when an editor control is created.

## Safe block source

Template Markdown should be complete and independently parseable. When generating fenced containers or code dynamically, choose a fence longer than matching runs in the body. Shipped figure, custom-container, Mermaid, and code editors follow this rule to avoid closing a block prematurely.

`InsertBlockBefore` and `InsertBlockAfter` normalize separation around the current block and return an updated reveal range through `MarkdownEditedEventArgs`.
