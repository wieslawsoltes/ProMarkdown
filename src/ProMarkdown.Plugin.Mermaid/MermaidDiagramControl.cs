using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows.Input;
using System.Xml;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProMarkdown.Services;
using ProMarkdown.Plugin.Mermaid.Themes;
using ShimSkiaSharp;
using Svg;
using AvaloniaSvgImage = Avalonia.Svg.Skia.SvgImage;

namespace ProMarkdown.Plugin.Mermaid;

/// <summary>Displays a bounded, sanitized, theme-aware Mermaid SVG.</summary>
[TemplatePart("PART_DiagramImage", typeof(Image), IsRequired = true)]
public sealed class MermaidDiagramControl : TemplatedControl, IMarkdownInputBoundary, IDisposable
{
    private const int MaximumErrorMessageLength = 512;
    private const double LinkClickDragThreshold = 4d;
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    public static readonly StyledProperty<MarkdownThemePalette?> ThemePaletteProperty =
        AvaloniaProperty.Register<MermaidDiagramControl, MarkdownThemePalette?>(nameof(ThemePalette));
    public static readonly DirectProperty<MermaidDiagramControl, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<MermaidDiagramControl, bool>(nameof(IsLoading), control => control._isLoading);
    public static readonly DirectProperty<MermaidDiagramControl, bool> HasImageProperty =
        AvaloniaProperty.RegisterDirect<MermaidDiagramControl, bool>(nameof(HasImage), control => control._hasImage);
    public static readonly DirectProperty<MermaidDiagramControl, bool> HasErrorProperty =
        AvaloniaProperty.RegisterDirect<MermaidDiagramControl, bool>(nameof(HasError), control => control._hasError);
    public static readonly DirectProperty<MermaidDiagramControl, string> ErrorTextProperty =
        AvaloniaProperty.RegisterDirect<MermaidDiagramControl, string>(nameof(ErrorText), control => control._errorText);
    public static readonly DirectProperty<MermaidDiagramControl, string> SourceTextProperty =
        AvaloniaProperty.RegisterDirect<MermaidDiagramControl, string>(nameof(SourceText), control => control._sourceText);
    public static readonly DirectProperty<MermaidDiagramControl, double> SourceFontSizeProperty =
        AvaloniaProperty.RegisterDirect<MermaidDiagramControl, double>(nameof(SourceFontSize), control => control._sourceFontSize);
    public static readonly DirectProperty<MermaidDiagramControl, string> RetryTextProperty =
        AvaloniaProperty.RegisterDirect<MermaidDiagramControl, string>(nameof(RetryText), control => control._retryText);
    public static readonly DirectProperty<MermaidDiagramControl, ICommand> RetryCommandProperty =
        AvaloniaProperty.RegisterDirect<MermaidDiagramControl, ICommand>(nameof(RetryCommand), control => control._retryCommand);

    private readonly IMermaidSvgRenderer _renderer;
    private readonly MermaidMarkdownPluginOptions _options;
    private readonly string _fontFamily;
    private readonly double _fontSize;
    private readonly CancellationToken _generationCancellationToken;
    private readonly Func<IDisposable> _beginAsyncOperation;
    private readonly Func<string, SvgSource> _svgSourceFactory;
    private readonly ICommand _retryCommand;
    private readonly string _sourceText;
    private readonly double _sourceFontSize;
    private readonly string _retryText;
    private IDisposable? _initialOperation;
    private Image? _imageControl;
    private SvgSource? _svgSource;
    private AvaloniaSvgImage? _svgImage;
    private CancellationTokenSource? _renderCancellation;
    private Uri? _hoverLink;
    private Point? _pendingLinkPointerOrigin;
    private Uri? _pendingLinkUri;
    private IReadOnlyList<MermaidDiagramLink> _links = [];
    private MermaidDiagramAutomationPeer? _automationPeer;
    private string _errorText;
    private int _keyboardLinkIndex;
    private int _renderVersion;
    private bool _isLoading;
    private bool _hasImage;
    private bool _hasError;
    private bool _hasCurrentPaletteImage;
    private bool _isInitialized;
    private bool _isDisposed;

