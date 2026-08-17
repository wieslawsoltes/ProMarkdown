using System.Globalization;
using System.Diagnostics;
using System.Text;
using ProMarkdown.Services;
using Mermaider;
using Mermaider.Models;
using Color = Avalonia.Media.Color;
using HslColor = Avalonia.Media.HslColor;
using IBrush = Avalonia.Media.IBrush;
using ISolidColorBrush = Avalonia.Media.ISolidColorBrush;

namespace ProMarkdown.Plugin.Mermaid;

internal delegate Task MermaidSvgRenderDelegate(
    string source,
    Stream destination,
    RenderOptions options,
    CancellationToken cancellationToken);

internal sealed class MermaiderSvgRenderer
{
    internal const int MaximumSourceCharacters = 256 * 1024;
    internal const int MaximumSvgCharacters = 8 * 1024 * 1024;
    internal const int MaximumCachedResults = 64;
    internal const int MaximumCacheBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan DefaultRenderTimeout = TimeSpan.FromSeconds(15);

    private static readonly ResourceLimits RenderLimits = new()
    {
        MaxInputLength = MaximumSourceCharacters,
        MaxOutputLength = MaximumSvgCharacters,
        RenderDeadline = DefaultRenderTimeout
    };

    private readonly Lock _gate = new();
    private readonly Dictionary<RenderKey, CacheEntry> _cache = [];
    private readonly LinkedList<RenderKey> _leastRecentlyUsed = [];
    private readonly MermaidSvgRenderDelegate _renderAsync;
    private readonly TimeSpan _renderTimeout;
    private readonly SemaphoreSlim _renderSlot = new(1, 1);
    private int _cacheBytes;
    private int _renderInvocationCount;

    internal int RenderInvocationCount => Volatile.Read(ref _renderInvocationCount);

    internal int CachedResultCount
    {
        get
        {
            lock (_gate)
                return _cache.Count;
        }
    }

    internal int CachedOutputBytes
    {
        get
        {
            lock (_gate)
                return _cacheBytes;
        }
    }

    internal bool TryGetCachedVariant(
        string source,
        string fontFamily,
        double fontSize,
        out string svg)
    {
        lock (_gate)
        {
            for (var node = _leastRecentlyUsed.Last; node is not null; node = node.Previous)
            {
                if (!node.Value.MatchesContent(source, fontFamily, fontSize) ||
                    !_cache.TryGetValue(node.Value, out var entry) ||
                    !entry.RenderTask.IsCompletedSuccessfully)
                {
                    continue;
                }

                svg = entry.RenderTask.Result;
                Touch(entry);
                return true;
            }
        }

        svg = string.Empty;
        return false;
    }

    internal void Invalidate(
        string source,
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize)
    {
        var key = RenderKey.Create(source, palette, fontFamily, fontSize);
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            if (!_cache.TryGetValue(key, out var entry))
                return;

            RemoveEntry(key, entry);
            if (!entry.RenderTask.IsCompleted)
                cancellation = entry.RenderCancellation;
        }

