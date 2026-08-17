using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Markdig.Syntax;

namespace CodexGui.Markdown.Services;

internal sealed class ThematicBreakMarkdownPlugin : IMarkdownPlugin, IMarkdownBlockRenderingPlugin
{
    private static readonly IBrush RuleBrush =
        new ImmutableSolidColorBrush(Color.Parse("#D0D7DE"));

    public int Order => -90;

    public void Register(MarkdownPluginRegistry registry) => registry.AddBlockRenderingPlugin(this);

    public bool CanRender(Block block) => block is ThematicBreakBlock;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        context.AddBlockControl(new MarkdownThematicBreak
        {
            BorderBrush = RuleBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        return true;
    }
}

internal sealed class MarkdownThematicBreak : Border
{
}
