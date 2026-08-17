using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ProMarkdown.Services;
using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace ProMarkdown.Plugin.Mermaid;

/// <summary>
/// Adds Mermaid fenced blocks, custom containers, templates, editing, and native SVG rendering.
/// </summary>
public sealed class MermaidMarkdownPlugin : IMarkdownPlugin
{
    /// <summary>Gets the stable editor identifier used for Mermaid diagram blocks.</summary>
    public const string MermaidEditorId = "mermaid-diagram-editor";
    private readonly MermaiderSvgRenderer _renderer = new();

    /// <summary>Registers Mermaid parsing, rendering, templates, and editing services.</summary>
    /// <param name="registry">The Markdown plugin registry to extend.</param>
    public void Register(MarkdownPluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry
            .AddParserPlugin(new MermaidParserPlugin())
            .AddBlockRenderingPlugin(new MermaidDiagramBlockRenderingPlugin(_renderer))
            .AddBlockTemplateProvider(new MermaidBlockTemplateProvider())
            .AddEditorPlugin(new MermaidDiagramEditorPlugin(_renderer));
    }
}

internal sealed class MermaidBlockTemplateProvider : IMarkdownBlockTemplateProvider
{
    public int Order => -50;

    public IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        yield return new MarkdownBlockTemplate(
            "mermaid-diagram-block",
            "Mermaid diagram",
            MarkdownEditorFeature.Mermaid,
            MermaidDiagramEditorPlugin.BuildMermaidFence("flowchart TD\n    Start[Start] --> Decide{Choice}\n    Decide -->|Yes| Ship[Ship it]\n    Decide -->|No| Revise[Revise]"),
            "Insert a Mermaid diagram block.");
    }
}

internal sealed class MermaidParserPlugin : IMarkdownParserPlugin
{
    public int Order => -100;

    public void Configure(MarkdownPipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Extensions.AddIfNotAlready<MermaidParsingExtension>(new MermaidParsingExtension());
    }
}

internal sealed class MermaidParsingExtension : IMarkdownExtension
{
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        pipeline.BlockParsers.InsertBefore<FencedCodeBlockParser>(new MermaidFencedBlockParser());

        var containerParser = new MermaidContainerParser();
        if (!pipeline.BlockParsers.InsertBefore<CustomContainerParser>(containerParser))
        {
            pipeline.BlockParsers.InsertBefore<ParagraphBlockParser>(containerParser);
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }
}

internal enum MermaidBlockSyntax
{
    CodeFence,
    CustomContainer
}

internal sealed class MermaidDiagramBlock : FencedCodeBlock
{
    public MermaidDiagramBlock(BlockParser parser, MermaidBlockSyntax syntax)
        : base(parser)
    {
        Syntax = syntax;
    }

    public MermaidBlockSyntax Syntax { get; }

    public string NormalizedInfo { get; private set; } = "mermaid";

    public string DiagramArguments { get; private set; } = string.Empty;

    public bool TryInitializeDescriptor()
    {
        if (!MermaidSyntax.TryParseDescriptor(Info, Arguments, out var normalizedInfo, out var normalizedArguments))
        {
            return false;
        }

        NormalizedInfo = normalizedInfo;
        DiagramArguments = normalizedArguments;
        return true;
    }
}

internal abstract class MermaidBlockParserBase : FencedBlockParserBase<MermaidDiagramBlock>
{
    private readonly MermaidBlockSyntax _syntax;

    protected MermaidBlockParserBase(MermaidBlockSyntax syntax, char[] openingCharacters)
    {
        _syntax = syntax;
        OpeningCharacters = openingCharacters;
        InfoPrefix = null;
    }

    protected override MermaidDiagramBlock CreateFencedBlock(BlockProcessor processor)
    {
        return new MermaidDiagramBlock(this, _syntax);
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        var result = base.TryOpen(processor);
        if (result == BlockState.None)
        {
            return result;
        }

        if (processor.NewBlocks.Count == 0 || processor.NewBlocks.Peek() is not MermaidDiagramBlock mermaidBlock)
        {
            return BlockState.None;
        }

        if (mermaidBlock.TryInitializeDescriptor())
        {
            return result;
        }

        processor.NewBlocks.Pop();
        return BlockState.None;
    }
}