    static MermaidDiagramControl()
    {
        ThemeProperty.OverrideDefaultValue<MermaidDiagramControl>(new MermaidDiagramTheme());
        ThemePaletteProperty.Changed.AddClassHandler<MermaidDiagramControl>(
            static (control, _) => control.OnThemePaletteChanged());
    }

    internal MermaidDiagramControl(
        IMermaidSvgRenderer renderer,
        string source,
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize,
        MermaidMarkdownPluginOptions options,
        CancellationToken generationCancellationToken,
        Func<IDisposable> beginAsyncOperation,
        Func<string, SvgSource>? svgSourceFactory = null)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _sourceText = source ?? throw new ArgumentNullException(nameof(source));
        _sourceFontSize = Math.Max(fontSize - 1, 12);
        _fontFamily = fontFamily ?? throw new ArgumentNullException(nameof(fontFamily));
        _fontSize = fontSize;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _generationCancellationToken = generationCancellationToken;
        _beginAsyncOperation = beginAsyncOperation ?? throw new ArgumentNullException(nameof(beginAsyncOperation));
        _svgSourceFactory = svgSourceFactory ?? CreateSvgSource;
        _errorText = options.ErrorText;
        _retryText = options.RetryText;
        _retryCommand = new RetryRenderCommand(this);
        ThemePalette = palette ?? throw new ArgumentNullException(nameof(palette));
        Foreground = palette.Foreground;
        _initialOperation = _beginAsyncOperation();
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        _isInitialized = true;
        StartRender(isThemeRefresh: false);
    }

    internal MermaidDiagramControl(
        MermaiderSvgRenderer renderer,
        string source,
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize,
        Func<string, SvgSource>? svgSourceFactory = null)
        : this(
            renderer,
            source,
            palette,
            fontFamily,
            fontSize,
            new MermaidMarkdownPluginOptions(),
            CancellationToken.None,
            static () => EmptyOperation.Instance,
            svgSourceFactory)
    {
    }

    public MarkdownThemePalette? ThemePalette
    {
        get => GetValue(ThemePaletteProperty);
        set => SetValue(ThemePaletteProperty, value);
    }

    public bool IsLoading => _isLoading;
    public bool HasImage => _hasImage;
    public bool HasError => _hasError;
    public string ErrorText => _errorText;
    public string SourceText => _sourceText;
    /// <summary>Gets the font size used to display source text when rendering fails.</summary>
    public double SourceFontSize => _sourceFontSize;
    public string RetryText => _retryText;
    public ICommand RetryCommand => _retryCommand;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _imageControl = e.NameScope.Find<Image>("PART_DiagramImage");
        ApplyImage();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_isDisposed &&
            !_generationCancellationToken.IsCancellationRequested &&
            !_hasCurrentPaletteImage &&
            !HasError &&
            _renderCancellation is null)
        {
            StartRender(isThemeRefresh: false);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelRender(keepLoadingState: false);
        ClearPendingLinkInteraction();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        if (_svgImage?.Size is not { Width: > 0, Height: > 0 } sourceSize || !double.IsFinite(availableSize.Width))
            return measured;
        var width = Math.Max(1, availableSize.Width);
        return new Size(width, Math.Max(1, width * sourceSize.Height / sourceSize.Width));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Visual target = _imageControl is not null ? _imageControl : this;
        _hoverLink = TryHitTestLink(e.GetPosition(target));
        if (_hoverLink is null)
            ClearValue(CursorProperty);
        else
            SetCurrentValue(CursorProperty, HandCursor);

        if (_pendingLinkPointerOrigin is { } pressedPoint &&
            HasExceededLinkClickDragThreshold(pressedPoint, e.GetPosition(this)))
        {
            ClearPendingLinkInteraction();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _hoverLink = null;
        ClearPendingLinkInteraction();
        ClearValue(CursorProperty);
        base.OnPointerExited(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Visual target = _imageControl is not null ? _imageControl : this;
        var pressedLink = TryHitTestLink(e.GetPosition(target));
        _hoverLink = pressedLink;
        if (pressedLink is { } uri && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pendingLinkPointerOrigin = e.GetPosition(this);
            _pendingLinkUri = uri;
            e.Handled = true;
            return;
        }

        ClearPendingLinkInteraction();
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var point = e.GetPosition(this);
        var pendingLinkUri = _pendingLinkUri;
        var pendingLinkPointerOrigin = _pendingLinkPointerOrigin;

        base.OnPointerReleased(e);

        Visual target = _imageControl is not null ? _imageControl : this;
        if (e.InitialPressMouseButton == MouseButton.Left &&
            pendingLinkUri is not null &&
            pendingLinkPointerOrigin is { } pressedPoint &&
            !HasExceededLinkClickDragThreshold(pressedPoint, point) &&
            TryHitTestLink(e.GetPosition(target)) is { } releasedLinkUri &&
            Uri.Compare(
                pendingLinkUri,
                releasedLinkUri,
                UriComponents.AbsoluteUri,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            e.Handled = true;
            ObserveLinkActivation(releasedLinkUri);
        }

        ClearPendingLinkInteraction();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_links.Count == 0)
        {
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                e.Handled = true;
                ObserveLinkActivation(_links[_keyboardLinkIndex].Uri);
                return;
            case Key.Left:
            case Key.Up:
                e.Handled = true;
                SelectKeyboardLink(_keyboardLinkIndex - 1);
                return;
            case Key.Right:
            case Key.Down:
                e.Handled = true;
                SelectKeyboardLink(_keyboardLinkIndex + 1);
                return;
            case Key.Home:
                e.Handled = true;
                SelectKeyboardLink(0);
                return;
            case Key.End:
                e.Handled = true;
                SelectKeyboardLink(_links.Count - 1);
                return;
            default:
                base.OnKeyDown(e);
                return;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        _automationPeer ??= new MermaidDiagramAutomationPeer(this);

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        CancelRender(keepLoadingState: false);
        Interlocked.Exchange(ref _initialOperation, null)?.Dispose();
        ClearPendingLinkInteraction();
        if (_imageControl is not null)
            _imageControl.Source = null;
        _imageControl = null;
        _svgImage = null;
        _svgSource?.Dispose();
        _svgSource = null;
        _hoverLink = null;
        SetLinks([]);
        _hasCurrentPaletteImage = false;
    }

    private void OnThemePaletteChanged()
    {
        SetCurrentValue(ForegroundProperty, ResolvePalette().Foreground);
        if (!_isInitialized || _isDisposed)
            return;
        _hasCurrentPaletteImage = false;
        StartRender(isThemeRefresh: HasImage);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs args)
    {
        if (ThemePalette is not null || !_isInitialized || _isDisposed)
            return;
        SetCurrentValue(ForegroundProperty, ResolvePalette().Foreground);
        _hasCurrentPaletteImage = false;
        StartRender(isThemeRefresh: HasImage);
    }

    private void StartRender(bool isThemeRefresh)
    {
        if (_isDisposed || _generationCancellationToken.IsCancellationRequested)
            return;
        CancelRender(keepLoadingState: true);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_generationCancellationToken);
        _renderCancellation = cancellation;
        var version = ++_renderVersion;
        SetIsLoading(true);
        if (!isThemeRefresh)
            SetHasError(false);
        var operation = Interlocked.Exchange(ref _initialOperation, null) ?? _beginAsyncOperation();
        ObserveRenderGeneration(version, cancellation, operation);
    }

    private async void ObserveRenderGeneration(
        int version,
        CancellationTokenSource cancellation,
        IDisposable operation)
    {
        try
        {
            await RenderGenerationAsync(version, cancellation, operation);
        }
        catch (Exception exception)
        {
            ReportNonRecoverableAsyncException(exception);
        }
    }

    private async Task RenderGenerationAsync(
        int version,
        CancellationTokenSource cancellation,
        IDisposable operation)
    {
        SvgSource? preparedSource = null;
        var renderedSvg = false;
        var request = new MermaidSvgRenderRequest(
            SourceText,
            ResolvePalette(),
            _fontFamily,
            _fontSize);
        try
        {
            var renderTask = StartRendererTask(
                _renderer,
                request,
                cancellation.Token);
            if (!HasImage && _renderer is MermaiderSvgRenderer cachedRenderer &&
                cachedRenderer.TryGetCachedVariant(SourceText, _fontFamily, _fontSize, out var cachedSvg))
            {
                await ShowCachedVariantAsync(version, cancellation, cachedSvg).ConfigureAwait(false);
            }

            var svg = await AwaitRendererAsync(renderTask, cancellation.Token).ConfigureAwait(false);
            renderedSvg = true;
            preparedSource = await Task.Run(() => _svgSourceFactory(svg), cancellation.Token).ConfigureAwait(false);
            if (preparedSource.Picture is null)
                throw new InvalidOperationException("The sanitized Mermaid SVG could not be parsed.");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentRender(version, cancellation))
                    return;
                var previous = _svgSource;
                _svgSource = preparedSource;
                preparedSource = null;
                _svgImage = new AvaloniaSvgImage { Source = _svgSource };
                SetLinks(ExtractLinks(_svgSource.Svg?.SourceDocument));
                ApplyImage();
                SetHasImage(true);
                _hasCurrentPaletteImage = true;
                SetHasError(false);
                SetIsLoading(false);
                InvalidateMeasure();
                previous?.Dispose();
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_isDisposed &&
                    version == _renderVersion &&
                    ReferenceEquals(_renderCancellation, cancellation))
                {
                    SetIsLoading(false);
                }
            });
        }
        catch (Exception exception) when (IsRecoverableAsyncException(exception))
        {
            if (renderedSvg && _renderer is MermaiderSvgRenderer cachedRenderer)
                cachedRenderer.Invalidate(SourceText, request.Palette, _fontFamily, _fontSize);
            Trace.TraceWarning("A Mermaid diagram could not be rendered: {0}", exception);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentRender(version, cancellation))
                    return;
                SetIsLoading(false);
                SetHasError(true);
                SetErrorText($"{_options.ErrorText} {GetErrorMessage(exception)}".Trim());
            });
        }
        finally
        {
            preparedSource?.Dispose();
            Interlocked.CompareExchange(ref _renderCancellation, null, cancellation);
            cancellation.Dispose();
            operation.Dispose();
        }
    }

    private static Task<string> StartRendererTask(
        IMermaidSvgRenderer renderer,
        MermaidSvgRenderRequest request,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => renderer.RenderAsync(request, cancellationToken),
            CancellationToken.None);

    private static async Task<string> AwaitRendererAsync(
        Task<string> renderTask,
        CancellationToken cancellationToken)
    {
        try
        {
            return await renderTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveAbandonedRenderer(renderTask);
            throw;
        }
    }

    private static async void ObserveAbandonedRenderer(Task<string> renderTask)
    {
        try
        {
            await renderTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (IsRecoverableAsyncException(exception))
        {
            Trace.TraceWarning("An abandoned Mermaid renderer failed after cancellation: {0}", exception);
        }
        catch (Exception exception)
        {
            ReportNonRecoverableAsyncException(exception);
        }
    }

    private async Task ShowCachedVariantAsync(int version, CancellationTokenSource cancellation, string svg)
    {
        SvgSource? preparedSource = null;
        try
        {
            preparedSource = await Task.Run(() => _svgSourceFactory(svg), cancellation.Token).ConfigureAwait(false);
            if (preparedSource.Picture is null)
                return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentRender(version, cancellation) || HasImage)
                    return;
                _svgSource = preparedSource;
                preparedSource = null;
                _svgImage = new AvaloniaSvgImage { Source = _svgSource };
                SetLinks(ExtractLinks(_svgSource.Svg?.SourceDocument));
                ApplyImage();
                SetHasImage(true);
                _hasCurrentPaletteImage = false;
                InvalidateMeasure();
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or ArgumentException)
        {
            Trace.TraceWarning("A cached Mermaid preview could not be displayed: {0}", exception);
        }
        finally
        {
            preparedSource?.Dispose();
        }
    }

    private MarkdownThemePalette ResolvePalette() =>
        ThemePalette ?? (ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light);

    private bool IsCurrentRender(int version, CancellationTokenSource cancellation) =>
        !_isDisposed && version == _renderVersion && !cancellation.IsCancellationRequested;

    private void ApplyImage()
    {
        if (_imageControl is not null)
            _imageControl.Source = _svgImage;
    }

    private Uri? TryHitTestLink(Point point)
    {
        if (_imageControl is not { Bounds.Width: > 0, Bounds.Height: > 0 } image ||
            _svgImage?.Size is not { Width: > 0, Height: > 0 } sourceSize ||
            _svgSource?.Svg is not { } svg)
            return null;
        var scale = Stretch.Uniform.CalculateScaling(image.Bounds.Size, sourceSize, StretchDirection.Both);
        var scaled = sourceSize * scale;
        var offsetX = (image.Bounds.Width - scaled.Width) / 2;
        var offsetY = (image.Bounds.Height - scaled.Height) / 2;
        if (point.X < offsetX || point.Y < offsetY || point.X > offsetX + scaled.Width || point.Y > offsetY + scaled.Height)
            return null;
        var picture = _svgSource.Picture;
        if (picture is null)
            return null;
        var picturePoint = new SKPoint(
            (float)((point.X - offsetX) / scale.X + picture.CullRect.Left),
            (float)((point.Y - offsetY) / scale.Y + picture.CullRect.Top));
        foreach (var element in svg.HitTestElements(picturePoint))
        {
            for (SvgElement? current = element; current is not null; current = current.Parent)
            {
                if (current is SvgAnchor anchor && MermaidSvgSanitizer.IsSafeHttpsUri(anchor.Href) &&
                    Uri.TryCreate(anchor.Href, UriKind.Absolute, out var uri))
                    return uri;
            }
        }
        return null;
    }

    private static IReadOnlyList<MermaidDiagramLink> ExtractLinks(SvgElement? root)
    {
        if (root is null)
            return [];

        var links = new List<MermaidDiagramLink>();
        AddLink(root, links);
        foreach (var element in root.Descendants())
            AddLink(element, links);
        return links;
    }

    private static void AddLink(SvgElement element, List<MermaidDiagramLink> links)
    {
        if (element is not SvgAnchor anchor ||
            !MermaidSvgSanitizer.IsSafeHttpsUri(anchor.Href) ||
            !Uri.TryCreate(anchor.Href, UriKind.Absolute, out var uri))
        {
            return;
        }

        var name = !string.IsNullOrWhiteSpace(anchor.Title)
            ? anchor.Title.Trim()
            : !string.IsNullOrWhiteSpace(anchor.Content)
                ? anchor.Content.Trim()
                : uri.AbsoluteUri;
        links.Add(new MermaidDiagramLink(uri, name));
    }

    private void SetLinks(IReadOnlyList<MermaidDiagramLink> links)
    {
        _links = links;
        _keyboardLinkIndex = links.Count == 0
            ? 0
            : Math.Clamp(_keyboardLinkIndex, 0, links.Count - 1);
        PseudoClasses.Set(":has-links", links.Count > 0);
        AutomationProperties.SetItemStatus(
            this,
            links.Count == 0 ? string.Empty : links[_keyboardLinkIndex].Name);
        _automationPeer?.RefreshLinks();
    }

    private void SelectKeyboardLink(int index)
    {
        if (_links.Count == 0)
            return;
        _keyboardLinkIndex = (index % _links.Count + _links.Count) % _links.Count;
        AutomationProperties.SetItemStatus(this, _links[_keyboardLinkIndex].Name);
    }

    private void ActivateAutomationLink(int index)
    {
        if ((uint)index >= (uint)_links.Count)
            return;
        SelectKeyboardLink(index);
        ObserveLinkActivation(_links[index].Uri);
    }

    private Task ActivateLinkAsync(Uri uri)
    {
        if (!MermaidSvgSanitizer.IsSafeHttpsUri(uri.AbsoluteUri))
            return Task.CompletedTask;
        if (_options.ActivateLinkAsync is { } activate)
            return activate(uri, _generationCancellationToken);
        return TopLevel.GetTopLevel(this) is { } topLevel ? topLevel.Launcher.LaunchUriAsync(uri) : Task.CompletedTask;
    }

    internal async Task ActivateLinkSafelyAsync(Uri uri)
    {
        try
        {
            await ActivateLinkAsync(uri).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverableAsyncException(exception))
        {
            Trace.TraceWarning("A Mermaid link could not be activated: {0}", exception);
        }
    }

    private async void ObserveLinkActivation(Uri uri)
    {
        try
        {
            await ActivateLinkSafelyAsync(uri);
        }
        catch (Exception exception)
        {
            ReportNonRecoverableAsyncException(exception);
        }
    }

    internal static bool IsRecoverableAsyncException(Exception exception) =>
        exception is not OutOfMemoryException and not AccessViolationException;

    internal static void ReportNonRecoverableAsyncException(Exception exception)
    {
        var dispatchInfo = ExceptionDispatchInfo.Capture(exception);
        Dispatcher.UIThread.Post(dispatchInfo.Throw, DispatcherPriority.Send);
    }

    private void CancelRender(bool keepLoadingState)
    {
        _renderVersion++;
        var cancellation = Interlocked.Exchange(ref _renderCancellation, null);
        if (cancellation is not null)
        {
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        if (!keepLoadingState)
            SetIsLoading(false);
    }

    private static bool HasExceededLinkClickDragThreshold(Point origin, Point current)
    {
        var deltaX = current.X - origin.X;
        var deltaY = current.Y - origin.Y;
        var thresholdSquared = LinkClickDragThreshold * LinkClickDragThreshold;
        return (deltaX * deltaX) + (deltaY * deltaY) > thresholdSquared;
    }

    private void ClearPendingLinkInteraction()
    {
        _pendingLinkPointerOrigin = null;
        _pendingLinkUri = null;
    }

    private void SetIsLoading(bool value)
    {
        SetAndRaise(IsLoadingProperty, ref _isLoading, value);
        PseudoClasses.Set(":loading", value);
    }

    private void SetHasImage(bool value)
    {
        SetAndRaise(HasImageProperty, ref _hasImage, value);
        PseudoClasses.Set(":has-image", value);
    }

    private void SetHasError(bool value)
    {
        SetAndRaise(HasErrorProperty, ref _hasError, value);
        PseudoClasses.Set(":error", value);
    }

    private void SetErrorText(string value) => SetAndRaise(ErrorTextProperty, ref _errorText, value);

    private static string GetErrorMessage(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message.Trim();
        return message.Length <= MaximumErrorMessageLength ? message : string.Concat(message.AsSpan(0, MaximumErrorMessageLength), "…");
    }

    private static SvgSource CreateSvgSource(string svg) => SvgSource.LoadFromSvg(MermaidSvgSanitizer.Sanitize(svg));

    private sealed class RetryRenderCommand(MermaidDiagramControl owner) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => !owner._isDisposed;
        public void Execute(object? parameter) => owner.StartRender(isThemeRefresh: owner.HasImage);
    }

    private sealed class EmptyOperation : IDisposable
    {
        public static EmptyOperation Instance { get; } = new();
        public void Dispose() { }
    }

    private sealed record MermaidDiagramLink(Uri Uri, string Name);

    private sealed class MermaidDiagramAutomationPeer(MermaidDiagramControl owner) : ControlAutomationPeer(owner)
    {
        private IReadOnlyList<AutomationPeer>? _links;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string? GetNameCore() => owner._options.AccessibleName;

        protected override string? GetHelpTextCore() => owner.SourceText;

        protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
        {
            if (_links is not null)
                return _links;

            var links = new AutomationPeer[owner._links.Count];
            for (var index = 0; index < links.Length; index++)
                links[index] = new MermaidLinkAutomationPeer(owner, this, index);
            _links = links;
            return links;
        }

        internal void RefreshLinks()
        {
            _links = null;
            RaiseChildrenChangedEvent();
        }

    }

    private sealed class MermaidLinkAutomationPeer(
        MermaidDiagramControl owner,
        MermaidDiagramAutomationPeer parent,
        int index) : ControlAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Hyperlink;

        protected override string? GetNameCore() =>
            (uint)index < (uint)owner._links.Count ? owner._links[index].Name : string.Empty;

        protected override string? GetAutomationIdCore() => $"MermaidLink_{index + 1}";

        protected override AutomationPeer? GetParentCore() => parent;

        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => [];

        protected override bool IsKeyboardFocusableCore() => false;

        protected override bool HasKeyboardFocusCore() => false;

        protected override void SetFocusCore()
        {
        }

        public void Invoke() => owner.ActivateAutomationLink(index);
    }
}
