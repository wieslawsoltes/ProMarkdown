using CodexGui.Markdown.Services;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.CustomContainers;

public sealed class CustomContainersMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new CustomContainerBlockRenderingPlugin())
            .AddBlockTemplateProvider(new CustomContainerBlockTemplateProvider())
            .AddEditorPlugin(new CustomContainerBlockEditorPlugin());
    }
}

public static class CustomContainerMarkdownEditorIds
{
    public const string Block = "custom-container-block-editor";
}

internal sealed class CustomContainerBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => 50;

    public bool CanRender(Block block) => block is CustomContainer;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not CustomContainer customContainer)
        {
            return false;
        }

        var document = MarkdownCustomContainerParser.Parse(customContainer, context.ParseResult);
        context.AddBlockControl(MarkdownCustomContainerRendering.CreateBlockView(document, context.RenderContext));
        return true;
    }
}