internal sealed class MermaidFencedBlockParser : MermaidBlockParserBase
{
    public MermaidFencedBlockParser()
        : base(MermaidBlockSyntax.CodeFence, ['`', '~'])
    {
    }
}

internal sealed class MermaidContainerParser : MermaidBlockParserBase
{
    public MermaidContainerParser()
        : base(MermaidBlockSyntax.CustomContainer, [':'])
    {
    }
}

internal static class MermaidSyntax
{
    private static readonly HashSet<string> MermaidLanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "diagram-mermaid",
        "mmd",
        "mermaid",
        "mermaidjs"
    };

    public static bool TryParseDescriptor(
        string? info,
        string? arguments,
        out string normalizedInfo,
        out string normalizedArguments)
    {
        normalizedInfo = "mermaid";
        normalizedArguments = string.Empty;

        var normalizedDescriptor = NormalizeDescriptor(info);
        if (IsMermaidLanguage(normalizedDescriptor))
        {
            normalizedArguments = arguments?.Trim() ?? string.Empty;
            return true;
        }

        if (!string.Equals(normalizedDescriptor, "diagram", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var trimmedArguments = arguments?.Trim() ?? string.Empty;
        if (trimmedArguments.Length == 0)
        {
            return false;
        }

        var separatorIndex = trimmedArguments.IndexOfAny([' ', '\t']);
        var firstToken = separatorIndex >= 0 ? trimmedArguments[..separatorIndex] : trimmedArguments;
        if (!IsMermaidLanguage(firstToken))
        {
            return false;
        }

        normalizedArguments = separatorIndex >= 0
            ? trimmedArguments[(separatorIndex + 1)..].TrimStart()
            : string.Empty;
        return true;
    }

    public static string NormalizeCode(string source)
    {
        return source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');
    }

    private static bool IsMermaidLanguage(string? languageHint)
    {
        return MermaidLanguageAliases.Contains(NormalizeDescriptor(languageHint));
    }

    private static string NormalizeDescriptor(string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(languageHint))
        {
            return string.Empty;
        }

        var trimmed = languageHint.Trim();
        var separatorIndex = trimmed.IndexOfAny([' ', '\t', ',', ';', '{', '(']);
        var normalized = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        return normalized.Trim().Trim('.').ToLowerInvariant();
    }
}

internal sealed class MermaidDiagramBlockRenderingPlugin(MermaiderSvgRenderer renderer) : IMarkdownBlockRenderingPlugin
{
    private readonly MermaiderSvgRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    public int Order => -100;

    public bool CanRender(Block block)
    {
        return block is MermaidDiagramBlock;
    }

    public bool TryRender(MarkdownBlockRenderingPluginContext context)
    {
        if (context.Block is not MermaidDiagramBlock mermaidBlock)
        {
            return false;
        }

        var diagramSource = MermaidSyntax.NormalizeCode(mermaidBlock.Lines.ToString());
        var palette = context.RenderContext.ThemePalette ??
                      MarkdownThemePalette.Resolve(context.RenderContext.Foreground);
        var control = new MermaidDiagramControl(
            _renderer,
            diagramSource,
            palette,
            context.RenderContext.FontFamily.Name,
            context.RenderContext.FontSize);
        context.TrackResource(control);
        context.AddBlockControl(control);
        return true;
    }
}

internal sealed class MermaidDiagramEditorPlugin(MermaiderSvgRenderer renderer) : IMarkdownEditorPlugin
{
    private readonly MermaiderSvgRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    public string EditorId => MermaidMarkdownPlugin.MermaidEditorId;

    public MarkdownEditorFeature Feature => MarkdownEditorFeature.Mermaid;

    public int Order => -100;

