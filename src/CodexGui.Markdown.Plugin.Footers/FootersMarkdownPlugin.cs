using CodexGui.Markdown.Services;
using Markdig.Extensions.Footers;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Footers;

public sealed class FootersMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new FooterBlockRenderingPlugin())
            .AddBlockTemplateProvider(new FooterBlockTemplateProvider())
            .AddEditorPlugin(new FooterBlockEditorPlugin());
    }
}

public static class FooterMarkdownEditorIds
{
    public const string Block = "footer-block-editor";
}

internal sealed class FooterBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => 30;

    public bool CanRender(Block block) => block is FooterBlock;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not FooterBlock footerBlock)
        {
            return false;
        }

        var document = MarkdownFooterParser.Parse(footerBlock, context.ParseResult);
        context.AddBlockControl(MarkdownFooterRendering.CreateBlockView(document, context.RenderContext));
        return true;
    }
}
