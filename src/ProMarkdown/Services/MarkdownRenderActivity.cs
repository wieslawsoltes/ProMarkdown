using Avalonia.Threading;

namespace ProMarkdown.Services;

internal sealed class MarkdownRenderActivity(Action completed) : IDisposable
{
    private Action? _completed = completed ?? throw new ArgumentNullException(nameof(completed));
    private readonly object _gate = new();
    private int _count = 1;
    private bool _isCompleted;
    private bool _isDisposed;

    public IDisposable Begin()
    {
        lock (_gate)
        {
            if (_isCompleted || _isDisposed)
                return EmptyLease.Instance;

            _count++;
            return new Lease(this);
        }
    }

    public void Complete()
    {
        Action? completion = null;
        lock (_gate)
        {
            if (_isCompleted || _isDisposed)
                return;

            _count--;
            if (_count == 0)
            {
                _isCompleted = true;
                completion = _completed;
                _completed = null;
            }
        }

        if (completion is not null)
            Dispatch(completion);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _isDisposed = true;
            _completed = null;
        }
    }

    private static void Dispatch(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private sealed class Lease(MarkdownRenderActivity owner) : IDisposable
    {
        private MarkdownRenderActivity? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Complete();
    }

    private sealed class EmptyLease : IDisposable
    {
        public static EmptyLease Instance { get; } = new();
        public void Dispose() { }
    }
}
