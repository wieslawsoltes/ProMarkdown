using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace CodexGui.Markdown.Services;

public sealed class MarkdownParsingService : IMarkdownParsingService
{
    private readonly IReadOnlyList<IMarkdownParserPlugin> _parserPlugins;
    private readonly MarkdownPipeline _markdownPipeline;

    public MarkdownParsingService(IEnumerable<IMarkdownParserPlugin>? parserPlugins = null)
    {
        _parserPlugins = (parserPlugins ?? [])
            .OrderBy(static plugin => plugin.Order)
            .ToArray();

        var builder = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UseAdvancedExtensions()
            .UseEmojiAndSmiley()
            .UseSmartyPants()
            .Use(new YamlFrontMatterExtension
            {
                AllowInMiddleOfDocument = true
            });

        foreach (var parserPlugin in _parserPlugins)
        {
            parserPlugin.Configure(builder);
        }

        _markdownPipeline = builder.Build();
    }

    public MarkdownParseResult Parse(string markdown)
    {
        var transformedMarkdown = markdown;
        foreach (var parserPlugin in _parserPlugins)
        {
            transformedMarkdown = parserPlugin.TransformMarkdown(transformedMarkdown);
        }

        var document = Markdig.Markdown.Parse(transformedMarkdown, _markdownPipeline);
        return new MarkdownParseResult(
            document,
            markdown,
            transformedMarkdown,
            BuildParentMap(document));
    }

    private static IReadOnlyDictionary<MarkdownObject, MarkdownObject?> BuildParentMap(MarkdownDocument document)
    {
        var parentMap = new Dictionary<MarkdownObject, MarkdownObject?>();
        CollectChildren(document, parent: null, parentMap);
        return parentMap;
    }

    private static void CollectChildren(MarkdownObject markdownObject, MarkdownObject? parent, Dictionary<MarkdownObject, MarkdownObject?> parentMap)
    {
        if (!parentMap.TryAdd(markdownObject, parent))
        {
            return;
        }

        switch (markdownObject)
        {
            case ContainerBlock containerBlock:
                foreach (var child in containerBlock)
                {
                    CollectChildren(child, markdownObject, parentMap);
                }

                break;
            case LeafBlock { Inline: { } inlineContainer } leafBlock:
                CollectInline(inlineContainer, leafBlock, parentMap);
                break;
        }
    }

    private static void CollectInline(Markdig.Syntax.Inlines.Inline inline, MarkdownObject parent, Dictionary<MarkdownObject, MarkdownObject?> parentMap)
    {
        HashSet<MarkdownObject> visitedSiblings = [];
        for (Markdig.Syntax.Inlines.Inline? current = inline; current is not null && visitedSiblings.Add(current); current = current.NextSibling)
        {
            parentMap.TryAdd(current, parent);
            if (current is Markdig.Syntax.Inlines.ContainerInline containerInline)
            {
                CollectInlineChildren(containerInline, current, parentMap);
            }
        }
    }

    private static void CollectInlineChildren(Markdig.Syntax.Inlines.ContainerInline containerInline, MarkdownObject parent, Dictionary<MarkdownObject, MarkdownObject?> parentMap)
    {
        HashSet<MarkdownObject> visitedSiblings = [];
        for (Markdig.Syntax.Inlines.Inline? current = containerInline.FirstChild; current is not null && visitedSiblings.Add(current); current = current.NextSibling)
        {
            if (!parentMap.TryAdd(current, parent))
            {
                continue;
            }

            if (current is Markdig.Syntax.Inlines.ContainerInline nestedContainer)
            {
                CollectInlineChildren(nestedContainer, current, parentMap);
            }
        }
    }
}
