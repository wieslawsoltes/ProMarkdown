using System.Buffers.Binary;
using System.Net.Http;
using Avalonia.Media.Imaging;

namespace ProMarkdown.Services;

/// <summary>Identifies image source kinds that Markdown rendering may load.</summary>
[Flags]
public enum MarkdownImageSourceKinds
{
    None = 0,
    Data = 1,
    File = 2,
    Remote = 4,
    All = Data | File | Remote
}

/// <summary>Configures the sources and resource limits used when loading Markdown images.</summary>
public sealed record MarkdownImageOptions
{
    private static readonly TimeSpan MaximumRemoteTimeout =
        TimeSpan.FromMilliseconds(uint.MaxValue - 1L);

    public const long DefaultMaximumBytes = 8L * 1024 * 1024;
    public const long DefaultMaximumPixelCount = 64L * 1024 * 1024;
    public static readonly TimeSpan DefaultRemoteTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Gets the compatibility policy that permits remote, file, and embedded data images.</summary>
    public static MarkdownImageOptions Default { get; } = new();

    /// <summary>Gets a policy that permits local files and embedded data images.</summary>
    public static MarkdownImageOptions BlockRemote { get; } = new()
    {
        AllowedSourceKinds = MarkdownImageSourceKinds.File | MarkdownImageSourceKinds.Data
    };

    /// <summary>Gets a policy that permits only embedded data images.</summary>
    public static MarkdownImageOptions EmbeddedOnly { get; } = new()
    {
        AllowedSourceKinds = MarkdownImageSourceKinds.Data
    };

    public MarkdownImageSourceKinds AllowedSourceKinds { get; init; } = MarkdownImageSourceKinds.All;

    public long MaximumBytes { get; init; } = DefaultMaximumBytes;

    public long MaximumPixelCount { get; init; } = DefaultMaximumPixelCount;

    public TimeSpan RemoteTimeout { get; init; } = DefaultRemoteTimeout;

    internal void Validate()
    {
        if ((AllowedSourceKinds & ~MarkdownImageSourceKinds.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(AllowedSourceKinds));
        if (MaximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumBytes));
        if (MaximumPixelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumPixelCount));
        if (RemoteTimeout <= TimeSpan.Zero || RemoteTimeout > MaximumRemoteTimeout)
            throw new ArgumentOutOfRangeException(nameof(RemoteTimeout));
    }
}

/// <summary>Describes one Markdown image load.</summary>
public sealed class MarkdownImageLoadRequest
{
    public required Uri Source { get; init; }

    public required MarkdownImageOptions Options { get; init; }
}

/// <summary>Loads a bounded bitmap for a rendered Markdown image.</summary>
public interface IMarkdownImageLoader
{
    Task<Bitmap> LoadAsync(MarkdownImageLoadRequest request, CancellationToken cancellationToken);
}

/// <summary>Loads data, file, and HTTPS/HTTP images according to <see cref="MarkdownImageOptions"/>.</summary>
public sealed class DefaultMarkdownImageLoader : IMarkdownImageLoader
{
    private static readonly HttpClient HttpClient = new();

    public static DefaultMarkdownImageLoader Instance { get; } = new();

    private DefaultMarkdownImageLoader()
    {
    }

