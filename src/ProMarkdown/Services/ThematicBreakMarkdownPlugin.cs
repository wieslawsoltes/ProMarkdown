using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Markdig.Syntax;

namespace ProMarkdown.Services;

internal sealed class ThematicBreakMarkdownPlugin : IMarkdownPlugin, IMarkdownBlockRenderingPlugin
{
    public int Order => -90;

    public void Register(MarkdownPluginRegistry registry) => registry.AddBlockRenderingPlugin(this);

    public bool CanRender(Block block) => block is ThematicBreakBlock;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        context.AddBlockControl(new MarkdownThematicBreak
        {
            BorderBrush = (context.RenderContext.ThemePalette ??
                           MarkdownThemePalette.Resolve(context.RenderContext.Foreground)).Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        return true;
    }
}

internal sealed class MarkdownThematicBreak : Border
{
}
