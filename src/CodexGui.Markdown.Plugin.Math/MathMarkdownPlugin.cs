using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexGui.Markdown.Services;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;
using MarkdownInline = Markdig.Syntax.Inlines.Inline;

namespace CodexGui.Markdown.Plugin.Math;

public sealed class MathMarkdownPlugin : IMarkdownPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddBlockRenderingPlugin(new MathBlockRenderingPlugin())
            .AddInlineRenderingPlugin(new MathInlineRenderingPlugin())
            .AddBlockTemplateProvider(new MathMarkdownBlockTemplateProvider())
            .AddEditorPlugin(new MathBlockMarkdownEditorPlugin())
            .AddEditorPlugin(new MathInlineMarkdownEditorPlugin());
    }
}

public static class MathMarkdownEditorIds
{
    public const string Block = "math-block-editor";
    public const string Inline = "math-inline-editor";
}

internal sealed class MathBlockRenderingPlugin : IMarkdownBlockRenderingPlugin
{
    public int Order => -30;

    public bool CanRender(Block block) => block is MathBlock;

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Block is not MathBlock mathBlock)
        {
            return false;
        }

        var math = MathMarkdownSyntax.NormalizeBlockSource(mathBlock.Lines.ToString());
        if (string.IsNullOrWhiteSpace(math))
        {
            return true;
        }

        var view = MarkdownMathRendering.CreateBlockView(
            MarkdownMathParser.ParseBlock(math),
            context.RenderContext);
        context.AddBlockControl(MathMarkdownSurfaceFactory.CreateBlockCallout(view));
        return true;
    }
}

internal sealed class MathInlineRenderingPlugin : IMarkdownInlineRenderingPlugin
{
    public int Order => -30;

    public bool CanRender(MarkdownInline inline) => inline is MathInline;

    public bool TryRender(MarkdownInlineRenderingPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Inline is not MathInline mathInline)
        {
            return false;
        }

        var math = MathMarkdownSyntax.NormalizeInlineSource(mathInline.Content.ToString());
        if (string.IsNullOrWhiteSpace(math))
        {
            return true;
        }

        context.AddInlineControl(
            MarkdownMathRendering.CreateInlineView(
                MarkdownMathParser.ParseInline(math),
                context.RenderContext));
        return true;
    }
}

internal static class MathMarkdownSyntax
{
    public static string NormalizeLineEndings(string? source)
    {
        return (source ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    public static string NormalizeBlockText(string? source)
    {
        return NormalizeLineEndings(source).TrimEnd('\n');
    }

    public static string NormalizeInlineText(string? source)
    {
        return NormalizeLineEndings(source).Trim();
    }

    public static string NormalizeBlockSource(string? source)
    {
        return NormalizeBlockText(source);
    }

    public static string NormalizeInlineSource(string? source)
    {
        return NormalizeInlineText(source);
    }

    public static string BuildMathBlock(string? expression)
    {
        var normalizedExpression = NormalizeBlockText(expression);
        return $"$$\n{normalizedExpression}\n$$";
    }

    public static string BuildInlineMath(string? expression)
    {
        var normalizedExpression = NormalizeInlineText(expression)
            .Replace("$", "\\$", StringComparison.Ordinal);
        return $"${normalizedExpression}$";
    }
}

internal static class MathMarkdownSurfaceFactory
{
    private static readonly IBrush SurfaceBorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#7C3AED"));
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#F5F3FF"));
    private static readonly IBrush SubtitleBrush = new SolidColorBrush(Color.Parse("#6E6E6E"));

    public static Control CreateBlockCallout(Control body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var contentPanel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12),
            Children =
            {
                new TextBlock
                {
                    Text = "Math",
                    FontWeight = FontWeight.SemiBold,
                    Foreground = AccentBrush,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Block formula",
                    FontSize = 12,
                    Foreground = SubtitleBrush,
                    TextWrapping = TextWrapping.Wrap
                },
                body
            }
        };

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        layout.Children.Add(new Border
        {
            Width = 4,
            Background = AccentBrush
        });

        Grid.SetColumn(contentPanel, 1);
        layout.Children.Add(contentPanel);

        return new Border
        {
            Background = Background,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = layout
        };
    }
}
