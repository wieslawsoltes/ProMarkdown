using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdig;
using Markdig.Syntax;
using ProMarkdown.Controls;
using MarkdownInline = Markdig.Syntax.Inlines.Inline;

namespace ProMarkdown.Services;

public interface IMarkdownParsingService
{
    MarkdownParseResult Parse(string markdown);
}

public interface IMarkdownInlineRenderingService
{
    MarkdownRenderResult Render(MarkdownParseResult parseResult, MarkdownRenderContext context);
}

public delegate MarkdownVisualHitTestResult? MarkdownVisualHitTestHandler(MarkdownVisualHitTestRequest request);

public interface IMarkdownRenderController
{
    MarkdownRenderResult Render(MarkdownRenderRequest request);
}

public interface IMarkdownHitTestingService
{
    MarkdownHitTestResult? HitTest(MarkdownHitTestRequest request);

    MarkdownHitTestResult? HitTestTextPosition(MarkdownRenderResult renderResult, int textPosition);
}

public interface IMarkdownEditingService
{
    MarkdownEditorSession? ResolveSession(MarkdownEditorResolveRequest request);

    Control? CreateEditor(MarkdownEditorRenderRequest request);
}

public interface IMarkdownPlugin
{
    void Register(MarkdownPluginRegistry registry);
}

public interface IMarkdownParserPlugin
{
    int Order => 0;

    void Configure(MarkdownPipelineBuilder builder)
    {
    }

    string TransformMarkdown(string markdown) => markdown;
}

public interface IMarkdownBlockRenderingPlugin
{
    int Order => 0;

    bool CanRender(Block block);

    bool TryRender(MarkdownBlockRenderingPluginContext context);
}

public interface IMarkdownInlineRenderingPlugin
{
    int Order => 0;

    bool CanRender(MarkdownInline inline);

    bool TryRender(MarkdownInlineRenderingPluginContext context);
}

public interface IMarkdownEditorPlugin
{
    string EditorId { get; }

    MarkdownEditorFeature Feature { get; }

    int Order => 0;

    bool TryResolveTarget(MarkdownEditorResolveContext context, out MarkdownEditorTarget? target);

    Control? CreateEditor(MarkdownEditorPluginContext context);
}

public interface IMarkdownBlockTemplateProvider
{
    int Order => 0;

    IEnumerable<MarkdownBlockTemplate> GetTemplates(MarkdownBlockTemplateContext context);
}

public sealed class MarkdownPluginRegistry
{
    private readonly List<IMarkdownParserPlugin> _parserPlugins = [];
    private readonly List<IMarkdownBlockRenderingPlugin> _blockRenderingPlugins = [];
    private readonly List<IMarkdownInlineRenderingPlugin> _inlineRenderingPlugins = [];
    private readonly List<IMarkdownEditorPlugin> _editorPlugins = [];
    private readonly List<IMarkdownBlockTemplateProvider> _blockTemplateProviders = [];

    public IReadOnlyList<IMarkdownParserPlugin> ParserPlugins => _parserPlugins;

    public IReadOnlyList<IMarkdownBlockRenderingPlugin> BlockRenderingPlugins => _blockRenderingPlugins;

    public IReadOnlyList<IMarkdownInlineRenderingPlugin> InlineRenderingPlugins => _inlineRenderingPlugins;

    public IReadOnlyList<IMarkdownEditorPlugin> EditorPlugins => _editorPlugins;

    public IReadOnlyList<IMarkdownBlockTemplateProvider> BlockTemplateProviders => _blockTemplateProviders;

    public MarkdownPluginRegistry AddPlugin(IMarkdownPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        plugin.Register(this);
        return this;
    }

    public MarkdownPluginRegistry AddParserPlugin(IMarkdownParserPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _parserPlugins.Add(plugin);
        return this;
    }

    public MarkdownPluginRegistry AddBlockRenderingPlugin(IMarkdownBlockRenderingPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _blockRenderingPlugins.Add(plugin);
        return this;
    }

    public MarkdownPluginRegistry AddInlineRenderingPlugin(IMarkdownInlineRenderingPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _inlineRenderingPlugins.Add(plugin);
        return this;
    }

    public MarkdownPluginRegistry AddEditorPlugin(IMarkdownEditorPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _editorPlugins.Add(plugin);
        return this;
    }

    public MarkdownPluginRegistry AddBlockTemplateProvider(IMarkdownBlockTemplateProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _blockTemplateProviders.Add(provider);
        return this;
    }
}

public sealed class MarkdownRenderContext
{
    public required Uri? BaseUri { get; init; }

    public required double FontSize { get; init; }

