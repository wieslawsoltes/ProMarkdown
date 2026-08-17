using CodexGui.Markdown.Services;
using Markdig.Extensions.DefinitionLists;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.DefinitionLists;

public sealed class DefinitionListMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new DefinitionListBlockRenderingPlugin())
            .AddBlockTemplateProvider(new DefinitionListBlockTemplateProvider())
            .AddEditorPlugin(new DefinitionListBlockEditorPlugin());
    }
}

public static class DefinitionListMarkdownEditorIds
{
    public const string Block = "definition-list-block-editor";
}

internal sealed class DefinitionListBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => -20;

    public bool CanRender(Block block) => block is DefinitionList;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not DefinitionList definitionList)
        {
            return false;
        }

        var document = MarkdownDefinitionListParser.Parse(definitionList, context.ParseResult);
        context.AddBlockControl(MarkdownDefinitionListRendering.CreateBlockView(document, context.RenderContext));
        return true;
    }
}