        CancelRender(cancellation);
    }

    public MermaiderSvgRenderer()
        : this(static async (source, destination, options, cancellationToken) =>
            await MermaidRenderer.RenderSvgAsync(
                source,
                destination,
                options,
                cancellationToken).ConfigureAwait(false))
    {
    }

    internal MermaiderSvgRenderer(
        MermaidSvgRenderDelegate renderAsync,
        TimeSpan? renderTimeout = null)
    {
        _renderAsync = renderAsync ?? throw new ArgumentNullException(nameof(renderAsync));
        _renderTimeout = renderTimeout ?? DefaultRenderTimeout;
        if (_renderTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(renderTimeout));
    }

    public async Task<string> RenderAsync(
        string source,
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(fontFamily);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Length > MaximumSourceCharacters)
            throw new InvalidOperationException("The Mermaid source exceeds the configured size limit.");

        var key = RenderKey.Create(source, palette, fontFamily, fontSize);
        CacheEntry? entry = null;
        var observeCompletion = false;
        List<CancellationTokenSource>? evictedCancellations = null;
        var capacityExceeded = false;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached) &&
                !cached.RenderTask.IsFaulted &&
                !cached.RenderTask.IsCanceled)
            {
                Touch(cached);
                entry = cached;
            }
            else
            {
                if (cached is not null)
                    RemoveEntry(key, cached);

                evictedCancellations = TrimCache(MaximumCachedResults - 1);
                if (_cache.Count >= MaximumCachedResults)
                {
                    capacityExceeded = true;
                }
                else
                {
                    var node = _leastRecentlyUsed.AddLast(key);
                    var renderCancellation = new CancellationTokenSource();
                    var renderTask = RenderUncachedAsync(
                        source,
                        palette,
                        fontFamily,
                        fontSize,
                        renderCancellation.Token);
                    entry = new CacheEntry(renderTask, node, renderCancellation);
                    _cache.Add(key, entry);
                    observeCompletion = true;
                }
            }

            if (entry is not null)
                entry.WaiterCount++;
            else if (!capacityExceeded)
                throw new InvalidOperationException("The Mermaid render cache entered an invalid state.");

            evictedCancellations ??= TrimCache();
        }

        CancelEvictedRenders(evictedCancellations);
        if (capacityExceeded)
            throw new InvalidOperationException("Too many Mermaid diagrams are waiting to render.");

        if (observeCompletion)
            _ = ObserveCompletionAsync(key, entry!);

        try
        {
            return await entry!.RenderTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseWaiter(key, entry!);
        }
    }

    private async Task<string> RenderUncachedAsync(
        string source,
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(_renderTimeout);
        var operationToken = timeoutCancellation.Token;
        var slotAcquired = false;
        var destinationTransferred = false;
        var destination = new SizeLimitedMemoryStream(MaximumSvgCharacters);
        try
        {
            await _renderSlot.WaitAsync(operationToken).ConfigureAwait(false);
            slotAcquired = true;
            Interlocked.Increment(ref _renderInvocationCount);
            var options = CreateOptions(palette, fontFamily, fontSize);
            var renderOperation = Task.Run(
                () => _renderAsync(source, destination, options, operationToken),
                CancellationToken.None);
            try
            {
                await renderOperation.WaitAsync(operationToken).ConfigureAwait(false);
            }
            catch when (!renderOperation.IsCompleted)
            {
                // The renderer did not observe cancellation. Keep the serial render
                // slot and destination alive until it exits, while allowing the
                // caller's timeout or cancellation to complete immediately.
                destinationTransferred = true;
                slotAcquired = false;
                _ = ObserveAbandonedRenderAsync(renderOperation, destination);
                throw;
            }

            operationToken.ThrowIfCancellationRequested();

            if (destination.Length > MaximumSvgCharacters)
                throw new InvalidOperationException("The generated Mermaid SVG exceeds the configured size limit.");

            return Encoding.UTF8.GetString(destination.GetBuffer(), 0, checked((int)destination.Length));
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The Mermaid diagram did not render within {_renderTimeout.TotalSeconds:0.###} seconds.",
                exception);
        }
        finally
        {
            if (!destinationTransferred)
                destination.Dispose();
            if (slotAcquired)
                _renderSlot.Release();
        }
    }

    private async Task ObserveAbandonedRenderAsync(Task renderOperation, Stream destination)
    {
        try
        {
            await renderOperation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "The Mermaid renderer faulted after its initiating request ended: {0}",
                exception);
        }
        finally
        {
            destination.Dispose();
            _renderSlot.Release();
        }
    }

    private async Task ObserveCompletionAsync(RenderKey key, CacheEntry observedEntry)
    {
        List<CancellationTokenSource>? evictedCancellations = null;
        try
        {
            var svg = await observedEntry.RenderTask.ConfigureAwait(false);
            lock (_gate)
            {
                if (!_cache.TryGetValue(key, out var entry) || !ReferenceEquals(entry, observedEntry))
                    return;
                entry.Size = checked(svg.Length * sizeof(char));
                _cacheBytes = checked(_cacheBytes + entry.Size);
                Touch(entry);
                evictedCancellations = TrimCache();
            }
        }
        catch
        {
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var entry) && ReferenceEquals(entry, observedEntry))
                    RemoveEntry(key, entry);
            }
        }
        finally
        {
            observedEntry.RenderCancellation.Dispose();
            CancelEvictedRenders(evictedCancellations);
        }
    }

    private static RenderOptions CreateOptions(
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize)
    {
        var fallback = palette.IsDark ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light;
        return new RenderOptions
        {
            Bg = ToCss(palette.Surface, fallback.Surface),
            Fg = ToCss(palette.Foreground, fallback.Foreground),
            Surface = ToCss(palette.SurfaceRaised, fallback.SurfaceRaised),
            Muted = ToCss(palette.MutedForeground, fallback.MutedForeground),
            Accent = ToCss(palette.Foreground, fallback.Foreground),
            Border = ToCss(palette.Foreground, fallback.Foreground),
            Line = ToCss(palette.MutedForeground, fallback.MutedForeground),
            DataPalette = CreateDataPalette(palette, fallback),
            Font = fontFamily,
            FontSize = string.Create(CultureInfo.InvariantCulture, $"{fontSize:0.###}px"),
            Transparent = true,
            Strict = new StrictStylingOptions(),
            SanitizeMode = SanitizeMode.Block,
            Limits = RenderLimits
        };
    }

    private static string[] CreateDataPalette(MarkdownThemePalette palette, MarkdownThemePalette fallback)
    {
        ReadOnlySpan<double> hueOffsets = [0, 43, 86, 137, 188, 231, 274, 317];
        var accent = ResolveColor(palette.Accent, fallback.Accent).ToHsl();
        var saturation = Math.Clamp(accent.S, 0.55, 0.82);
        var baseLightness = palette.IsDark
            ? Math.Clamp(accent.L, 0.58, 0.70)
            : Math.Clamp(accent.L, 0.38, 0.50);
        var result = new string[hueOffsets.Length];
        for (var index = 0; index < hueOffsets.Length; index++)
        {
            var lightnessOffset = index % 2 == 0 ? 0.025 : -0.025;
            var color = new HslColor(
                1,
                (accent.H + hueOffsets[index]) % 360,
                saturation,
                Math.Clamp(baseLightness + lightnessOffset, 0, 1)).ToRgb();
            result[index] = ToCss(color);
        }

        return result;
    }

    private static string ToCss(IBrush brush, IBrush fallback) =>
        ToCss(ResolveColor(brush, fallback));

    private static Color ResolveColor(IBrush brush, IBrush fallback)
    {
        if (brush is ISolidColorBrush solid)
            return solid.Color;
        if (fallback is ISolidColorBrush fallbackSolid)
            return fallbackSolid.Color;

        throw new InvalidOperationException("Markdown theme palette brushes must resolve to solid colors.");
    }

    private static string ToCss(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void Touch(CacheEntry entry)
    {
        _leastRecentlyUsed.Remove(entry.Node);
        _leastRecentlyUsed.AddLast(entry.Node);
    }

    private List<CancellationTokenSource>? TrimCache(int maximumResults = MaximumCachedResults)
    {
        List<CancellationTokenSource>? cancellations = null;
        while (_cache.Count > maximumResults || _cacheBytes > MaximumCacheBytes)
        {
            var oldest = FindEvictionCandidate();
            if (oldest is null)
                break;

            var removed = _cache[oldest.Value];
            RemoveEntry(oldest.Value, removed);
            if (!removed.RenderTask.IsCompleted)
                (cancellations ??= []).Add(removed.RenderCancellation);
        }

        return cancellations;
    }

    private LinkedListNode<RenderKey>? FindEvictionCandidate()
    {
        for (var node = _leastRecentlyUsed.First; node is not null; node = node.Next)
        {
            if (!_cache.TryGetValue(node.Value, out var entry))
                continue;
            if (entry.RenderTask.IsCompleted || entry.WaiterCount == 0)
                return node;
        }

        return null;
    }

    private void ReleaseWaiter(RenderKey key, CacheEntry entry)
    {
        CancellationTokenSource? cancellation = null;
        lock (_gate)
        {
            entry.WaiterCount--;
            if (entry.WaiterCount == 0 &&
                !entry.RenderTask.IsCompleted &&
                _cache.TryGetValue(key, out var cached) &&
                ReferenceEquals(cached, entry))
            {
                RemoveEntry(key, entry);
                cancellation = entry.RenderCancellation;
            }
        }

        CancelRender(cancellation);
    }

    private void RemoveEntry(RenderKey key, CacheEntry entry)
    {
        _cache.Remove(key);
        _leastRecentlyUsed.Remove(entry.Node);
        _cacheBytes -= entry.Size;
    }

    private static void CancelEvictedRenders(List<CancellationTokenSource>? cancellations)
    {
        if (cancellations is null)
            return;

        foreach (var cancellation in cancellations)
            CancelRender(cancellation);
    }

    private static void CancelRender(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion can dispose the shared render token concurrently with eviction.
        }
    }

    private sealed class CacheEntry(
        Task<string> renderTask,
        LinkedListNode<RenderKey> node,
        CancellationTokenSource renderCancellation)
    {
        public Task<string> RenderTask { get; } = renderTask;
        public LinkedListNode<RenderKey> Node { get; } = node;
        public CancellationTokenSource RenderCancellation { get; } = renderCancellation;
        public int Size { get; set; }
        public int WaiterCount { get; set; }
    }

    private sealed class SizeLimitedMemoryStream(long maximumLength) : MemoryStream
    {
        public override void SetLength(long value)
        {
            EnsureLength(value);
            base.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWriteLength(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWriteLength(buffer.Length);
            base.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWriteLength(count);
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWriteLength(buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            EnsureWriteLength(1);
            base.WriteByte(value);
        }

        private void EnsureWriteLength(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (Position > maximumLength || count > maximumLength - Position)
                ThrowSizeLimitExceeded();
        }

        private void EnsureLength(long value)
        {
            if (value > maximumLength)
                ThrowSizeLimitExceeded();
        }

        private static void ThrowSizeLimitExceeded() =>
            throw new InvalidOperationException(
                "The generated Mermaid SVG exceeds the configured size limit.");
    }

    private readonly record struct RenderKey(
        string Source,
        bool IsDark,
        string Surface,
        string SurfaceRaised,
        string Foreground,
        string MutedForeground,
        string Accent,
        string FontFamily,
        double FontSize)
    {
        public static RenderKey Create(
            string source,
            MarkdownThemePalette palette,
            string fontFamily,
            double fontSize) => new(
                source,
                palette.IsDark,
                ToCss(palette.Surface, palette.IsDark ? MarkdownThemePalette.Dark.Surface : MarkdownThemePalette.Light.Surface),
                ToCss(palette.SurfaceRaised, palette.IsDark ? MarkdownThemePalette.Dark.SurfaceRaised : MarkdownThemePalette.Light.SurfaceRaised),
                ToCss(palette.Foreground, palette.IsDark ? MarkdownThemePalette.Dark.Foreground : MarkdownThemePalette.Light.Foreground),
                ToCss(palette.MutedForeground, palette.IsDark ? MarkdownThemePalette.Dark.MutedForeground : MarkdownThemePalette.Light.MutedForeground),
                ToCss(palette.Accent, palette.IsDark ? MarkdownThemePalette.Dark.Accent : MarkdownThemePalette.Light.Accent),
                fontFamily,
                fontSize);

        public bool MatchesContent(string source, string fontFamily, double fontSize) =>
            string.Equals(Source, source, StringComparison.Ordinal) &&
            string.Equals(FontFamily, fontFamily, StringComparison.Ordinal) &&
            FontSize.Equals(fontSize);
    }
}