    public required FontFamily FontFamily { get; init; }

    public required IBrush? Foreground { get; init; }

    public required TextWrapping TextWrapping { get; init; }

    /// <summary>Gets the semantic palette used to theme rendered Markdown content.</summary>
    public MarkdownThemePalette? ThemePalette { get; init; }

    /// <summary>Gets the image-loading policy for this render generation.</summary>
    public MarkdownImageOptions ImageOptions { get; init; } = MarkdownImageOptions.Default;

    /// <summary>Gets the image loader used by this render generation.</summary>
    public IMarkdownImageLoader ImageLoader { get; init; } = DefaultMarkdownImageLoader.Instance;

    /// <summary>Gets a token canceled when this render generation becomes stale.</summary>
    public CancellationToken CancellationToken { get; init; }

    public required double AvailableWidth { get; init; }

    public required int RenderGeneration { get; init; }

    public required MarkdownRenderResourceTracker ResourceTracker { get; init; }

    public required Func<int, bool> IsCurrentRender { get; init; }

    public MarkdownEditorState? EditorState { get; init; }

    internal Func<IDisposable>? BeginAsyncOperationCallback { get; init; }

    /// <summary>Begins asynchronous work that contributes to the host control's rendering state.</summary>
    public IDisposable BeginAsyncOperation() =>
        BeginAsyncOperationCallback?.Invoke() ?? EmptyAsyncOperation.Instance;

    /// <summary>
    /// Keeps this render generation active until a nested Markdown control completes its current render.
    /// </summary>
    /// <param name="control">The nested Markdown control to observe.</param>
    public void TrackNestedRendering(MarkdownTextBlock control)
    {
        ArgumentNullException.ThrowIfNull(control);
        ResourceTracker.Track(new NestedRenderLifetime(
            control,
            BeginAsyncOperation(),
            CancellationToken));
    }

    private sealed class NestedRenderLifetime : IDisposable
    {
        private MarkdownTextBlock? _control;
        private IDisposable? _operation;
        private CancellationTokenRegistration _parentCancellationRegistration;
        private int _completionScheduled;

        public NestedRenderLifetime(
            MarkdownTextBlock control,
            IDisposable operation,
            CancellationToken parentCancellationToken)
        {
            _control = control;
            _operation = operation;
            control.RenderCompleted += OnRenderCompleted;
            control.AttachedToVisualTree += OnAttachedToVisualTree;
            control.DetachedFromVisualTree += OnDetachedFromVisualTree;
            _parentCancellationRegistration = parentCancellationToken.Register(
                static state => ((NestedRenderLifetime)state!).CancelNestedRender(),
                this);

            TryCompleteActivity();
        }

        public void Dispose()
        {
            _parentCancellationRegistration.Dispose();
            var control = Interlocked.Exchange(ref _control, null);
            if (control is not null)
            {
                control.RenderCompleted -= OnRenderCompleted;
                control.AttachedToVisualTree -= OnAttachedToVisualTree;
                control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
                control.ReleaseNestedRenderGeneration();
            }

            Interlocked.Exchange(ref _operation, null)?.Dispose();
        }

        private void OnRenderCompleted(object? sender, EventArgs eventArgs) => TryCompleteActivity();

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs) =>
            TryCompleteActivity();

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs) =>
            TryCompleteActivity();

        private void TryCompleteActivity()
        {
            var control = _control;
            if (control is null || _operation is null || control.IsRendering)
                return;

            // Completion is deferred because an attach handler can synchronously finish one
            // generation before a later handler starts the generation that will be displayed.
            if (Interlocked.Exchange(ref _completionScheduled, 1) == 0)
                Dispatcher.UIThread.Post(CompleteDeferredActivity, DispatcherPriority.Loaded);
        }

        private void CompleteDeferredActivity()
        {
            Interlocked.Exchange(ref _completionScheduled, 0);
            var control = _control;
            if (control is null || _operation is null || control.IsRendering)
                return;

            CompleteActivity();
        }

        private void CompleteActivity()
        {
            var control = _control;
            if (control is not null)
            {
                control.RenderCompleted -= OnRenderCompleted;
                control.AttachedToVisualTree -= OnAttachedToVisualTree;
                control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            }
            Interlocked.Exchange(ref _operation, null)?.Dispose();
        }

        private void CancelNestedRender()
        {
            var control = Interlocked.Exchange(ref _control, null);
            if (control is null)
                return;
            control.RenderCompleted -= OnRenderCompleted;
            control.AttachedToVisualTree -= OnAttachedToVisualTree;
            control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            control.ReleaseNestedRenderGeneration();
            Interlocked.Exchange(ref _operation, null)?.Dispose();
        }
    }

    private sealed class EmptyAsyncOperation : IDisposable
    {
        public static EmptyAsyncOperation Instance { get; } = new();
        public void Dispose() { }
    }
}