    public bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target)
    {
        ArgumentNullException.ThrowIfNull(context);

        target = null;
        if (!context.TryFindAncestor<MermaidDiagramBlock>(out var mermaidBlock, out var depth) || mermaidBlock is null)
        {
            return false;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(mermaidBlock.Span);
        if (sourceSpan.IsEmpty)
        {
            return false;
        }

        target = new MarkdownEditorTarget(
            Feature,
            new MarkdownAstNodeInfo(mermaidBlock, sourceSpan, mermaidBlock.Line, mermaidBlock.Column),
            depth,
            "Mermaid diagram");
        return true;
    }

    public Control? CreateEditor(MarkdownEditorPluginContext context)
    {
        if (context.Node is not MermaidDiagramBlock mermaidBlock)
        {
            return null;
        }

        var sourceText = MermaidSyntax.NormalizeCode(mermaidBlock.Lines.ToString());
        var textBox = MarkdownEditorUiFactory.CreateCodeEditor(sourceText);
        if (context.PresentationMode == MarkdownEditorPresentationMode.Inline)
        {
            MarkdownEditorUiFactory.ApplyInlineCodeStyle(textBox, context.RenderContext.FontSize);
        }

        var previewHost = new Border
        {
            Background = MarkdownEditorUiFactory.SectionBackground,
            BorderBrush = MarkdownEditorUiFactory.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8)
        };

        context.TrackResource(new MermaidEditorPreviewController(
            textBox,
            previewHost,
            _renderer,
            context.RenderContext.ThemePalette ??
            MarkdownThemePalette.Resolve(context.RenderContext.Foreground),
            context.RenderContext.FontFamily.Name,
            context.RenderContext.FontSize));

        var body = new StackPanel
        {
            Spacing = context.PresentationMode == MarkdownEditorPresentationMode.Inline ? 6 : 10
        };
        if (context.PresentationMode == MarkdownEditorPresentationMode.Card)
        {
            body.Children.Add(MarkdownEditorUiFactory.CreateInfoText("Edit Mermaid source and review the live native preview below before applying the fenced diagram block."));
        }

        string BuildCurrentMarkdown() => BuildMermaidFence(textBox.Text ?? string.Empty);

        body.Children.Add(textBox);
        body.Children.Add(previewHost);

        return MarkdownEditorUiFactory.CreateEditorSurface(
            context,
            "Edit Mermaid diagram",
            "Mermaid editor",
            body,
            () => context.CommitReplacement(BuildCurrentMarkdown()),
            context.CancelEdit,
            textBox,
            buildBlockMarkdownForActions: BuildCurrentMarkdown);
    }

    internal static string BuildMermaidFence(string source)
    {
        var normalized = MermaidSyntax.NormalizeCode(source);
        var fence = normalized.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
        return $"{fence}mermaid\n{normalized}\n{fence}";
    }
}

internal sealed class MermaidEditorPreviewController : IDisposable
{
    private static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(200);
    private readonly TextBox _textBox;
    private readonly Border _previewHost;
    private readonly MermaiderSvgRenderer _renderer;
    private readonly MarkdownThemePalette _palette;
    private readonly string _fontFamily;
    private readonly double _fontSize;
    private readonly DispatcherTimer _timer;
    private MermaidDiagramControl? _current;
    private bool _isDisposed;

    public MermaidEditorPreviewController(
        TextBox textBox,
        Border previewHost,
        MermaiderSvgRenderer renderer,
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize)
    {
        _textBox = textBox;
        _previewHost = previewHost;
        _renderer = renderer;
        _palette = palette;
        _fontFamily = fontFamily;
        _fontSize = fontSize;
        _timer = new DispatcherTimer(PreviewDebounce, DispatcherPriority.Background, OnTimerTick);
        _textBox.TextChanged += OnTextChanged;
        ReplacePreview();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _textBox.TextChanged -= OnTextChanged;
        _previewHost.Child = null;
        _current?.Dispose();
        _current = null;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs args)
    {
        _timer.Stop();
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs args)
    {
        _timer.Stop();
        ReplacePreview();
    }

    private void ReplacePreview()
    {
        if (_isDisposed)
            return;

        var next = new MermaidDiagramControl(
            _renderer,
            MermaidSyntax.NormalizeCode(_textBox.Text ?? string.Empty),
            _palette,
            _fontFamily,
            _fontSize);
        var previous = _current;
        _current = next;
        _previewHost.Child = next;
        previous?.Dispose();
    }
}
