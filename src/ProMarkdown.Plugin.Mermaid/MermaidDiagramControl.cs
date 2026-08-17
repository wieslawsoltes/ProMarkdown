using System.Diagnostics;
using System.Windows.Input;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using ProMarkdown.Services;
using AvaloniaSvgImage = Avalonia.Svg.Skia.SvgImage;

namespace ProMarkdown.Plugin.Mermaid;

internal sealed class MermaidDiagramControl : Grid, IDisposable
{
    private const int MaximumErrorMessageLength = 512;
    private readonly MermaiderSvgRenderer _renderer;
    private readonly string _source;
    private readonly MarkdownThemePalette _palette;
    private readonly string _fontFamily;
    private readonly double _fontSize;
    private readonly Func<string, SvgSource> _svgSourceFactory;
    private readonly Image _image;
    private readonly ProgressBar _progress;
    private readonly StackPanel _error;
    private readonly TextBlock _errorMessage;
    private SvgSource? _svgSource;
    private AvaloniaSvgImage? _svgImage;
    private CancellationTokenSource? _renderCancellation;
    private int _renderVersion;
    private bool _hasCurrentPaletteImage;
    private bool _isDisposed;

    public MermaidDiagramControl(
        MermaiderSvgRenderer renderer,
        string source,
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize,
        Func<string, SvgSource>? svgSourceFactory = null)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _fontFamily = fontFamily ?? throw new ArgumentNullException(nameof(fontFamily));
        _fontSize = fontSize;
        _svgSourceFactory = svgSourceFactory ?? CreateSvgSource;

        Background = Brushes.Transparent;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        MinHeight = 48;

        _image = new Image
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Stretch = Stretch.Uniform,
            IsVisible = false
        };
        Children.Add(_image);

        _progress = new ProgressBar
        {
            Width = 64,
            Height = 4,
            IsIndeterminate = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Children.Add(_progress);

        _errorMessage = new TextBlock
        {
            Text = "Mermaid diagram could not be rendered.",
            Foreground = palette.Foreground,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _error = new StackPanel
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                _errorMessage,
                new SelectableTextBlock
                {
                    Text = source,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                    FontSize = Math.Max(fontSize - 1, 12),
                    Foreground = palette.Foreground,
                    TextWrapping = TextWrapping.Wrap
                },
                new Button
                {
                    Content = "Retry",
                    Command = new RetryCommand(this),
                    HorizontalAlignment = HorizontalAlignment.Left
                }
            }
        };
        Children.Add(_error);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_isDisposed && !_hasCurrentPaletteImage)
            StartRender();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelRender();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        if (_svgImage?.Size is not { Width: > 0, Height: > 0 } sourceSize ||
            !double.IsFinite(availableSize.Width))
            return measured;

        var width = Math.Max(1, availableSize.Width);
        var height = width * sourceSize.Height / sourceSize.Width;
        return new Size(width, Math.Max(1, height));
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        CancelRender();
        _image.Source = null;
        _svgImage = null;
        _hasCurrentPaletteImage = false;
        _svgSource?.Dispose();
        _svgSource = null;
    }

    private async void StartRender()
    {
        if (_isDisposed)
            return;

        CancelRender();
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _renderCancellation, cancellation);
        var version = ++_renderVersion;
        _progress.IsVisible = true;
        _error.IsVisible = false;
        var cachedRender = Task.CompletedTask;
        if (_svgImage is null &&
            _renderer.TryGetCachedVariant(_source, _fontFamily, _fontSize, out var cachedSvg))
        {
            cachedRender = ShowCachedVariantAsync(version, cancellation, cachedSvg);
        }

        var authoritativeRender = RenderAsync(version, cancellation);
        try
        {
            await Task.WhenAll(cachedRender, authoritativeRender);
        }
        catch (Exception exception)
        {
            Trace.TraceError("The Mermaid UI render boundary failed: {0}", exception);
        }
    }

    private async Task ShowCachedVariantAsync(
        int version,
        CancellationTokenSource cancellation,
        string cachedSvg)
    {
        SvgSource? preparedSource = null;
        try
        {
            preparedSource = await Task.Run(
                () => _svgSourceFactory(cachedSvg),
                cancellation.Token).ConfigureAwait(false);
            if (preparedSource.Picture is null)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentRender(version, cancellation) || _svgImage is not null)
                    return;

                _svgSource = preparedSource;
                preparedSource = null;
                _svgImage = new AvaloniaSvgImage { Source = _svgSource };
                _hasCurrentPaletteImage = false;
                _image.Source = _svgImage;
                _image.IsVisible = true;
                _progress.IsVisible = false;
                InvalidateMeasure();
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (XmlException)
        {
            // A cached preview is opportunistic; the authoritative render still reports failures.
        }
        catch (InvalidOperationException)
        {
            // A cached preview is opportunistic; the authoritative render still reports failures.
        }
        catch (ArgumentException)
        {
            // A cached preview is opportunistic; the authoritative render still reports failures.
        }
        catch (Exception exception)
        {
            // A cached preview must never take down the UI. The authoritative render remains
            // responsible for displaying a user-facing failure for the requested palette.
            Trace.TraceWarning(
                "A cached Mermaid preview could not be displayed: {0}",
                exception);
        }
        finally
        {
            preparedSource?.Dispose();
        }
    }

    private async Task RenderAsync(int version, CancellationTokenSource cancellation)
    {
        SvgSource? preparedSource = null;
        var renderedSvg = false;
        try
        {
            var svg = await _renderer.RenderAsync(
                _source,
                _palette,
                _fontFamily,
                _fontSize,
                cancellation.Token).ConfigureAwait(false);
            renderedSvg = true;
            preparedSource = await Task.Run(
                () => _svgSourceFactory(svg),
                cancellation.Token).ConfigureAwait(false);
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
                _hasCurrentPaletteImage = true;
                _image.Source = _svgImage;
                _image.IsVisible = true;
                _progress.IsVisible = false;
                _error.IsVisible = false;
                InvalidateMeasure();
                previous?.Dispose();
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (renderedSvg)
                _renderer.Invalidate(_source, _palette, _fontFamily, _fontSize);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentRender(version, cancellation))
                    return;
                _progress.IsVisible = false;
                _image.IsVisible = _svgImage is not null;
                _errorMessage.Text = $"Mermaid diagram could not be rendered: {GetErrorMessage(exception)}";
                _error.IsVisible = true;
            });
        }
        finally
        {
            preparedSource?.Dispose();
            Interlocked.CompareExchange(ref _renderCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private bool IsCurrentRender(int version, CancellationTokenSource cancellation) =>
        !_isDisposed &&
        version == _renderVersion &&
        ReferenceEquals(Volatile.Read(ref _renderCancellation), cancellation) &&
        !cancellation.IsCancellationRequested;

    private void CancelRender()
    {
        _renderVersion++;
        var cancellation = Interlocked.Exchange(ref _renderCancellation, null);
        if (cancellation is null)
            return;

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The render completion path can dispose the token after ownership is exchanged.
        }
    }

    private static string GetErrorMessage(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message.Trim();
        return message.Length <= MaximumErrorMessageLength
            ? message
            : string.Concat(message.AsSpan(0, MaximumErrorMessageLength), "…");
    }

    private static SvgSource CreateSvgSource(string svg) =>
        SvgSource.LoadFromSvg(MermaidSvgSanitizer.Sanitize(svg));

    private sealed class RetryCommand(MermaidDiagramControl owner) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => !owner._isDisposed;

        public void Execute(object? parameter) => owner.StartRender();
    }
}