public sealed class MarkdownBlockRenderingPluginContext
{
    private readonly Action<Control, MarkdownVisualHitTestHandler?> _addBlockControl;
    private readonly Func<string?, Uri?> _resolveUri;

    internal MarkdownBlockRenderingPluginContext(
        Block block,
        MarkdownParseResult parseResult,
        MarkdownRenderContext renderContext,
        InlineCollection output,
        int quoteDepth,
        int listDepth,
        double availableWidth,
        Action<Control, MarkdownVisualHitTestHandler?> addBlockControl,
        Func<string?, Uri?> resolveUri)
    {
        Block = block;
        ParseResult = parseResult;
        RenderContext = renderContext;
        Output = output;
        QuoteDepth = quoteDepth;
        ListDepth = listDepth;
        AvailableWidth = availableWidth;
        _addBlockControl = addBlockControl;
        _resolveUri = resolveUri;
    }

    public Block Block { get; }

    public MarkdownParseResult ParseResult { get; }

    public MarkdownRenderContext RenderContext { get; }

    public InlineCollection Output { get; }

    public int QuoteDepth { get; }

    public int ListDepth { get; }

    public double AvailableWidth { get; }

    public void AddBlockControl(Control control, MarkdownVisualHitTestHandler? hitTestHandler = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        _addBlockControl(control, hitTestHandler);
    }

    public Uri? ResolveUri(string? url) => _resolveUri(url);

    public void TrackResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        RenderContext.ResourceTracker.Track(resource);
    }

    public bool IsCurrentRender() => RenderContext.IsCurrentRender(RenderContext.RenderGeneration);
}

public sealed class MarkdownInlineRenderingPluginContext
{
    private readonly Action<Avalonia.Controls.Documents.Inline> _addInline;
    private readonly Action<Control, MarkdownVisualHitTestHandler?> _addInlineControl;
    private readonly Func<string?, Uri?> _resolveUri;

    internal MarkdownInlineRenderingPluginContext(
        MarkdownInline inline,
        MarkdownParseResult parseResult,
        MarkdownRenderContext renderContext,
        InlineCollection output,
        int quoteDepth,
        int listDepth,
        Action<Avalonia.Controls.Documents.Inline> addInline,
        Action<Control, MarkdownVisualHitTestHandler?> addInlineControl,
        Func<string?, Uri?> resolveUri)
    {
        Inline = inline;
        ParseResult = parseResult;
        RenderContext = renderContext;
        Output = output;
        QuoteDepth = quoteDepth;
        ListDepth = listDepth;
        _addInline = addInline;
        _addInlineControl = addInlineControl;
        _resolveUri = resolveUri;
    }

    public MarkdownInline Inline { get; }

    public MarkdownParseResult ParseResult { get; }

    public MarkdownRenderContext RenderContext { get; }

    public InlineCollection Output { get; }

    public int QuoteDepth { get; }

    public int ListDepth { get; }

    public void AddInline(Avalonia.Controls.Documents.Inline inline)
    {
        ArgumentNullException.ThrowIfNull(inline);
        _addInline(inline);
    }

    public void AddInlineControl(Control control, MarkdownVisualHitTestHandler? hitTestHandler = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        _addInlineControl(control, hitTestHandler);
    }

    public Uri? ResolveUri(string? url) => _resolveUri(url);

    public void TrackResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        RenderContext.ResourceTracker.Track(resource);
    }

    public bool IsCurrentRender() => RenderContext.IsCurrentRender(RenderContext.RenderGeneration);
}

public sealed class MarkdownRenderRequest
{
    public string? Markdown { get; init; }

    public required MarkdownRenderContext Context { get; init; }
}

public sealed class MarkdownParseResult(
    MarkdownDocument document,
    string originalMarkdown,
    string parsedMarkdown,
    IReadOnlyDictionary<MarkdownObject, MarkdownObject?>? parentMap = null)
{
    public static MarkdownParseResult Empty { get; } = new(
        new MarkdownDocument(),
        string.Empty,
        string.Empty,
        new Dictionary<MarkdownObject, MarkdownObject?>());

    public MarkdownDocument Document { get; } = document;

    public string OriginalMarkdown { get; } = originalMarkdown;

    public string ParsedMarkdown { get; } = parsedMarkdown;

    public IReadOnlyDictionary<MarkdownObject, MarkdownObject?> ParentMap { get; } =
        parentMap ?? new Dictionary<MarkdownObject, MarkdownObject?>();

    public bool UsesOriginalSourceSpans => string.Equals(OriginalMarkdown, ParsedMarkdown, StringComparison.Ordinal);

    public bool TryGetParent(MarkdownObject markdownObject, out MarkdownObject? parent)
    {
        ArgumentNullException.ThrowIfNull(markdownObject);
        return ParentMap.TryGetValue(markdownObject, out parent);
    }
}

