using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Svg.Skia;
using Avalonia.Threading;

namespace ProMarkdown.Plugin.Mermaid;

internal sealed class MermaidPlayback : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(83);

    private readonly IReadOnlyList<MermaidAnimationScenario> _scenarios;
    private readonly CancellationToken _cancellationToken;
    private readonly Func<string, SvgSource> _loadSvg;
    private readonly Func<SvgSource, SvgSource?> _replaceFrame;
    private readonly Action<Exception> _showError;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private readonly PlaybackCommand _previous;
    private readonly PlaybackCommand _next;
    private MermaidAnimation _diagram;
    private TimeSpan _position;
    private Int128 _elapsedPositionTicks;
    private Int128 _clockStartTicks;
    private FrameRequest? _requestedFrame;
    private (FrameRequest Request, int Version)? _renderingFrame;
    private MermaidScrubFrameCache? _scrubCache;
    private (TimeSpan Time, long EstimatedBytes)? _displayedScrubSample;
    private TimeSpan? _lastScrollPosition;
    private TimeSpan? _deferredScrollPosition;
    private int _scenarioIndex;
    private int _navigationStepIndex;
    private int _frameVersion;
    private bool _isPlaying;
    private bool _isVisible;
    private bool _isDisposed;

    public MermaidPlayback(
        MermaidAnimation diagram,
        CancellationToken cancellationToken,
        Func<string, SvgSource> loadSvg,
        Func<SvgSource, SvgSource?> replaceFrame,
        Action<Exception> showError)
    {
        _diagram = diagram;
        _scenarios = diagram.Scenarios;
        _cancellationToken = cancellationToken;
        _loadSvg = loadSvg;
        _replaceFrame = replaceFrame;
        _showError = showError;
        _timer = new DispatcherTimer(FrameInterval, DispatcherPriority.Background, OnTick);
        _timer.Stop();
        ScenarioNames = _scenarios.Select(scenario => scenario.Name).ToArray();
        _navigationStepIndex = FindStep(TimeSpan.Zero);
        PlayPauseCommand = new PlaybackCommand(TogglePlayback);
        ReplayCommand = new PlaybackCommand(Replay);
        _previous = new PlaybackCommand(() => GoToStep(_navigationStepIndex - 1), () => _navigationStepIndex > 0);
        _next = new PlaybackCommand(() => GoToStep(_navigationStepIndex + 1), () => _navigationStepIndex + 1 < _diagram.Steps.Count);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand PlayPauseCommand { get; }
    public ICommand ReplayCommand { get; }
    public ICommand PreviousCommand => _previous;
    public ICommand NextCommand => _next;
    public IReadOnlyList<string> ScenarioNames { get; }
    public bool HasScenarios => ScenarioNames.Count > 0;
    public bool HasSteps => _diagram.Steps.Count > 1;
    public bool IsPlaying => _isPlaying;
    public string PlayPauseText => IsPlaying ? "Pause" : "Play";
    public double DurationSeconds => _diagram.ScoreDuration.TotalSeconds;
    public string StoryDescription => string.Join(" ", _diagram.Steps.Select(step => step.Caption).OfType<string>());

    public double PositionSeconds
    {
        get => Math.Min(DurationSeconds, _position.TotalSeconds);
        set
        {
            if (!double.IsFinite(value))
                return;

            var position = Math.Clamp(value, 0, DurationSeconds);
            var target = PendingFrameRequest is { Kind: FrameKind.Seek } seek ? seek.Time.TotalSeconds : PositionSeconds;
            if (Math.Abs(position - target) >= 0.001)
                Seek(TimeSpan.FromSeconds(position));
        }
    }

    private FrameRequest? CurrentFrameRequest =>
        _renderingFrame is { } frame && frame.Version == _frameVersion ? frame.Request : null;

    private FrameRequest? PendingFrameRequest => _requestedFrame ?? CurrentFrameRequest;

    public int ScenarioIndex
    {
        get => _scenarioIndex;
        set
        {
            if ((uint)value >= (uint)_scenarios.Count || value == _scenarioIndex)
                return;
            _scenarioIndex = value;
            _diagram = _scenarios[value].Animation;
            _lastScrollPosition = null;
            ClearScrubCache();
            Seek(TimeSpan.Zero);
            SetPosition(TimeSpan.Zero);
            Notify(nameof(ScenarioIndex));
            Notify(nameof(HasSteps));
            Notify(nameof(DurationSeconds));
            Notify(nameof(PositionSeconds));
            RefreshStepCommands();
        }
    }

    public void UpdateViewport(bool visible, double scrollProgress)
    {
        if (_isDisposed)
            return;

        var enteredViewport = visible && !_isVisible;
        if (_isVisible != visible)
        {
            _isVisible = visible;
            if (!visible)
            {
                if (_requestedFrame is not { KeepWhenHidden: true } &&
                    CurrentFrameRequest is { KeepWhenHidden: true } pending)
                    _requestedFrame = pending;
                _frameVersion++;
                ClearScrubCache();
                _lastScrollPosition = null;
                _deferredScrollPosition = null;
            }
            UpdateClock();
        }

        if (visible && !_isPlaying && _diagram.ScrubsOnScroll)
        {
            // A stable set of sample times lets scrolling in either direction reuse prepared SVGs.
            var samples = Math.Max(1, Math.Ceiling(DurationSeconds / FrameInterval.TotalSeconds));
            var sample = Math.Round(Math.Clamp(scrollProgress, 0, 1) * samples);
            var time = _diagram.ScoreDuration * (sample / samples);
            if (_lastScrollPosition != time)
            {
                _lastScrollPosition = time;
                if (PendingFrameRequest is not { KeepWhenHidden: true })
                    _requestedFrame = new FrameRequest(time, FrameKind.ScrollSample);
                else if (!enteredViewport)
                    // Preserve the seek on reentry. Subsequent scroll changes apply after it.
                    _deferredScrollPosition = time;
            }
        }
        ScheduleRequestedFrame();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _frameVersion++;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _clock.Stop();
        ClearScrubCache();
    }

    private void TogglePlayback()
    {
        if (_isPlaying)
        {
            if (PendingFrameRequest is not { Kind: FrameKind.Seek })
            {
                _frameVersion++;
                _requestedFrame = null;
            }
            SetPlaying(false);
            UpdateNavigationStep();
            ScheduleRequestedFrame();
        }
        else if (!_diagram.Repeats && (PendingFrameRequest?.Time ?? _position) >= _diagram.ScoreDuration)
        {
            Replay();
        }
        else
        {
            SetPlaying(true);
        }
    }

    private void Replay()
    {
        Seek(TimeSpan.Zero);
        SetPlaying(true, 0);
    }

    private void GoToStep(int index)
    {
        var step = _diagram.Steps[index];
        // The next caption starts fading in at the boundary. Show the selected step's outcome.
        var end = step.End - TimeSpan.FromMilliseconds(1);
        Seek(end > step.Start ? end : step.Start);
    }

    private void Seek(TimeSpan time)
    {
        SetPlaying(false);
        _deferredScrollPosition = null;
        _frameVersion++;
        RequestFrame(new FrameRequest(time, FrameKind.Seek));
        UpdateNavigationStep();
    }

    private void SetPlaying(bool value, Int128? startTicks = null)
    {
        if (_isDisposed || _isPlaying == value)
            return;
        _isPlaying = value;
        if (value)
            _deferredScrollPosition = null;
        _clockStartTicks = startTicks ?? PendingFrameRequest?.ElapsedTicks ?? _elapsedPositionTicks;
        UpdateClock();
        Notify(nameof(IsPlaying));
        Notify(nameof(PlayPauseText));
    }

    private void UpdateClock()
    {
        if (_isPlaying && _isVisible)
        {
            _clock.Start();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _clock.Reset();
            _clockStartTicks = PendingFrameRequest?.ElapsedTicks ?? _elapsedPositionTicks;
        }
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (_cancellationToken.IsCancellationRequested)
        {
            Dispose();
            return;
        }
        if (_renderingFrame is not null)
            return;
        if (_requestedFrame is not null || !_isPlaying)
        {
            if (!_isPlaying)
                _timer.Stop();
            TryStartFrame();
            return;
        }

        // Keep the dash phase independent of the wrapped position, even after a seek near TimeSpan.MaxValue.
        var elapsedTicks = _clockStartTicks + _clock.Elapsed.Ticks;
        var timeTicks = elapsedTicks;
        if (_diagram.Repeats)
        {
            if (elapsedTicks >= _diagram.Duration.Ticks && _diagram.Duration > _diagram.Intro)
            {
                var loopTicks = (_diagram.Duration - _diagram.Intro).Ticks;
                timeTicks = _diagram.Intro.Ticks + (elapsedTicks - _diagram.Intro.Ticks) % loopTicks;
            }
        }
        else if (elapsedTicks >= _diagram.ScoreDuration.Ticks)
        {
            SetPlaying(false);
            RequestFrame(new FrameRequest(_diagram.ScoreDuration, FrameKind.End, elapsedTicks));
            return;
        }
        var time = TimeSpan.FromTicks((long)Int128.Min(timeTicks, long.MaxValue));
        RequestFrame(new FrameRequest(time, TotalElapsedTicks: elapsedTicks));
    }

    private void RequestFrame(FrameRequest request)
    {
        if (_isDisposed)
            return;
        _requestedFrame = request;
        TryStartFrame();
    }

    private void ScheduleRequestedFrame()
    {
        if (!_isDisposed && _isVisible && _requestedFrame is not null)
            _timer.Start();
    }

    private void TryStartFrame()
    {
        if (_isDisposed || !_isVisible || _renderingFrame is not null || _requestedFrame is not { } request ||
            _cancellationToken.IsCancellationRequested)
            return;
        _requestedFrame = null;
        if (request.Kind == FrameKind.ScrollSample)
        {
            if (_displayedScrubSample?.Time == request.Time)
                return;
            if (_scrubCache is not null && _scrubCache.TryTake(request.Time, out var cached))
            {
                PresentFrame(request, cached.Source, cached.EstimatedBytes);
                return;
            }
        }
        _renderingFrame = (request, _frameVersion);
        _ = RenderFrameAsync(_diagram, request, _frameVersion);
    }

    private async Task RenderFrameAsync(MermaidAnimation diagram, FrameRequest request, int version)
    {
        SvgSource? image = null;
        try
        {
            try
            {
                var prepared = await Task.Run(() =>
                {
                    var svg = diagram.CreateFrame(request.Time, request.ElapsedTicks);
                    return (Source: _loadSvg(svg), svg.Length);
                }, _cancellationToken);
                image = prepared.Source;
                if (image.Picture is not { } picture)
                    throw new InvalidOperationException("The sanitized Mermaid animation frame could not be parsed.");
                if (IsCurrentFrame(version))
                {
                    var bytes = (long)prepared.Length * sizeof(char) + (long)picture.ApproximateBytesUsed;
                    PresentFrame(request, image, bytes);
                    image = null;
                }
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) when (MermaidDiagramControl.IsRecoverableAsyncException(exception))
            {
                if (IsCurrentFrame(version))
                {
                    _requestedFrame = null;
                    _deferredScrollPosition = null;
                    SetPlaying(false);
                    _showError(exception);
                }
            }
            finally
            {
                image?.Dispose();
                _renderingFrame = null;
                if (_requestedFrame is null && _deferredScrollPosition is { } time)
                {
                    _deferredScrollPosition = null;
                    _requestedFrame = new FrameRequest(time, FrameKind.ScrollSample);
                }
                if (request.Kind == FrameKind.Seek && !_isDisposed)
                    UpdateNavigationStep();
                ScheduleRequestedFrame();
            }
        }
        catch (Exception exception)
        {
            // The timer and commands cannot await this task. Forward failures from callbacks or cleanup too.
            MermaidDiagramControl.ReportNonRecoverableAsyncException(exception);
        }
    }

    private void PresentFrame(FrameRequest request, SvgSource source, long estimatedBytes)
    {
        var previous = _replaceFrame(source);
        if (previous is not null)
        {
            if (_displayedScrubSample is { } cached)
            {
                (_scrubCache ??= new MermaidScrubFrameCache()).Add(
                    new MermaidScrubFrameCache.Entry(cached.Time, previous, cached.EstimatedBytes));
            }
            else
                previous.Dispose();
        }
        _displayedScrubSample = request.Kind == FrameKind.ScrollSample ? (request.Time, estimatedBytes) : null;
        _elapsedPositionTicks = request.ElapsedTicks;
        SetPosition(request.Time);
    }

    private void ClearScrubCache()
    {
        // The view still owns and displays its current frame.
        _displayedScrubSample = null;
        _scrubCache?.Dispose();
    }

    private bool IsCurrentFrame(int version) => !_isDisposed && _isVisible &&
        version == _frameVersion && !_cancellationToken.IsCancellationRequested;

    private void SetPosition(TimeSpan time)
    {
        if (_position != time)
        {
            _position = time;
            Notify(nameof(PositionSeconds));
        }
        UpdateNavigationStep();
    }

    private void UpdateNavigationStep()
    {
        var target = PendingFrameRequest is { Kind: FrameKind.Seek } seek ? seek.Time : _position;
        var index = FindStep(target);
        if (_navigationStepIndex != index)
        {
            _navigationStepIndex = index;
            RefreshStepCommands();
        }
    }

    private int FindStep(TimeSpan time)
    {
        var steps = _diagram.Steps;
        var index = 0;
        while (index < steps.Count && steps[index].Start <= time)
            index++;
        return index - 1;
    }

    private void RefreshStepCommands()
    {
        _previous.Refresh();
        _next.Refresh();
    }

    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    private enum FrameKind
    {
        Playback,
        ScrollSample,
        Seek,
        End
    }

    private readonly record struct FrameRequest(
        TimeSpan Time, FrameKind Kind = FrameKind.Playback, Int128? TotalElapsedTicks = null)
    {
        public bool KeepWhenHidden => Kind is FrameKind.Seek or FrameKind.End;
        public Int128 ElapsedTicks => TotalElapsedTicks ?? Time.Ticks;
    }

    private sealed class PlaybackCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
                execute();
        }

        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