    public async Task<Bitmap> LoadAsync(MarkdownImageLoadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = MarkdownImagePolicy.ValidateRequest(request.Source, request.Options);
        cancellationToken.ThrowIfCancellationRequested();

        await using Stream stream = kind switch
        {
            MarkdownImageSourceKinds.Data => LoadDataUri(request.Source, request.Options.MaximumBytes),
            MarkdownImageSourceKinds.File => LoadFile(request.Source, request.Options.MaximumBytes),
            MarkdownImageSourceKinds.Remote => await LoadRemoteAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported image source: {request.Source.Scheme}")
        };

        cancellationToken.ThrowIfCancellationRequested();
        ValidateEncodedPixelCount(stream, request.Options.MaximumPixelCount);
        var bitmap = new Bitmap(stream);
        var pixelCount = checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height);
        if (pixelCount > request.Options.MaximumPixelCount)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("The decoded Markdown image exceeds the configured pixel limit.");
        }

        return bitmap;
    }

    private static void ValidateEncodedPixelCount(Stream stream, long maximumPixelCount)
    {
        if (!stream.CanSeek || !TryReadEncodedDimensions(stream, out var width, out var height))
            throw new InvalidOperationException("The Markdown image format is not supported by the bounded image loader.");

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The Markdown image has invalid dimensions.");
        if ((long)width * height > maximumPixelCount)
            throw new InvalidOperationException("The decoded Markdown image exceeds the configured pixel limit.");
    }

    private static bool TryReadEncodedDimensions(Stream stream, out int width, out int height)
    {
        var originalPosition = stream.Position;
        try
        {
            Span<byte> header = stackalloc byte[32];
            var read = ReadUpTo(stream, header);
            if (read < 4)
            {
                width = 0;
                height = 0;
                return false;
            }

            if (read >= 24 && header[0] == 0x89 && header[1..8].SequenceEqual("PNG\r\n\x1a\n"u8))
            {
                width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
                height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
                return true;
            }

            if (read >= 10 &&
                (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]);
                height = BinaryPrimitives.ReadUInt16LittleEndian(header[8..10]);
                return true;
            }

            if (read >= 26 && header[..2].SequenceEqual("BM"u8))
            {
                var dibHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]);
                int encodedHeight;
                if (dibHeaderSize == 12)
                {
                    width = BinaryPrimitives.ReadUInt16LittleEndian(header[18..20]);
                    encodedHeight = BinaryPrimitives.ReadUInt16LittleEndian(header[20..22]);
                }
                else if (dibHeaderSize >= 40)
                {
                    width = BinaryPrimitives.ReadInt32LittleEndian(header[18..22]);
                    encodedHeight = BinaryPrimitives.ReadInt32LittleEndian(header[22..26]);
                }
                else
                {
                    width = 0;
                    height = 0;
                    return false;
                }

                height = encodedHeight == int.MinValue ? 0 : Math.Abs(encodedHeight);
                return true;
            }

            if (header[0] == 0xFF && header[1] == 0xD8)
                return TryReadJpegDimensions(stream, originalPosition, out width, out height);

            if (read >= 30 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
                return TryReadWebPDimensions(header, out width, out height);

            if (read >= 6 && header[0] == 0 && header[1] == 0 && header[2] == 1 && header[3] == 0)
                return TryReadIcoDimensions(stream, originalPosition, out width, out height);

            if (TryReadWbmpDimensions(header[..read], out width, out height))
                return true;

            width = 0;
            height = 0;
            return false;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static int ReadUpTo(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                break;
            totalRead += read;
        }

        return totalRead;
    }

    private static bool TryReadJpegDimensions(Stream stream, long start, out int width, out int height)
    {
        stream.Position = start + 2;
        while (stream.Position < stream.Length)
        {
            var prefix = stream.ReadByte();
            if (prefix < 0)
                break;
            if (prefix != 0xFF)
                continue;

            int marker;
            do
            {
                marker = stream.ReadByte();
            } while (marker == 0xFF);

            if (marker < 0 || marker is 0xD9 or 0xDA)
                break;
            if (marker is 0x00 or 0x01 or >= 0xD0 and <= 0xD8)
                continue;

            var segmentLength = ReadUInt16BigEndian(stream);
            if (segmentLength < 2)
                break;

            if (IsJpegStartOfFrame(marker))
            {
                if (segmentLength < 7 || stream.ReadByte() < 0)
                    break;
                height = ReadUInt16BigEndian(stream);
                width = ReadUInt16BigEndian(stream);
                return width > 0 && height > 0;
            }

            var bytesToSkip = segmentLength - 2L;
            if (bytesToSkip > stream.Length - stream.Position)
                break;
            stream.Seek(bytesToSkip, SeekOrigin.Current);
        }

        width = 0;
        height = 0;
        return false;
    }

    private static int ReadUInt16BigEndian(Stream stream)
    {
        var high = stream.ReadByte();
        var low = stream.ReadByte();
        return high < 0 || low < 0 ? -1 : (high << 8) | low;
    }

    private static bool IsJpegStartOfFrame(int marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or
        0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or
        0xCD or 0xCE or 0xCF;

    private static bool TryReadWebPDimensions(ReadOnlySpan<byte> header, out int width, out int height)
    {
        var chunkType = header[12..16];
        if (chunkType.SequenceEqual("VP8X"u8))
        {
            width = 1 + ReadUInt24LittleEndian(header[24..27]);
            height = 1 + ReadUInt24LittleEndian(header[27..30]);
            return true;
        }

        if (chunkType.SequenceEqual("VP8L"u8) && header[20] == 0x2F)
        {
            width = 1 + header[21] + ((header[22] & 0x3F) << 8);
            height = 1 + ((header[22] & 0xC0) >> 6) + (header[23] << 2) + ((header[24] & 0x0F) << 10);
            return true;
        }

        if (chunkType.SequenceEqual("VP8 "u8) &&
            header[23] == 0x9D && header[24] == 0x01 && header[25] == 0x2A)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(header[26..28]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(header[28..30]) & 0x3FFF;
            return true;
        }

        width = 0;
        height = 0;
        return false;
    }

    private static bool TryReadIcoDimensions(Stream stream, long start, out int width, out int height)
    {
        stream.Position = start + 4;
        Span<byte> countBytes = stackalloc byte[2];
        if (ReadUpTo(stream, countBytes) != countBytes.Length)
            return FailDimensions(out width, out height);

        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(countBytes);
        var availableLength = stream.Length - start;
        var directoryLength = 6L + (entryCount * 16L);
        if (entryCount == 0 || directoryLength > availableLength)
            return FailDimensions(out width, out height);

        width = 0;
        height = 0;
        long largestPixelCount = 0;
        Span<byte> entry = stackalloc byte[16];
        for (var index = 0; index < entryCount; index++)
        {
            stream.Position = start + 6L + (index * 16L);
            if (ReadUpTo(stream, entry) != entry.Length)
                return FailDimensions(out width, out height);

            var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..12]);
            var payloadOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..16]);
            if (payloadLength == 0 || payloadOffset > availableLength || payloadLength > availableLength - payloadOffset)
                return FailDimensions(out width, out height);

            if (!TryReadIcoPayloadDimensions(
                    stream,
                    start + payloadOffset,
                    payloadLength,
                    out var entryWidth,
                    out var entryHeight))
            {
                return FailDimensions(out width, out height);
            }

            var pixelCount = (long)entryWidth * entryHeight;
            if (pixelCount <= largestPixelCount)
                continue;

            largestPixelCount = pixelCount;
            width = entryWidth;
            height = entryHeight;
        }

        return largestPixelCount > 0;
    }

    private static bool TryReadIcoPayloadDimensions(
        Stream stream,
        long payloadOffset,
        uint payloadLength,
        out int width,
        out int height)
    {
        stream.Position = payloadOffset;
        Span<byte> header = stackalloc byte[24];
        var read = ReadUpTo(stream, header[..(int)Math.Min((uint)header.Length, payloadLength)]);
        if (read >= 24 && header[0] == 0x89 && header[1..8].SequenceEqual("PNG\r\n\x1a\n"u8))
        {
            width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            return width > 0 && height > 0;
        }

        if (read < 12)
            return FailDimensions(out width, out height);

        var dibHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        int encodedHeight;
        if (dibHeaderSize == 12)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(header[4..6]);
            encodedHeight = BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]);
        }
        else if (dibHeaderSize >= 40 && read >= 12)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
            encodedHeight = BinaryPrimitives.ReadInt32LittleEndian(header[8..12]);
        }
        else
        {
            return FailDimensions(out width, out height);
        }

        if (width <= 0 || encodedHeight == int.MinValue)
            return FailDimensions(out width, out height);

        var absoluteHeight = Math.Abs(encodedHeight);
        if (absoluteHeight < 2 || (absoluteHeight & 1) != 0)
            return FailDimensions(out width, out height);

        height = absoluteHeight / 2;
        return true;
    }

    private static bool FailDimensions(out int width, out int height)
    {
        width = 0;
        height = 0;
        return false;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> value) =>
        value[0] | (value[1] << 8) | (value[2] << 16);

    private static bool TryReadWbmpDimensions(ReadOnlySpan<byte> header, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (header.Length < 4 || header[0] != 0 || header[1] != 0)
            return false;

        var index = 2;
        return TryReadWbmpInteger(header, ref index, out width) &&
               TryReadWbmpInteger(header, ref index, out height) &&
               width > 0 &&
               height > 0;
    }

    private static bool TryReadWbmpInteger(ReadOnlySpan<byte> header, ref int index, out int value)
    {
        value = 0;
        for (var count = 0; count < 5 && index < header.Length; count++)
        {
            var current = header[index++];
            if (value > (int.MaxValue >> 7))
                return false;
            value = (value << 7) | (current & 0x7F);
            if ((current & 0x80) == 0)
                return true;
        }

        return false;
    }

    internal static bool IsSourceAllowed(Uri source, MarkdownImageOptions options)
    {
        return MarkdownImagePolicy.IsSourceAllowed(source, options);
    }

    private static MemoryStream LoadDataUri(Uri source, long maximumBytes)
    {
        var value = source.OriginalString;
        var separator = value.IndexOf(',');
        if (separator < 0 || !value[..separator].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only base64 data URI images are supported.");

        var encoded = value.AsSpan(separator + 1);
        if (encoded.Length > checked(maximumBytes * 4 / 3 + 4))
            throw new InvalidOperationException("The Markdown image exceeds the configured byte limit.");

        var bytes = Convert.FromBase64String(encoded.ToString());
        if (bytes.LongLength > maximumBytes)
            throw new InvalidOperationException("The Markdown image exceeds the configured byte limit.");
        return new MemoryStream(bytes, writable: false);
    }

    private static Stream LoadFile(Uri source, long maximumBytes)
    {
        var stream = new FileStream(source.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= maximumBytes)
            return new MarkdownBoundedReadStream(stream, maximumBytes);

        stream.Dispose();
        throw new InvalidOperationException("The Markdown image exceeds the configured byte limit.");
    }

    private static async Task<MemoryStream> LoadRemoteAsync(
        MarkdownImageLoadRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Options.RemoteTimeout);
        using var response = await HttpClient.GetAsync(
            request.Source,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length > request.Options.MaximumBytes)
            throw new InvalidOperationException("The Markdown image exceeds the configured byte limit.");

        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var destination = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (destination.Length > request.Options.MaximumBytes - read)
                    throw new InvalidOperationException("The Markdown image exceeds the configured byte limit.");

                await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            }

            destination.Position = 0;
            return destination;
        }
        catch
        {
            destination.Dispose();
            throw;
        }
    }
}