public readonly record struct MarkdownSourceSpan(int Start, int Length)
{
    public static MarkdownSourceSpan Empty { get; } = new(-1, 0);

    public int End => Length > 0 ? Start + Length - 1 : Start;

    public int EndExclusive => Length > 0 ? Start + Length : Start;

    public bool IsEmpty => Start < 0 || Length <= 0;

    public bool Contains(int position) => !IsEmpty && position >= Start && position < EndExclusive;

    public string Slice(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (IsEmpty || Start >= sourceText.Length)
        {
            return string.Empty;
        }

        var start = Math.Max(Start, 0);
        var length = Math.Min(Length, sourceText.Length - start);
        return length <= 0 ? string.Empty : sourceText.Substring(start, length);
    }

    public static MarkdownSourceSpan FromMarkdig(SourceSpan sourceSpan)
    {
        return sourceSpan.IsEmpty ? Empty : new MarkdownSourceSpan(sourceSpan.Start, sourceSpan.Length);
    }
}

public enum MarkdownEditorFeature
{
    Paragraph,
    Heading,
    TextStyle,
    List,
    Table,
    YamlFrontMatter,
    Alert,
    CustomContainer,
    Figure,
    DefinitionList,
    Abbreviation,
    Footer,
    Code,
    LinkReference,
    Footnote,
    Math,
    Mermaid
}

public enum MarkdownEditorPresentationMode
{
    Inline,
    Card
}

public sealed class MarkdownEditorPreferences
{
    private readonly Dictionary<MarkdownEditorFeature, string> _preferredEditors = [];

    public IReadOnlyDictionary<MarkdownEditorFeature, string> PreferredEditors => _preferredEditors;

    public MarkdownEditorPreferences PreferEditor(MarkdownEditorFeature feature, string editorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorId);
        _preferredEditors[feature] = editorId;
        return this;
    }

    public MarkdownEditorPreferences ClearPreference(MarkdownEditorFeature feature)
    {
        _preferredEditors.Remove(feature);
        return this;
    }

    public bool TryGetPreferredEditor(MarkdownEditorFeature feature, out string? editorId)
    {
        return _preferredEditors.TryGetValue(feature, out editorId);
    }

    public MarkdownEditorPreferences Clone()
    {
        var clone = new MarkdownEditorPreferences();
        foreach (var pair in _preferredEditors)
        {
            clone._preferredEditors[pair.Key] = pair.Value;
        }

        return clone;
    }
}

public sealed class MarkdownBlockTemplate(
    string templateId,
    string label,
    MarkdownEditorFeature feature,
    string markdown,
    string? description = null)
{
    public string TemplateId { get; } = templateId;

    public string Label { get; } = label;

    public MarkdownEditorFeature Feature { get; } = feature;

    public string Markdown { get; } = markdown;

    public string? Description { get; } = description;
}

public sealed class MarkdownBlockTemplateContext
{
    public required MarkdownObject Node { get; init; }

    public required MarkdownParseResult ParseResult { get; init; }

    public required MarkdownRenderContext RenderContext { get; init; }

    public required MarkdownEditorSession Session { get; init; }

    public required MarkdownEditorPreferences Preferences { get; init; }
}

public sealed class MarkdownEditorResolveRequest
{
    public required MarkdownHitTestResult HitTestResult { get; init; }

    public required MarkdownEditorPreferences Preferences { get; init; }
}

public sealed class MarkdownEditorResolveContext
{
    public required MarkdownHitTestResult HitTestResult { get; init; }

    public MarkdownObject Node => HitTestResult.AstNode.Node;

    public MarkdownParseResult ParseResult => HitTestResult.ParseResult;

    public MarkdownEditorPreferences Preferences { get; init; } = new();

    public IEnumerable<MarkdownObject> EnumerateSelfAndAncestors()
    {
        for (MarkdownObject? current = Node; current is not null;)
        {
            yield return current;
            if (!ParseResult.TryGetParent(current, out current))
            {
                yield break;
            }
        }
    }

