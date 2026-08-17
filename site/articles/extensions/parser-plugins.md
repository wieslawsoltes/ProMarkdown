---
title: "Parser Plugins"
---

# Parser Plugins

`IMarkdownParserPlugin` participates before the Markdig document is created. It can configure the `MarkdownPipelineBuilder`, transform source text, or do both.

## Add a Markdig extension

Wrap a reusable Markdig extension in a ProMarkdown plugin:

```csharp
using Markdig;
using ProMarkdown.Services;

public sealed class MarkdigExtensionPlugin(IMarkdownExtension extension) : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry) =>
        registry.AddParserPlugin(new ParserPlugin(extension));

    private sealed class ParserPlugin(IMarkdownExtension extension) : IMarkdownParserPlugin
    {
        private readonly IMarkdownExtension _extension =
            extension ?? throw new ArgumentNullException(nameof(extension));

        public int Order => 0;

        public void Configure(MarkdownPipelineBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (!builder.Extensions.Contains(_extension))
            {
                builder.Extensions.Add(_extension);
            }
        }
    }
}
```

Configuring the pipeline is the preferred route for custom syntax because Markdig supplies AST spans that ProMarkdown can map back to source.

## Transform source

`TransformMarkdown` runs in ascending plugin order before Markdig parses the source:

```csharp
public string TransformMarkdown(string markdown)
{
    ArgumentNullException.ThrowIfNull(markdown);
    return markdown.Replace("(c)", "©", StringComparison.Ordinal);
}
```

Use transformation carefully. `MarkdownParseResult` retains both `OriginalMarkdown` and `ParsedMarkdown`, and `UsesOriginalSourceSpans` is false whenever they differ. `MarkdownEditingService` deliberately refuses to start an editor session in that case because AST offsets no longer reliably address the original document.

If a transform changes length or line structure, source-map hits refer to transformed source unless a rendering plugin supplies an explicit span override. Prefer a Markdig parser extension when source-aware editing is a requirement.

## Contract behavior

```csharp
public interface IMarkdownParserPlugin
{
    int Order => 0;
    void Configure(MarkdownPipelineBuilder builder) { }
    string TransformMarkdown(string markdown) => markdown;
}
```

- `Configure` is called once when `MarkdownParsingService` is constructed.
- `TransformMarkdown` is called for every parse.
- The default pipeline is configured before plugin `Configure` calls.
- Parser plugins should be deterministic and thread-safe when their owning controller is shared.

After parsing, `MarkdownParseResult.ParentMap` contains block and inline parent relationships. Renderers and editor resolvers use that map to walk from a precise hit to an enclosing extension node.
