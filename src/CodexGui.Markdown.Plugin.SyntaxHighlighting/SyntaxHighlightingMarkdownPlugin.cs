using CodexGui.Markdown.Services;
using Markdig.Syntax;

namespace CodexGui.Markdown.Plugin.SyntaxHighlighting;

public sealed class SyntaxHighlightingMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.AddBlockRenderingPlugin(new SyntaxHighlightingCodeBlockRenderingPlugin());
    }
}

internal sealed class SyntaxHighlightingCodeBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => 5;

    public bool CanRender(Block block) => block is CodeBlock;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not CodeBlock codeBlock)
        {
            return false;
        }

        var languageHint = codeBlock is FencedCodeBlock fencedCode ? fencedCode.Info : null;
        var code = MarkdownCodeBlockRendering.NormalizeCode(codeBlock.Lines.ToString());
        var inlines = MarkdownBuiltInSyntaxHighlighting.CreateHighlightedInlines(code, languageHint);
        var lineCount = string.IsNullOrEmpty(code) ? 0 : code.Split('\n', StringSplitOptions.None).Length;
        var metaText = lineCount == 1 ? "1 line • Built-in" : $"{lineCount} lines • Built-in";
        var surface = MarkdownCodeBlockRendering.CreateSurface(
            codeBlock,
            code,
            inlines,
            languageHint,
            metaText,
            context.RenderContext,
            textForeground: MarkdownCodeBlockRendering.DefaultCodeTextForeground);

        context.AddBlockControl(surface.Control, surface.HitTestHandler);
        return true;
    }
}