    public bool TryFindAncestor<TMarkdownObject>(out TMarkdownObject? markdownObject, out int depth)
        where TMarkdownObject : MarkdownObject
    {
        depth = 0;
        foreach (var candidate in EnumerateSelfAndAncestors())
        {
            if (candidate is TMarkdownObject typed)
            {
                markdownObject = typed;
                return true;
            }

            depth++;
        }

        markdownObject = null;
        return false;
    }

    public bool TryFindAncestor(Func<MarkdownObject, bool> predicate, out MarkdownObject? markdownObject, out int depth)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        depth = 0;
        foreach (var candidate in EnumerateSelfAndAncestors())
        {
            if (predicate(candidate))
            {
                markdownObject = candidate;
                return true;
            }

            depth++;
        }

        markdownObject = null;
        return false;
    }
}

public sealed class MarkdownEditorTarget(
    MarkdownEditorFeature feature,
    MarkdownAstNodeInfo astNode,
    int matchDepth,
    string title)
{
    public MarkdownEditorFeature Feature { get; } = feature;

    public MarkdownAstNodeInfo AstNode { get; } = astNode;

    public MarkdownSourceSpan SourceSpan => AstNode.SourceSpan;

    public int MatchDepth { get; } = matchDepth;

    public string Title { get; } = title;
}

public sealed class MarkdownEditorSession(
    string editorId,
    MarkdownEditorFeature feature,
    MarkdownSourceSpan sourceSpan,
    string title,
    string nodeKind)
{
    public string EditorId { get; } = editorId;

    public MarkdownEditorFeature Feature { get; } = feature;

    public MarkdownSourceSpan SourceSpan { get; } = sourceSpan;

    public string Title { get; } = title;

    public string NodeKind { get; } = nodeKind;
}

public sealed class MarkdownEditorRenderRequest
{
    public required MarkdownObject Node { get; init; }

    public required MarkdownParseResult ParseResult { get; init; }

    public required MarkdownRenderContext RenderContext { get; init; }

    public required MarkdownEditorSession Session { get; init; }

    public required MarkdownEditorPreferences Preferences { get; init; }

    public required double AvailableWidth { get; init; }

    public required Action<string> CommitReplacement { get; init; }

    public required Action<MarkdownBlockTemplate, string> InsertBlockBefore { get; init; }

    public required Action<MarkdownBlockTemplate, string> InsertBlockAfter { get; init; }

    public required Action RemoveBlock { get; init; }

    public required Action CancelEdit { get; init; }
}

public sealed class MarkdownEditorPluginContext
{
    private readonly Action<string> _commitReplacement;
    private readonly Action<MarkdownBlockTemplate, string> _insertBlockBefore;
    private readonly Action<MarkdownBlockTemplate, string> _insertBlockAfter;
    private readonly Action _removeBlock;
    private readonly Action _cancelEdit;

    internal MarkdownEditorPluginContext(MarkdownEditorRenderRequest request, IReadOnlyList<MarkdownBlockTemplate> blockTemplates)
    {
        Node = request.Node;
        ParseResult = request.ParseResult;
        RenderContext = request.RenderContext;
        Session = request.Session;
        Preferences = request.Preferences;
        AvailableWidth = request.AvailableWidth;
        BlockTemplates = blockTemplates;
        _commitReplacement = request.CommitReplacement;
        _insertBlockBefore = request.InsertBlockBefore;
        _insertBlockAfter = request.InsertBlockAfter;
        _removeBlock = request.RemoveBlock;
        _cancelEdit = request.CancelEdit;
    }

    public MarkdownObject Node { get; }

    public MarkdownParseResult ParseResult { get; }

    public MarkdownRenderContext RenderContext { get; }

    public MarkdownEditorSession Session { get; }

    public MarkdownEditorPreferences Preferences { get; }

    public MarkdownEditorFeature Feature => Session.Feature;

    public MarkdownEditorPresentationMode PresentationMode =>
        RenderContext.EditorState?.PresentationMode ?? MarkdownEditorPresentationMode.Inline;

    public MarkdownSourceSpan SourceSpan => Session.SourceSpan;

    public string Markdown => ParseResult.OriginalMarkdown;

    public string SourceText => SourceSpan.Slice(Markdown);

    public double AvailableWidth { get; }

    public IReadOnlyList<MarkdownBlockTemplate> BlockTemplates { get; }

    public void CommitReplacement(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        _commitReplacement(markdown);
    }

