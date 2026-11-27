using Avalonia.Svg.Skia;

namespace ProMarkdown.Plugin.Mermaid;

internal sealed class MermaidScrubFrameCache : IDisposable
{
    private const int MaximumFrames = 16;
    private const int MaximumEstimatedBytes = 2 * 1024 * 1024;

    private readonly List<Entry> _entries = new(MaximumFrames);
    private long _estimatedBytes;

    public bool TryTake(TimeSpan time, out Entry entry)
    {
        for (var index = _entries.Count - 1; index >= 0; index--)
        {
            if (_entries[index].Time != time)
                continue;

            entry = _entries[index];
            _entries.RemoveAt(index);
            _estimatedBytes -= entry.EstimatedBytes;
            return true;
        }

        entry = default;
        return false;
    }

    public void Add(Entry entry)
    {
        if (entry.EstimatedBytes > MaximumEstimatedBytes)
        {
            entry.Source.Dispose();
            return;
        }

        while (_entries.Count >= MaximumFrames || _estimatedBytes + entry.EstimatedBytes > MaximumEstimatedBytes)
        {
            var oldest = _entries[0];
            _entries.RemoveAt(0);
            _estimatedBytes -= oldest.EstimatedBytes;
            oldest.Source.Dispose();
        }

        _entries.Add(entry);
        _estimatedBytes += entry.EstimatedBytes;
    }

    public void Dispose()
    {
        foreach (var entry in _entries)
            entry.Source.Dispose();
        _entries.Clear();
        _estimatedBytes = 0;
    }

    internal readonly record struct Entry(TimeSpan Time, SvgSource Source, long EstimatedBytes);
}
