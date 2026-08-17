using CodexGui.Markdown.Services;
using Markdig.Extensions.Alerts;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.Alerts;

public sealed class AlertsMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new AlertBlockRenderingPlugin())
            .AddBlockTemplateProvider(new AlertBlockTemplateProvider())
            .AddEditorPlugin(new AlertBlockEditorPlugin());
    }
}

public static class AlertsMarkdownEditorIds
{
    public const string Block = "alert-block-editor";
}

internal sealed class AlertBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => -25;

    public bool CanRender(Block block) => block is AlertBlock;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not AlertBlock alertBlock)
        {
            return false;
        }

        var document = MarkdownAlertParser.Parse(alertBlock, context.ParseResult);
        context.AddBlockControl(MarkdownAlertRendering.CreateBlockView(document, context.RenderContext));
        return true;
    }
}