    public void InsertBlockBefore(MarkdownBlockTemplate template, string currentBlockMarkdown)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(currentBlockMarkdown);
        _insertBlockBefore(template, currentBlockMarkdown);
    }

    public void InsertBlockAfter(MarkdownBlockTemplate template, string currentBlockMarkdown)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(currentBlockMarkdown);
        _insertBlockAfter(template, currentBlockMarkdown);
    }

    public void RemoveBlock() => _removeBlock();

    public void CancelEdit() => _cancelEdit();

    public void TrackResource(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        RenderContext.ResourceTracker.Track(resource);
    }
}

public sealed class MarkdownEditorState
{
    public required IMarkdownEditingService EditingService { get; init; }

    public required MarkdownEditorPreferences Preferences { get; init; }

    public MarkdownEditorSession? ActiveSession { get; init; }

    public required MarkdownEditorPresentationMode PresentationMode { get; init; }

    public required Action<MarkdownEditorSession, string> CommitReplacement { get; init; }

    public required Action<MarkdownEditorSession, MarkdownBlockTemplate, string> InsertBlockBefore { get; init; }

    public required Action<MarkdownEditorSession, MarkdownBlockTemplate, string> InsertBlockAfter { get; init; }

    public required Action<MarkdownEditorSession> RemoveBlock { get; init; }

    public required Action CancelEdit { get; init; }
}

public enum MarkdownRenderedElementKind
{
    Text,
    LineBreak,
    InlineControl,
    BlockControl
}

public sealed class MarkdownAstNodeInfo(MarkdownObject node, MarkdownSourceSpan sourceSpan, int line, int column)
{
    public MarkdownObject Node { get; } = node;

    public MarkdownSourceSpan SourceSpan { get; } = sourceSpan;

    public int Line { get; } = line;

    public int Column { get; } = column;

    public string NodeKind => Node.GetType().Name;
}

public sealed class MarkdownTextSourceMapEntry(
    int renderedTextStart,
    int renderedTextLength,
    MarkdownAstNodeInfo astNode,
    MarkdownRenderedElementKind elementKind)
{
    public int RenderedTextStart { get; } = renderedTextStart;

    public int RenderedTextLength { get; } = renderedTextLength;

    public int RenderedTextEndExclusive => RenderedTextStart + RenderedTextLength;

    public MarkdownAstNodeInfo AstNode { get; } = astNode;

    public MarkdownRenderedElementKind ElementKind { get; } = elementKind;

    public bool Contains(int textPosition) => textPosition >= RenderedTextStart && textPosition < RenderedTextEndExclusive;
}

public sealed class MarkdownVisualSourceMapEntry(
    Control control,
    MarkdownAstNodeInfo astNode,
    MarkdownRenderedElementKind elementKind,
    MarkdownVisualHitTestHandler? hitTestHandler)
{
    public Control Control { get; } = control;

    public MarkdownAstNodeInfo AstNode { get; } = astNode;

    public MarkdownRenderedElementKind ElementKind { get; } = elementKind;

    public MarkdownVisualHitTestHandler? HitTestHandler { get; } = hitTestHandler;
}

public sealed class MarkdownRenderMap(
    IReadOnlyList<MarkdownTextSourceMapEntry> textEntries,
    IReadOnlyDictionary<Control, MarkdownVisualSourceMapEntry> visualEntries)
{
    public static MarkdownRenderMap Empty { get; } = new MarkdownRenderMap(
        Array.Empty<MarkdownTextSourceMapEntry>(),
        new Dictionary<Control, MarkdownVisualSourceMapEntry>());

    public IReadOnlyList<MarkdownTextSourceMapEntry> TextEntries { get; } = textEntries;

    public IReadOnlyDictionary<Control, MarkdownVisualSourceMapEntry> VisualEntries { get; } = visualEntries;

    public bool TryGetTextEntry(int textPosition, out MarkdownTextSourceMapEntry? entry)
    {
        foreach (var candidate in TextEntries)
        {
            if (candidate.Contains(textPosition))
            {
                entry = candidate;
                return true;
            }
        }

        entry = null;
        return false;
    }

    public bool TryGetVisualEntry(Control control, out MarkdownVisualSourceMapEntry? entry)
    {
        return VisualEntries.TryGetValue(control, out entry);
    }
}

public sealed class MarkdownHitTestRequest
{
    public required Control Host { get; init; }

    public required Point Point { get; init; }

    public required TextLayout? TextLayout { get; init; }

    public required Thickness Padding { get; init; }

    public required MarkdownRenderResult RenderResult { get; init; }
}

public sealed class MarkdownVisualHitTestRequest
{
    public required Control Host { get; init; }

    public required Control HitVisual { get; init; }

    public required Control Control { get; init; }

    public required Point HostPoint { get; init; }

    public required Point LocalPoint { get; init; }

    public required MarkdownRenderResult RenderResult { get; init; }

