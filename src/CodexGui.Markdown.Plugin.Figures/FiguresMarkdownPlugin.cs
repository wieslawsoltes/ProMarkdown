using CodexGui.Markdown.Services;
using Markdig.Extensions.Figures;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Figures;

public sealed class FiguresMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new FigureBlockRenderingPlugin())
            .AddBlockTemplateProvider(new FigureBlockTemplateProvider())
            .AddEditorPlugin(new FigureBlockEditorPlugin());
    }
}

public static class FiguresMarkdownEditorIds
{
    public const string Block = "figure-block-editor";
}

internal sealed class FigureBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => -18;

    public bool CanRender(Block block) => block is Figure;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not Figure figure)
        {
            return false;
        }

        var document = MarkdownFigureParser.Parse(figure, context.ParseResult);
        context.AddBlockControl(MarkdownFigureRendering.CreateBlockView(document, context.RenderContext));
        return true;
    }
}
