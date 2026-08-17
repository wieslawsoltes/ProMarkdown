using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ProMarkdown.Services;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Windows.Input;

namespace ProMarkdown.Controls;

public sealed class MarkdownTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string?>(nameof(Markdown));

    public static readonly StyledProperty<Uri?> BaseUriProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, Uri?>(nameof(BaseUri));

    public static readonly StyledProperty<bool> IsEditingEnabledProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, bool>(nameof(IsEditingEnabled));

    public static readonly StyledProperty<MarkdownEditorPresentationMode> EditorPresentationModeProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, MarkdownEditorPresentationMode>(
            nameof(EditorPresentationMode),
            defaultValue: MarkdownEditorPresentationMode.Inline);

    /// <summary>Identifies the <see cref="ThemePalette"/> property.</summary>
    public static readonly StyledProperty<MarkdownThemePalette?> ThemePaletteProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, MarkdownThemePalette?>(nameof(ThemePalette));

    /// <summary>Identifies the <see cref="IsTaskListInteractive"/> property.</summary>
    public static readonly StyledProperty<bool> IsTaskListInteractiveProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, bool>(nameof(IsTaskListInteractive));

    /// <summary>Identifies the <see cref="TaskListToggleCommand"/> property.</summary>
    public static readonly StyledProperty<ICommand?> TaskListToggleCommandProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, ICommand?>(nameof(TaskListToggleCommand));

    private const double LinkClickDragThreshold = 4d;
    private static readonly Cursor LinkCursor = new(StandardCursorType.Hand);
    private readonly TaskListToggleForwardingCommandImpl _taskListToggleForwardingCommand;
    private IMarkdownRenderController _renderController = MarkdownRenderingServices.DefaultController;
    private IMarkdownHitTestingService _hitTestingService = MarkdownRenderingServices.DefaultHitTestingService;
    private IMarkdownEditingService _editingService = MarkdownRenderingServices.DefaultEditingService;
    private MarkdownEditorPreferences _editorPreferences = new();
    private MarkdownRenderResourceTracker? _renderResources;
    private MarkdownRenderResult? _lastRenderResult;
    private MarkdownEditorSession? _activeEditorSession;
    private int _renderGeneration;
    private double _lastMeasuredWidth = double.NaN;
    private double _effectiveViewportWidth = double.NaN;
    private Point? _pendingLinkPointerOrigin;
    private Uri? _pendingLinkUri;
    private bool _isAttachedToVisualTree;

    static MarkdownTextBlock()
    {
        BackgroundProperty.OverrideDefaultValue<MarkdownTextBlock>(Brushes.Transparent);
        MarkdownProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.HandleMarkdownChanged());
        BaseUriProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        IsEditingEnabledProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.HandleEditingEnabledChanged());
        EditorPresentationModeProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        FontFamilyProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        FontSizeProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        ForegroundProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        TextWrappingProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        ThemePaletteProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.RebuildMarkdown());
        IsTaskListInteractiveProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.ConfigureTaskListCheckBoxes());
        TaskListToggleCommandProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.HandleTaskListToggleCommandChanged());
        BoundsProperty.Changed.AddClassHandler<MarkdownTextBlock>((control, _) => control.HandleBoundsChanged());
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public Uri? BaseUri
    {
        get => GetValue(BaseUriProperty);
        set => SetValue(BaseUriProperty, value);
    }

    public bool IsEditingEnabled
    {
        get => GetValue(IsEditingEnabledProperty);
        set => SetValue(IsEditingEnabledProperty, value);
    }

    public MarkdownEditorPresentationMode EditorPresentationMode
    {
        get => GetValue(EditorPresentationModeProperty);
        set => SetValue(EditorPresentationModeProperty, value);
    }

    /// <summary>Gets or sets the semantic palette used by the rendered Markdown document.</summary>
    public MarkdownThemePalette? ThemePalette
    {
        get => GetValue(ThemePaletteProperty);
        set => SetValue(ThemePaletteProperty, value);
    }

    /// <summary>Gets or sets whether task-list checkboxes accept pointer and keyboard input.</summary>
    public bool IsTaskListInteractive
    {
        get => GetValue(IsTaskListInteractiveProperty);
        set => SetValue(IsTaskListInteractiveProperty, value);
    }

    /// <summary>Gets or sets the command invoked with a <see cref="MarkdownTaskListToggleRequest"/> after a task checkbox changes.</summary>
    public ICommand? TaskListToggleCommand
    {
        get => GetValue(TaskListToggleCommandProperty);
        set => SetValue(TaskListToggleCommandProperty, value);
    }

    public IMarkdownRenderController RenderController
    {
        get => _renderController;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_renderController, value))
            {
                return;
            }

            _renderController = value;
            RebuildMarkdown();
        }
    }

    public IMarkdownHitTestingService HitTestingService
    {
        get => _hitTestingService;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _hitTestingService = value;
        }
    }

    public IMarkdownEditingService EditingService
    {
        get => _editingService;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_editingService, value))
            {
                return;
            }

            _editingService = value;
            RebuildMarkdown();
        }
    }

    public MarkdownEditorPreferences EditorPreferences
    {
        get => _editorPreferences;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _editorPreferences = value;
            RebuildMarkdown();
        }
    }

    public MarkdownRenderResult? LastRenderResult => _lastRenderResult;

    public MarkdownParseResult? LastParseResult => _lastRenderResult?.ParseResult;

    public MarkdownRenderMap? LastRenderMap => _lastRenderResult?.RenderMap;

    public MarkdownEditorSession? ActiveEditorSession => _activeEditorSession;

    public event EventHandler<MarkdownEditedEventArgs>? MarkdownEdited;

    public event EventHandler<MarkdownEditCanceledEventArgs>? MarkdownEditCanceled;

    public MarkdownTextBlock()
    {
        _taskListToggleForwardingCommand = new TaskListToggleForwardingCommandImpl(this);
        MarkdownDocumentSelection.SetEnabled(this, true);
        MarkdownDocumentLayout.SetEnabled(this, true);
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        RebuildMarkdown();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        _isAttachedToVisualTree = true;
        _taskListToggleForwardingCommand.SetSource(TaskListToggleCommand);
        RebuildMarkdown();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        _isAttachedToVisualTree = false;
        _renderGeneration = unchecked(_renderGeneration + 1);
        _taskListToggleForwardingCommand.SetSource(null);
        _effectiveViewportWidth = double.NaN;
        _lastRenderResult = null;
        _activeEditorSession = null;
        ClearPendingLinkInteraction();
        ClearValue(CursorProperty);
        DisposeRenderResources();
    }

    private void HandleMarkdownChanged()
    {
        if (_activeEditorSession is not null)
        {
            CancelEdit(MarkdownEditCancellationReason.MarkdownChanged);
            return;
        }

        RebuildMarkdown();
    }

    private void HandleEditingEnabledChanged()
    {
        if (!IsEditingEnabled && _activeEditorSession is not null)
        {
            CancelEdit(MarkdownEditCancellationReason.EditingDisabled);
            return;
        }

        RebuildMarkdown();
    }

    private void HandleBoundsChanged()
    {
        MarkdownDocumentSelection.InvalidateLayout(this);
        if (Bounds.Width <= 0 && ResolveViewportWidth() <= 0)
        {
            return;
        }

        HandleAvailableWidthChanged(ResolveAvailableWidth());
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs eventArgs)
    {
        if (eventArgs.EffectiveViewport.Width <= 0)
        {
            return;
        }

        _effectiveViewportWidth = eventArgs.EffectiveViewport.Width;
        HandleAvailableWidthChanged(ResolveAvailableWidth());
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (TextWrapping == TextWrapping.NoWrap)
        {
            return base.MeasureOverride(availableSize);
        }

        var constrainedWidth = ResolveAvailableWidth(availableSize.Width);
        if (constrainedWidth > 0 &&
            (double.IsInfinity(availableSize.Width) || constrainedWidth < availableSize.Width))
        {
            availableSize = new Size(constrainedWidth, availableSize.Height);
        }

        return base.MeasureOverride(availableSize);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (ShouldBypassSelectionPointerHandling())
        {
            return;
        }

        var point = e.GetPosition(this);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            TryResolveLinkTarget(point, out var navigateUri) &&
            navigateUri is not null)
        {
            _pendingLinkPointerOrigin = point;
            _pendingLinkUri = navigateUri;
        }
        else
        {
            ClearPendingLinkInteraction();
        }

        base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (ShouldBypassSelectionPointerHandling())
        {
            return;
        }

        var point = e.GetPosition(this);
        UpdateLinkCursor(point);

        if (_pendingLinkPointerOrigin is { } pressedPoint &&
            HasExceededLinkClickDragThreshold(pressedPoint, point))
        {
            ClearPendingLinkInteraction();
        }

        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (ShouldBypassSelectionPointerHandling())
        {
            return;
        }

        var point = e.GetPosition(this);
        var pendingLinkUri = _pendingLinkUri;
        var pendingLinkPointerOrigin = _pendingLinkPointerOrigin;

        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton == MouseButton.Left &&
            pendingLinkUri is not null &&
            pendingLinkPointerOrigin is { } pressedPoint &&
            !HasExceededLinkClickDragThreshold(pressedPoint, point) &&
            TryResolveLinkTarget(point, out var releasedLinkUri) &&
            releasedLinkUri is not null &&
            Uri.Compare(
                pendingLinkUri,
                releasedLinkUri,
                UriComponents.AbsoluteUri,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0)
        {
            e.Handled = true;
            _ = LaunchUriAsync(releasedLinkUri);
        }

        ClearPendingLinkInteraction();
        UpdateLinkCursor(point);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        ClearPendingLinkInteraction();
        ClearValue(CursorProperty);
        base.OnPointerExited(e);
    }

    private void HandleAvailableWidthChanged(double width)
    {
        if (width <= 0 || TextWrapping == TextWrapping.NoWrap)
        {
            return;
        }

        if (double.IsNaN(_lastMeasuredWidth) || Math.Abs(_lastMeasuredWidth - width) > 1)
        {
            _lastMeasuredWidth = width;
            InvalidateMeasure();
        }
    }

    private bool ShouldBypassSelectionPointerHandling()
    {
        // While an inline editor is active, let embedded controls own pointer input instead of
        // letting SelectableTextBlock enter text-selection mode and interfere with popups.
        return _activeEditorSession is not null;
    }

    private void UpdateLinkCursor(Point point)
    {
        if (TryResolveLinkTarget(point, out _))
        {
            SetCurrentValue(CursorProperty, LinkCursor);
        }
        else
        {
            ClearValue(CursorProperty);
        }
    }

    private bool TryResolveLinkTarget(Point point, out Uri? navigateUri)
    {
        return TryResolveLinkTarget(HitTestMarkdown(point), out navigateUri);
    }

    private bool TryResolveLinkTarget(MarkdownHitTestResult? hitTestResult, out Uri? navigateUri)
    {
        navigateUri = null;

        if (hitTestResult is null)
        {
            return false;
        }

        for (MarkdownObject? current = hitTestResult.AstNode.Node;
             current is not null;
             hitTestResult.ParseResult.TryGetParent(current, out current))
        {
            switch (current)
            {
                case LinkInline { IsImage: false } linkInline:
                return MarkdownUriUtilities.TryResolveUri(BaseUri, linkInline.GetDynamicUrl?.Invoke() ?? linkInline.Url, out navigateUri);
                case AutolinkInline autolinkInline:
                    var navigateUrl = autolinkInline.IsEmail
                        ? $"mailto:{autolinkInline.Url}"
                        : autolinkInline.Url;
                    return MarkdownUriUtilities.TryResolveUri(BaseUri, navigateUrl, out navigateUri);
            }
        }

        return false;
    }

    private Task LaunchUriAsync(Uri navigateUri)
    {
        return TopLevel.GetTopLevel(this) is { } topLevel
            ? topLevel.Launcher.LaunchUriAsync(navigateUri)
            : Task.CompletedTask;
    }

    private void ClearPendingLinkInteraction()
    {
        _pendingLinkPointerOrigin = null;
        _pendingLinkUri = null;
    }

    private static bool HasExceededLinkClickDragThreshold(Point origin, Point current)
    {
        var deltaX = current.X - origin.X;
        var deltaY = current.Y - origin.Y;
        var thresholdSquared = LinkClickDragThreshold * LinkClickDragThreshold;
        return ((deltaX * deltaX) + (deltaY * deltaY)) > thresholdSquared;
    }

    private void RebuildMarkdown()
    {
        ClearPendingLinkInteraction();
        ClearValue(CursorProperty);
        var previousResources = _renderResources;
        var resourceTracker = new MarkdownRenderResourceTracker();
        var renderGeneration = unchecked(++_renderGeneration);

        if (string.IsNullOrWhiteSpace(Markdown))
        {
            _lastRenderResult = MarkdownRenderResult.Empty(resourceTracker);
            _renderResources = resourceTracker;
            Text = string.Empty;
            Inlines = _lastRenderResult.Inlines;
            previousResources?.Dispose();
            MarkdownDocumentLayout.Refresh(this);
            MarkdownDocumentSelection.Reset(this);
            return;
        }

        var request = new MarkdownRenderRequest
        {
            Markdown = Markdown,
            Context = new MarkdownRenderContext
            {
                BaseUri = BaseUri,
                FontSize = FontSize,
                FontFamily = FontFamily,
                Foreground = Foreground,
                TextWrapping = TextWrapping,
                ThemePalette = ThemePalette ?? MarkdownThemePalette.Resolve(Foreground),
                AvailableWidth = ResolveAvailableWidth(),
                RenderGeneration = renderGeneration,
                ResourceTracker = resourceTracker,
                IsCurrentRender = IsCurrentRender,
                EditorState = CreateEditorState()
            }
        };

        MarkdownRenderResult result;
        try
        {
            result = RenderController.Render(request);
        }
        catch
        {
            resourceTracker.Dispose();
            throw;
        }

        if (!ReferenceEquals(result.ResourceTracker, resourceTracker))
            resourceTracker.Dispose();

        _lastRenderResult = result;
        Text = null;
        Inlines = result.Inlines;
        _renderResources = result.ResourceTracker;
        previousResources?.Dispose();
        ConfigureTaskListCheckBoxes();
        MarkdownDocumentLayout.Refresh(this);
        MarkdownDocumentSelection.Reset(this);
    }

    internal void ConfigureTaskListCheckBox(CheckBox checkBox)
    {
        var interactive = IsTaskListInteractive && TaskListToggleCommand is not null;
        MarkdownTaskListNormalizer.SetInteractive(checkBox, interactive);
        checkBox.Focusable = interactive;
        checkBox.IsHitTestVisible = interactive;
        checkBox.Command = interactive ? _taskListToggleForwardingCommand : null;
        checkBox.CommandParameter = interactive ? checkBox : null;
    }

    private void HandleTaskListToggleCommandChanged()
    {
        _taskListToggleForwardingCommand.SetSource(_isAttachedToVisualTree ? TaskListToggleCommand : null);
        ConfigureTaskListCheckBoxes();
    }

    private void ConfigureTaskListCheckBoxes()
    {
        foreach (var checkBox in MarkdownTaskListNormalizer.EnumerateCheckBoxes(Inlines))
            ConfigureTaskListCheckBox(checkBox);
    }

    public MarkdownHitTestResult? HitTestMarkdown(Point point)
    {
        if (_lastRenderResult is null)
        {
            return null;
        }

        return HitTestingService.HitTest(new MarkdownHitTestRequest
        {
            Host = this,
            Point = point,
            TextLayout = TextLayout,
            Padding = Padding,
            RenderResult = _lastRenderResult
        });
    }

    public bool TryBeginEdit(Point point)
    {
        return HitTestMarkdown(point) is { } hit && TryBeginEdit(hit);
    }

    public bool TryBeginEdit(MarkdownHitTestResult hitTestResult)
    {
        ArgumentNullException.ThrowIfNull(hitTestResult);

        if (!IsEditingEnabled)
        {
            return false;
        }

        var session = EditingService.ResolveSession(new MarkdownEditorResolveRequest
        {
            HitTestResult = hitTestResult,
            Preferences = EditorPreferences
        });

        if (session is null)
        {
            return false;
        }

        _activeEditorSession = session;
        RebuildMarkdown();
        return true;
    }

    public void CancelEdit()
    {
        CancelEdit(MarkdownEditCancellationReason.UserRequested);
    }

    private void CancelEdit(MarkdownEditCancellationReason reason)
    {
        if (_activeEditorSession is null)
        {
            return;
        }

        var session = _activeEditorSession;
        _activeEditorSession = null;
        RebuildMarkdown();
        MarkdownEditCanceled?.Invoke(this, new MarkdownEditCanceledEventArgs(session, reason));
    }

    private bool IsCurrentRender(int renderGeneration)
    {
        return renderGeneration == _renderGeneration;
    }

    private MarkdownEditorState? CreateEditorState()
    {
        if (!IsEditingEnabled)
        {
            return null;
        }

        return new MarkdownEditorState
        {
            EditingService = EditingService,
            Preferences = EditorPreferences.Clone(),
            ActiveSession = _activeEditorSession,
            PresentationMode = EditorPresentationMode,
            CommitReplacement = CommitEditorReplacement,
            InsertBlockBefore = InsertEditorBlockBefore,
            InsertBlockAfter = InsertEditorBlockAfter,
            RemoveBlock = RemoveEditorBlock,
            CancelEdit = CancelEdit
        };
    }

    private void CommitEditorReplacement(MarkdownEditorSession session, string replacementMarkdown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(replacementMarkdown);

        var currentMarkdown = Markdown ?? string.Empty;
        var updatedMarkdown = MarkdownSourceEditing.Replace(currentMarkdown, session.SourceSpan, replacementMarkdown);
        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            replacementMarkdown,
            revealStart: session.SourceSpan.Start,
            revealLength: replacementMarkdown.Length,
            MarkdownEditOperation.Replace);
    }

    private void InsertEditorBlockBefore(MarkdownEditorSession session, MarkdownBlockTemplate template, string currentBlockMarkdown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(currentBlockMarkdown);

        var currentMarkdown = Markdown ?? string.Empty;
        var replacementMarkdown = MarkdownSourceEditing.BuildBlockInsertionReplacement(
            currentBlockMarkdown,
            template.Markdown,
            insertBefore: true,
            out var revealStart,
            out var revealLength);
        var updatedMarkdown = MarkdownSourceEditing.Replace(currentMarkdown, session.SourceSpan, replacementMarkdown);

        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            replacementMarkdown,
            session.SourceSpan.Start + revealStart,
            revealLength,
            MarkdownEditOperation.InsertBlockBefore);
    }

    private void InsertEditorBlockAfter(MarkdownEditorSession session, MarkdownBlockTemplate template, string currentBlockMarkdown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(currentBlockMarkdown);

        var currentMarkdown = Markdown ?? string.Empty;
        var replacementMarkdown = MarkdownSourceEditing.BuildBlockInsertionReplacement(
            currentBlockMarkdown,
            template.Markdown,
            insertBefore: false,
            out var revealStart,
            out var revealLength);
        var updatedMarkdown = MarkdownSourceEditing.Replace(currentMarkdown, session.SourceSpan, replacementMarkdown);

        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            replacementMarkdown,
            session.SourceSpan.Start + revealStart,
            revealLength,
            MarkdownEditOperation.InsertBlockAfter);
    }

    private void RemoveEditorBlock(MarkdownEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var currentMarkdown = Markdown ?? string.Empty;
        var updatedMarkdown = MarkdownSourceEditing.Remove(currentMarkdown, session.SourceSpan);

        CompleteEditorChange(
            session,
            currentMarkdown,
            updatedMarkdown,
            string.Empty,
            revealStart: Math.Clamp(session.SourceSpan.Start, 0, updatedMarkdown.Length),
            revealLength: 0,
            MarkdownEditOperation.RemoveBlock);
    }

    private void CompleteEditorChange(
        MarkdownEditorSession session,
        string currentMarkdown,
        string updatedMarkdown,
        string replacementMarkdown,
        int revealStart,
        int revealLength,
        MarkdownEditOperation operation)
    {
        _activeEditorSession = null;

        if (string.Equals(updatedMarkdown, currentMarkdown, StringComparison.Ordinal))
        {
            RebuildMarkdown();
            return;
        }

        revealStart = Math.Clamp(revealStart, 0, updatedMarkdown.Length);
        revealLength = Math.Clamp(revealLength, 0, Math.Max(updatedMarkdown.Length - revealStart, 0));

        SetCurrentValue(MarkdownProperty, updatedMarkdown);
        MarkdownEdited?.Invoke(this, new MarkdownEditedEventArgs(session, replacementMarkdown, updatedMarkdown, revealStart, revealLength, operation));
    }

    private double ResolveAvailableWidth(double availableWidth = double.NaN)
    {
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            return Math.Max(availableWidth, 1);
        }

        var viewportWidth = ResolveViewportWidth();
        if (viewportWidth > 0)
        {
            return Math.Max(viewportWidth, 1);
        }

        return Bounds.Width > 0 ? Math.Max(Bounds.Width, 1) : 640;
    }

    private double ResolveViewportWidth()
    {
        if (this.FindAncestorOfType<ScrollViewer>() is { Viewport.Width: > 0 } scrollViewer)
        {
            return scrollViewer.Viewport.Width;
        }

        return _effectiveViewportWidth > 0 ? _effectiveViewportWidth : 0;
    }

    private void DisposeRenderResources()
    {
        _renderResources?.Dispose();
        _renderResources = null;
    }

    private sealed class TaskListToggleForwardingCommandImpl(MarkdownTextBlock owner) : ICommand
    {
        private ICommand? _source;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            if (parameter is not CheckBox checkBox ||
                !MarkdownTaskListNormalizer.IsInteractive(checkBox) ||
                _source is not { } source ||
                !MarkdownTaskListNormalizer.TryCreateToggleRequest(
                    owner.Markdown ?? string.Empty,
                    checkBox,
                    out var request,
                    checkBox.IsChecked != true))
            {
                return false;
            }

            return source.CanExecute(request);
        }

        public void SetSource(ICommand? source)
        {
            if (ReferenceEquals(_source, source))
                return;

            if (_source is not null)
                _source.CanExecuteChanged -= OnSourceCanExecuteChanged;

            _source = source;

            if (_source is not null)
                _source.CanExecuteChanged += OnSourceCanExecuteChanged;

            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Execute(object? parameter)
        {
            if (parameter is not CheckBox checkBox ||
                _source is not { } source ||
                !MarkdownTaskListNormalizer.TryCreateToggleRequest(
                    owner.Markdown ?? string.Empty,
                    checkBox,
                    out var request) ||
                !source.CanExecute(request))
            {
                if (parameter is CheckBox failed)
                    failed.IsChecked = failed.IsChecked != true;
                return;
            }

            source.Execute(request);
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnSourceCanExecuteChanged(object? sender, EventArgs args) =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public enum MarkdownEditOperation
{
    Replace,
    InsertBlockBefore,
    InsertBlockAfter,
    RemoveBlock
}

public sealed class MarkdownEditedEventArgs(
    MarkdownEditorSession session,
    string replacementMarkdown,
    string updatedMarkdown,
    int revealStart,
    int revealLength,
    MarkdownEditOperation operation) : EventArgs
{
    public MarkdownEditorSession Session { get; } = session;

    public string ReplacementMarkdown { get; } = replacementMarkdown;

    public string UpdatedMarkdown { get; } = updatedMarkdown;

    public int RevealStart { get; } = revealStart;

    public int RevealLength { get; } = revealLength;

    public MarkdownEditOperation Operation { get; } = operation;
}

public enum MarkdownEditCancellationReason
{
    UserRequested,
    MarkdownChanged,
    EditingDisabled
}

public sealed class MarkdownEditCanceledEventArgs(MarkdownEditorSession session, MarkdownEditCancellationReason reason) : EventArgs
{
    public MarkdownEditorSession Session { get; } = session;

    public MarkdownEditCancellationReason Reason { get; } = reason;
}