    public required MarkdownVisualSourceMapEntry DefaultEntry { get; init; }
}

public sealed class MarkdownVisualHitTestResult
{
    public MarkdownAstNodeInfo? AstNode { get; init; }

    public MarkdownRenderedElementKind? ElementKind { get; init; }

    public MarkdownSourceSpan? SourceSpan { get; init; }

    public IReadOnlyList<Rect> LocalHighlightRects { get; init; } = Array.Empty<Rect>();
}

public sealed class MarkdownHitTestResult(
    MarkdownAstNodeInfo astNode,
    MarkdownRenderedElementKind elementKind,
    MarkdownParseResult parseResult,
    int? renderedTextPosition = null,
    Control? visual = null,
    IReadOnlyList<Rect>? highlightRects = null,
    MarkdownSourceSpan? sourceSpanOverride = null)
{
    public MarkdownAstNodeInfo AstNode { get; } = astNode;

    public MarkdownRenderedElementKind ElementKind { get; } = elementKind;

    public MarkdownParseResult ParseResult { get; } = parseResult;

    public int? RenderedTextPosition { get; } = renderedTextPosition;

    public Control? Visual { get; } = visual;

    public IReadOnlyList<Rect> HighlightRects { get; } = highlightRects ?? Array.Empty<Rect>();

    public MarkdownSourceSpan? SourceSpanOverride { get; } = sourceSpanOverride;

    public MarkdownSourceSpan SourceSpan => SourceSpanOverride ?? AstNode.SourceSpan;

    public Rect? HighlightBounds
    {
        get
        {
            if (HighlightRects.Count == 0)
            {
                return null;
            }

            var bounds = HighlightRects[0];
            for (var index = 1; index < HighlightRects.Count; index++)
            {
                bounds = bounds.Union(HighlightRects[index]);
            }

            return bounds;
        }
    }

    public string MatchedSourceText => SourceSpan.Slice(ParseResult.UsesOriginalSourceSpans ? ParseResult.OriginalMarkdown : ParseResult.ParsedMarkdown);
}

public sealed class MarkdownRenderResult(
    InlineCollection inlines,
    MarkdownRenderResourceTracker resourceTracker,
    MarkdownParseResult parseResult,
    MarkdownRenderMap renderMap)
{
    public InlineCollection Inlines { get; } = inlines;

    public MarkdownRenderResourceTracker ResourceTracker { get; } = resourceTracker;

    public MarkdownParseResult ParseResult { get; } = parseResult;

    public MarkdownRenderMap RenderMap { get; } = renderMap;

    public static MarkdownRenderResult Empty(MarkdownRenderResourceTracker resourceTracker)
    {
        return new MarkdownRenderResult(new InlineCollection(), resourceTracker, MarkdownParseResult.Empty, MarkdownRenderMap.Empty);
    }
}

public sealed class MarkdownRenderResourceTracker : IDisposable
{
    private readonly object _gate = new();
    private List<IDisposable> _resources = [];
    private bool _disposed;

    public void Track(IDisposable resource)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                resource.Dispose();
                return;
            }

            _resources.Add(resource);
        }
    }

    public void Dispose()
    {
        List<IDisposable> resources;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            resources = _resources;
            _resources = [];
        }

        foreach (var resource in resources)
        {
            resource.Dispose();
        }
    }
}

internal sealed class MarkdownRenderedElementInfo(MarkdownAstNodeInfo astNode, MarkdownRenderedElementKind kind)
{
    public MarkdownAstNodeInfo AstNode { get; } = astNode;

    public MarkdownRenderedElementKind Kind { get; } = kind;
}

internal static class MarkdownRenderedElementMetadata
{
    private static readonly AttachedProperty<MarkdownRenderedElementInfo?> ElementInfoProperty =
        AvaloniaProperty.RegisterAttached<MarkdownRenderedElementMetadataHost, AvaloniaObject, MarkdownRenderedElementInfo?>(
            "ElementInfo");
    private static readonly AttachedProperty<MarkdownVisualHitTestHandler?> VisualHitTestHandlerProperty =
        AvaloniaProperty.RegisterAttached<MarkdownRenderedElementMetadataHost, AvaloniaObject, MarkdownVisualHitTestHandler?>(
            "VisualHitTestHandler");
    private static readonly AttachedProperty<bool> IsTextAdornmentProperty =
        AvaloniaProperty.RegisterAttached<MarkdownRenderedElementMetadataHost, AvaloniaObject, bool>(
            "IsTextAdornment");
    private static readonly AttachedProperty<bool> StretchesToDocumentWidthProperty =
        AvaloniaProperty.RegisterAttached<MarkdownRenderedElementMetadataHost, AvaloniaObject, bool>(
            "StretchesToDocumentWidth");