internal static class MarkdownImagePolicy
{
    public static MarkdownImageSourceKinds ValidateRequest(Uri source, MarkdownImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var kind = GetSourceKind(source);
        if (!IsSourceAllowed(source, options, kind))
            throw new InvalidOperationException("The Markdown image source is blocked by the configured policy.");
        return kind;
    }

    public static bool IsSourceAllowed(Uri source, MarkdownImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        return IsSourceAllowed(source, options, GetSourceKind(source));
    }

    private static bool IsSourceAllowed(
        Uri source,
        MarkdownImageOptions options,
        MarkdownImageSourceKinds kind)
    {
        if (kind == MarkdownImageSourceKinds.None || (options.AllowedSourceKinds & kind) == 0)
            return false;

        return kind != MarkdownImageSourceKinds.File ||
               !source.IsUnc ||
               (options.AllowedSourceKinds & MarkdownImageSourceKinds.Remote) != 0;
    }

    private static MarkdownImageSourceKinds GetSourceKind(Uri source)
    {
        if (source.IsFile)
            return MarkdownImageSourceKinds.File;
        if (source.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
            return MarkdownImageSourceKinds.Data;
        if (source.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return MarkdownImageSourceKinds.Remote;
        return MarkdownImageSourceKinds.None;
    }
}

internal sealed class MarkdownBoundedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maximumBytes;

    public MarkdownBoundedReadStream(Stream inner, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (!inner.CanRead || !inner.CanSeek)
            throw new ArgumentException("The bounded stream must support reading and seeking.", nameof(inner));
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        _inner = inner;
        _maximumBytes = maximumBytes;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => Math.Min(_inner.Length, _maximumBytes);

    public override long Position
    {
        get => _inner.Position;
        set
        {
            if (value < 0 || value > Length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _inner.Position = value;
        }
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, ClampReadCount(count));

    public override int Read(Span<byte> buffer) =>
        _inner.Read(buffer[..ClampReadCount(buffer.Length)]);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, ClampReadCount(count), cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer[..ClampReadCount(buffer.Length)], cancellationToken);

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        Position = target;
        return target;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private int ClampReadCount(int requestedCount)
    {
        var remaining = Length - Position;
        return remaining <= 0 ? 0 : (int)Math.Min(requestedCount, remaining);
    }
}