    public static void SetElementInfo(AvaloniaObject target, MarkdownRenderedElementInfo? elementInfo)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(ElementInfoProperty, elementInfo);
    }

    public static MarkdownRenderedElementInfo? GetElementInfo(AvaloniaObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(ElementInfoProperty);
    }

    public static void SetVisualHitTestHandler(AvaloniaObject target, MarkdownVisualHitTestHandler? hitTestHandler)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(VisualHitTestHandlerProperty, hitTestHandler);
    }

    public static MarkdownVisualHitTestHandler? GetVisualHitTestHandler(AvaloniaObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(VisualHitTestHandlerProperty);
    }

    public static void SetIsTextAdornment(AvaloniaObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsTextAdornmentProperty, value);
    }

    public static bool GetIsTextAdornment(AvaloniaObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsTextAdornmentProperty);
    }

    public static void SetStretchesToDocumentWidth(AvaloniaObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(StretchesToDocumentWidthProperty, value);
    }

    public static bool GetStretchesToDocumentWidth(AvaloniaObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(StretchesToDocumentWidthProperty);
    }

    public static MarkdownRenderedElementInfo? CreateElementInfo(MarkdownObject? markdownObject, MarkdownRenderedElementKind kind)
    {
        if (markdownObject is null)
        {
            return null;
        }

        var sourceSpan = MarkdownSourceSpan.FromMarkdig(markdownObject.Span);
        return new MarkdownRenderedElementInfo(
            new MarkdownAstNodeInfo(markdownObject, sourceSpan, markdownObject.Line, markdownObject.Column),
            kind);
    }

    public static MarkdownAstNodeInfo? CreateAstNodeInfo(MarkdownObject? markdownObject)
    {
        return CreateElementInfo(markdownObject, MarkdownRenderedElementKind.Text)?.AstNode;
    }
}

internal static class MarkdownRenderMapBuilder
{
    public static MarkdownRenderMap Build(InlineCollection inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);

        var textEntries = new List<MarkdownTextSourceMapEntry>();
        var visualEntries = new Dictionary<Control, MarkdownVisualSourceMapEntry>();
        var renderedTextPosition = 0;

        foreach (var inline in inlines)
        {
            CollectInline(inline, inheritedInfo: null, textEntries, visualEntries, ref renderedTextPosition);
        }

        return textEntries.Count == 0 && visualEntries.Count == 0
            ? MarkdownRenderMap.Empty
            : new MarkdownRenderMap(textEntries, visualEntries);
    }

    private static void CollectInline(
        Avalonia.Controls.Documents.Inline inline,
        MarkdownRenderedElementInfo? inheritedInfo,
        List<MarkdownTextSourceMapEntry> textEntries,
        Dictionary<Control, MarkdownVisualSourceMapEntry> visualEntries,
        ref int renderedTextPosition)
    {
        var currentInfo = MarkdownRenderedElementMetadata.GetElementInfo(inline) ?? inheritedInfo;

        switch (inline)
        {
            case Run run:
                AppendTextEntry(run.Text, currentInfo, textEntries, ref renderedTextPosition);
                break;
            case LineBreak:
                AppendTextEntry("\n", currentInfo, textEntries, ref renderedTextPosition);
                break;
            case InlineUIContainer inlineControl:
                if (inlineControl.Child is { } child && currentInfo is not null && !visualEntries.ContainsKey(child))
                {
                    visualEntries.Add(child, new MarkdownVisualSourceMapEntry(
                        child,
                        currentInfo.AstNode,
                        currentInfo.Kind,
                        MarkdownRenderedElementMetadata.GetVisualHitTestHandler(child)));
                }

                renderedTextPosition++;
                break;
            case Span span:
                foreach (var childInline in span.Inlines)
                {
                    CollectInline(childInline, currentInfo, textEntries, visualEntries, ref renderedTextPosition);
                }

                break;
        }
    }

    private static void AppendTextEntry(
        string? text,
        MarkdownRenderedElementInfo? elementInfo,
        List<MarkdownTextSourceMapEntry> textEntries,
        ref int renderedTextPosition)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (elementInfo is not null)
        {
            textEntries.Add(new MarkdownTextSourceMapEntry(
                renderedTextPosition,
                text.Length,
                elementInfo.AstNode,
                elementInfo.Kind));
        }

        renderedTextPosition += text.Length;
    }
}

internal sealed class MarkdownRenderedElementMetadataHost;
