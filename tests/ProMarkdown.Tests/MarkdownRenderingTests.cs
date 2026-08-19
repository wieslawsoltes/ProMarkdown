using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProMarkdown.Controls;
using ProMarkdown.Services;
using MarkdownLiteralInline = Markdig.Syntax.Inlines.LiteralInline;
using MarkdownLinkInline = Markdig.Syntax.Inlines.LinkInline;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class MarkdownRenderingTests
{
    [AvaloniaFact]
    public async Task FatalImageLoaderFailuresReachTheDispatcherExceptionBoundary()
    {
        var observed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            if (args.Exception is not OutOfMemoryException)
                return;
            args.Handled = true;
            observed.TrySetResult(args.Exception);
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        try
        {
            var control = new MarkdownTextBlock
            {
                ImageLoader = new FatalImageLoader()
            };
            control.Markdown = "![diagram](https://example.com/diagram.png)";

            var exception = await observed.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            exception.Message.ShouldBe("fatal image failure");
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
        }
    }

    [AvaloniaFact]
    public void SelectionCommandsAndDocumentTextTrackDocumentSelection()
    {
        var control = CreateMarkdown("First paragraph.\n\nSecond paragraph.");
        var notifications = 0;
        control.SelectionChanged += (_, _) => notifications++;

        var documentText = MarkdownSelection.GetDocumentText(control);
        documentText.ShouldContain("First paragraph.");
        documentText.ShouldContain("Second paragraph.");
        control.CanCopyDocumentSelection.ShouldBeFalse();
        control.CopySelectionCommand.CanExecute(null).ShouldBeFalse();

        _ = control.SelectAllCommand.CanExecute(null);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var selectAllEnabled = true;
        for (var iteration = 0; iteration < 100; iteration++)
            selectAllEnabled &= control.SelectAllCommand.CanExecute(null);
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        selectAllEnabled.ShouldBeTrue();
        allocatedBytes.ShouldBe(0);

        control.SelectAllCommand.Execute(null);

        control.CanCopyDocumentSelection.ShouldBeTrue();
        ((SelectableTextBlock)control).CanCopy.ShouldBeFalse();
        control.CopySelectionCommand.CanExecute(null).ShouldBeTrue();
        MarkdownSelection.GetSelectedText(control).Replace("\r\n", "\n", StringComparison.Ordinal)
            .ShouldBe(documentText.Replace("\r\n", "\n", StringComparison.Ordinal));
        MarkdownSelection.GetDocumentText(control).ShouldBe(documentText);
        notifications.ShouldBeGreaterThan(0);
    }

    [AvaloniaFact]
    public void DetachingRaisesCopyCommandStateChanged()
    {
        var control = CreateMarkdown("Selectable text");
        var window = new Window { Width = 320, Height = 120, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            control.SelectAllCommand.Execute(null);
            control.CanCopyDocumentSelection.ShouldBeTrue();

            var commandNotifications = 0;
            var selectionNotifications = 0;
            control.CopySelectionCommand.CanExecuteChanged += OnCanExecuteChanged;
            control.SelectionChanged += OnSelectionChanged;
            try
            {
                window.Content = null;

                control.CanCopyDocumentSelection.ShouldBeFalse();
                control.CopySelectionCommand.CanExecute(null).ShouldBeFalse();
                MarkdownSelection.CanCopy(control).ShouldBeFalse();
                MarkdownSelection.GetSelectedText(control).ShouldBeEmpty();
                commandNotifications.ShouldBe(1);
                selectionNotifications.ShouldBe(1);
            }
            finally
            {
                control.CopySelectionCommand.CanExecuteChanged -= OnCanExecuteChanged;
                control.SelectionChanged -= OnSelectionChanged;
            }

            void OnCanExecuteChanged(object? sender, EventArgs eventArgs) => commandNotifications++;
            void OnSelectionChanged(object? sender, EventArgs eventArgs) => selectionNotifications++;
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RepeatedSelectionRangeDoesNotRaiseDuplicateNotifications()
    {
        var control = CreateMarkdown("Selectable text");
        var segment = MarkdownDocumentSelection.GetSegmentControls(control).Single();
        var selectionNotifications = 0;
        var commandNotifications = 0;
        control.SelectionChanged += (_, _) => selectionNotifications++;
        control.CopySelectionCommand.CanExecuteChanged += (_, _) => commandNotifications++;

        MarkdownDocumentSelection.SelectRange(control, segment, 0, segment, 5);
        MarkdownDocumentSelection.SelectRange(control, segment, 0, segment, 5);

        selectionNotifications.ShouldBe(1);
        commandNotifications.ShouldBe(1);
    }

    [AvaloniaFact]
    public void DynamicSegmentReplacementClearsStaleSelectionCoordinates()
    {
        var content = new StackPanel
        {
            Children =
            {
                new SelectableTextBlock { Text = "first" },
                new SelectableTextBlock { Text = "second" },
                new SelectableTextBlock { Text = "third" }
            }
        };
        var host = new Border { Child = content };
        var control = CreateInlineDocument(host);
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            MarkdownDocumentSelection.SelectRange(control, segments[0], 0, segments[^1], 3);
            control.CanCopyDocumentSelection.ShouldBeTrue();

            host.Child = new SelectableTextBlock { Text = "replacement" };
            window.UpdateLayout();

            control.CanCopyDocumentSelection.ShouldBeFalse();
            control.CopySelectionCommand.CanExecute(null).ShouldBeFalse();
            var replacement = MarkdownDocumentSelection.GetSegmentControls(control).Single();
            Should.NotThrow(() =>
                MarkdownDocumentSelection.SelectRange(control, replacement, 0, replacement, 5));
            MarkdownSelection.GetSelectedText(control).ShouldBe("repla");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DynamicSelectedSegmentInsertionReappliesSelectionAndNotifies()
    {
        var first = new SelectableTextBlock { Text = "first" };
        var second = new SelectableTextBlock { Text = "second" };
        var third = new SelectableTextBlock { Text = "third" };
        var content = new StackPanel { Children = { first, second, third } };
        var control = CreateInlineDocument(content);
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentSelection.SelectRange(control, first, 0, third, 3);
            var notifications = 0;
            control.SelectionChanged += OnSelectionChanged;
            try
            {
                window.Width = 360;
                window.UpdateLayout();
                notifications.ShouldBe(0);

                var inserted = new SelectableTextBlock { Text = "inserted" };
                content.Children.Insert(1, inserted);
                window.UpdateLayout();

                inserted.SelectionStart.ShouldBe(0);
                inserted.SelectionEnd.ShouldBe(inserted.Text!.Length);
                MarkdownSelection.GetSelectedText(control).ShouldContain("inserted");
                notifications.ShouldBe(1);
            }
            finally
            {
                control.SelectionChanged -= OnSelectionChanged;
            }

            void OnSelectionChanged(object? sender, EventArgs eventArgs) => notifications++;
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DynamicSegmentTextChangesRefreshSelectionLength()
    {
        var segment = new SelectableTextBlock { Text = "short" };
        var control = CreateInlineDocument(segment);
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentSelection.GetSegmentControls(control).ShouldBe([segment]);

            segment.Text = "longer dynamic text";
            window.UpdateLayout();
            control.SelectAllCommand.Execute(null);

            segment.SelectionEnd.ShouldBe(segment.Text.Length);
            MarkdownSelection.GetSelectedText(control).ShouldBe(segment.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UnrelatedLayoutPassKeepsTheSelectionSegmentSnapshot()
    {
        var segment = new SelectableTextBlock { Text = "stable" };
        var control = CreateInlineDocument(segment);
        var unrelated = new Border { Height = 20 };
        var root = new StackPanel { Children = { control, unrelated } };
        var window = new Window { Width = 320, Height = 200, Content = root };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentSelection.GetSegmentControls(control).ShouldBe([segment]);
            var snapshotVersion = MarkdownDocumentSelection.GetSegmentSnapshotVersion(control);

            unrelated.Height = 40;
            window.UpdateLayout();
            MarkdownDocumentSelection.GetSegmentControls(control).ShouldBe([segment]);

            MarkdownDocumentSelection.GetSegmentSnapshotVersion(control).ShouldBe(snapshotVersion);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ChildLayoutChangesRefreshSelectionSegmentBoundsWithinFixedOwner()
    {
        var spacer = new Border { Height = 12 };
        var segment = new SelectableTextBlock { Text = "moving segment" };
        var content = new StackPanel { Children = { spacer, segment } };
        var control = CreateInlineDocument(content);
        control.Height = 120;
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentSelection.TryGetSegmentBounds(control, segment, out var initialBounds).ShouldBeTrue();
            var ownerHeight = control.Bounds.Height;

            spacer.Height = 48;
            window.UpdateLayout();

            MarkdownDocumentSelection.TryGetSegmentBounds(control, segment, out var updatedBounds).ShouldBeTrue();
            control.Bounds.Height.ShouldBe(ownerHeight, 0.01);
            updatedBounds.Y.ShouldBeGreaterThan(initialBounds.Y + 30);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MutableRenderTransformChangesRefreshSelectionSegmentBounds()
    {
        var segment = new SelectableTextBlock { Text = "transformed segment" };
        var transform = new TranslateTransform(0, 8);
        var content = new StackPanel
        {
            RenderTransform = transform,
            Children = { segment }
        };
        var control = CreateInlineDocument(content);
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentSelection.TryGetSegmentBounds(control, segment, out var initialBounds).ShouldBeTrue();

            transform.Y = 44;

            MarkdownDocumentSelection.TryGetSegmentBounds(control, segment, out var updatedBounds).ShouldBeTrue();
            updatedBounds.Y.ShouldBe(initialBounds.Y + 36, 0.01);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ScaledSegmentUsesTransformedBoundsAndInverseMappedHitCoordinates()
    {
        var segment = new SelectableTextBlock
        {
            Text = "scaled segment",
            Width = 120
        };
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = new ScaleTransform(1.75, 1.5),
            RenderTransformOrigin = RelativePoint.TopLeft,
            Children = { segment }
        };
        var control = CreateInlineDocument(content);
        var window = new Window { Width = 420, Height = 200, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var expectedLocalPoint = new Point(segment.Bounds.Width * 0.75, segment.Bounds.Height * 0.5);
            var ownerPoint = segment.TranslatePoint(expectedLocalPoint, control).ShouldNotBeNull();

            MarkdownDocumentSelection.TryGetSegmentBounds(control, segment, out var bounds).ShouldBeTrue();
            bounds.Width.ShouldBeGreaterThan(segment.Bounds.Width * 1.7);
            bounds.Height.ShouldBeGreaterThan(segment.Bounds.Height * 1.45);
            MarkdownDocumentSelection.TryHitTestSegment(
                control,
                ownerPoint,
                out var hitSegment,
                out var localPoint,
                out _).ShouldBeTrue();

            hitSegment.ShouldBeSameAs(segment);
            localPoint.X.ShouldBe(expectedLocalPoint.X, 0.01);
            localPoint.Y.ShouldBe(expectedLocalPoint.Y, 0.01);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RotatedSegmentUsesInverseMappedHitCoordinatesAndRejectsEmptyBoundingBoxCorners()
    {
        var segment = new SelectableTextBlock
        {
            Text = "rotated segment",
            Width = 160,
            RenderTransform = new RotateTransform(20),
            RenderTransformOrigin = RelativePoint.Center
        };
        var content = new StackPanel
        {
            Margin = new Thickness(40),
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { segment }
        };
        var control = CreateInlineDocument(content);
        var window = new Window { Width = 520, Height = 260, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var expectedLocalPoint = new Point(segment.Bounds.Width * 0.5, segment.Bounds.Height * 0.5);
            var ownerPoint = segment.TranslatePoint(expectedLocalPoint, control).ShouldNotBeNull();

            MarkdownDocumentSelection.TryGetSegmentBounds(control, segment, out var bounds).ShouldBeTrue();
            MarkdownDocumentSelection.TryHitTestSegment(
                control,
                ownerPoint,
                out var hitSegment,
                out var localPoint,
                out _).ShouldBeTrue();

            hitSegment.ShouldBeSameAs(segment);
            localPoint.X.ShouldBe(expectedLocalPoint.X, 0.01);
            localPoint.Y.ShouldBe(expectedLocalPoint.Y, 0.01);
            MarkdownDocumentSelection.TryHitTestSegment(
                control,
                bounds.TopLeft + new Vector(0.1, 0.1),
                out _,
                out _,
                out _).ShouldBeFalse();

            var emptyOwnerPoint = bounds.TopLeft + new Vector(0.1, 0.1);
            var emptyWindowPoint = control.TranslatePoint(emptyOwnerPoint, window).ShouldNotBeNull();
            var centerWindowPoint = control.TranslatePoint(ownerPoint, window).ShouldNotBeNull();
            window.MouseMove(emptyWindowPoint);
            window.MouseDown(emptyWindowPoint, MouseButton.Left);
            window.MouseMove(centerWindowPoint, RawInputModifiers.LeftMouseButton);
            window.MouseUp(centerWindowPoint, MouseButton.Left);

            control.CanCopyDocumentSelection.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RenderActivityIgnoresStaleGenerationLeases()
    {
        var plugin = new AsyncActivityPlugin();
        var control = CreateMarkdown("first", controller: MarkdownRenderingServices.CreateController(plugin));
        control.IsRendering.ShouldBeTrue();
        var firstLease = plugin.Leases.Last();

        control.Markdown = "second";
        control.IsRendering.ShouldBeTrue();
        var secondLease = plugin.Leases.Last();
        firstLease.Dispose();
        control.IsRendering.ShouldBeTrue();

        var completions = 0;
        control.RenderCompleted += (_, _) => completions++;
        secondLease.Dispose();

        control.IsRendering.ShouldBeFalse();
        completions.ShouldBe(1);
    }

    [AvaloniaFact]
    public void CompletedRenderActivityCannotBeReopenedByLatePluginWork()
    {
        var plugin = new AsyncActivityPlugin();
        var control = CreateMarkdown("render", controller: MarkdownRenderingServices.CreateController(plugin));
        var completions = 0;
        control.RenderCompleted += (_, _) => completions++;

        plugin.Leases.Last().Dispose();
        control.IsRendering.ShouldBeFalse();
        completions.ShouldBe(1);

        plugin.LastContext.ShouldNotBeNull().BeginAsyncOperation().Dispose();

        control.IsRendering.ShouldBeFalse();
        completions.ShouldBe(1);
    }

    [Fact]
    public void DisposedRenderActivityDoesNotRetainItsCompletionTarget()
    {
        var (target, lease) = CreateDisposedRenderActivityWithOutstandingLease();

        for (var attempt = 0; attempt < 3 && target.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        target.IsAlive.ShouldBeFalse();
        GC.KeepAlive(lease);
        lease.Dispose();
    }

    [AvaloniaFact]
    public async Task NestedMarkdownRenderingContributesToTheParentGeneration()
    {
        var loader = new FailingImageLoader();
        var plugin = new NestedMarkdownActivityPlugin(loader);
        var control = CreateMarkdown("nested", controller: MarkdownRenderingServices.CreateController(plugin));
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.RenderCompleted += OnRenderCompleted;

        await loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        control.IsRendering.ShouldBeTrue();

        loader.Fail();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        control.IsRendering.ShouldBeFalse();

        void OnRenderCompleted(object? sender, EventArgs eventArgs) => completed.TrySetResult();
    }

    [AvaloniaFact]
    public async Task NestedRenderingStartedDuringAttachmentKeepsTheParentGenerationActive()
    {
        var plugin = new AttachedNestedMarkdownActivityPlugin();
        var control = new MarkdownTextBlock();
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            control.RenderController = MarkdownRenderingServices.CreateController(plugin);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control.RenderCompleted += OnRenderCompleted;
            try
            {
                control.Markdown = "nested";
                Dispatcher.UIThread.RunJobs();
                plugin.HasOperation.ShouldBeTrue();
                control.IsRendering.ShouldBeTrue();

                plugin.Complete();
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
                control.IsRendering.ShouldBeFalse();
            }
            finally
            {
                control.RenderCompleted -= OnRenderCompleted;
            }

            void OnRenderCompleted(object? sender, EventArgs eventArgs) => completed.TrySetResult();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DetachedNestedRenderingCompletesTheParentGeneration()
    {
        var plugin = new AttachedNestedMarkdownActivityPlugin();
        var control = new MarkdownTextBlock();
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            control.RenderController = MarkdownRenderingServices.CreateController(plugin);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            control.RenderCompleted += OnRenderCompleted;
            try
            {
                control.Markdown = "nested";
                Dispatcher.UIThread.RunJobs();
                plugin.HasOperation.ShouldBeTrue();
                control.IsRendering.ShouldBeTrue();

                plugin.Detach();
                Dispatcher.UIThread.RunJobs();
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

                control.IsRendering.ShouldBeFalse();
            }
            finally
            {
                control.RenderCompleted -= OnRenderCompleted;
                plugin.Complete();
            }

            void OnRenderCompleted(object? sender, EventArgs eventArgs) => completed.TrySetResult();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ParentRerenderCancelsAnUnattachedNestedMarkdownGeneration()
    {
        var loader = new CancellationAwareImageLoader();
        var plugin = new NestedMarkdownActivityPlugin(loader);
        var control = CreateMarkdown("nested", controller: MarkdownRenderingServices.CreateController(plugin));

        await loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        control.IsRendering.ShouldBeTrue();

        control.Markdown = string.Empty;

        await loader.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        control.IsRendering.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void ParentRerenderDisposesCompletedNestedMarkdownResources()
    {
        var plugin = new NestedMarkdownResourcePlugin();
        var control = CreateMarkdown("nested", controller: MarkdownRenderingServices.CreateController(plugin));
        var resource = plugin.LastController.ShouldNotBeNull().LastResource.ShouldNotBeNull();
        resource.IsDisposed.ShouldBeFalse();

        control.Markdown = string.Empty;

        resource.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task ImagePoliciesAndLimitsAreEnforcedWithoutNetworkAccess()
    {
        MarkdownImageOptions.Default.AllowedSourceKinds.ShouldBe(MarkdownImageSourceKinds.All);
        MarkdownImageOptions.EmbeddedOnly.AllowedSourceKinds.ShouldBe(MarkdownImageSourceKinds.Data);
        DefaultMarkdownImageLoader.IsSourceAllowed(
                new Uri("file://server/share/image.png"),
                MarkdownImageOptions.BlockRemote)
            .ShouldBeFalse();
        var blockedRequest = new MarkdownImageLoadRequest
        {
            Source = new Uri("https://example.com/image.png"),
            Options = MarkdownImageOptions.BlockRemote
        };
        await Should.ThrowAsync<InvalidOperationException>(() =>
            DefaultMarkdownImageLoader.Instance.LoadAsync(blockedRequest, TestContext.Current.CancellationToken));

        var oversizedData = new MarkdownImageLoadRequest
        {
            Source = new Uri("data:image/png;base64,AAAA"),
            Options = new MarkdownImageOptions { MaximumBytes = 1 }
        };
        await Should.ThrowAsync<InvalidOperationException>(() =>
            DefaultMarkdownImageLoader.Instance.LoadAsync(oversizedData, TestContext.Current.CancellationToken));

        var pixelData = new Uri("data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFUlEQVR42mNkYGD4z8DAwMDEAAUADikBA4GMbH8AAAAASUVORK5CYII=");
        var pixelLimited = new MarkdownImageLoadRequest
        {
            Source = pixelData,
            Options = new MarkdownImageOptions { MaximumPixelCount = 1 }
        };
        await Should.ThrowAsync<InvalidOperationException>(() =>
            DefaultMarkdownImageLoader.Instance.LoadAsync(pixelLimited, TestContext.Current.CancellationToken));

        byte[] oversizedJpeg =
        [
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            0xFF, 0xFF, 0xFF, 0xFF, 0x03,
            0x01, 0x11, 0x00,
            0x02, 0x11, 0x00,
            0x03, 0x11, 0x00,
            0xFF, 0xD9
        ];
        var oversizedJpegRequest = new MarkdownImageLoadRequest
        {
            Source = new Uri($"data:image/jpeg;base64,{Convert.ToBase64String(oversizedJpeg)}"),
            Options = MarkdownImageOptions.Default
        };
        var jpegException = await Should.ThrowAsync<InvalidOperationException>(() =>
            DefaultMarkdownImageLoader.Instance.LoadAsync(
                oversizedJpegRequest,
                TestContext.Current.CancellationToken));
        jpegException.Message.ShouldBe("The decoded Markdown image exceeds the configured pixel limit.");

        var excessiveTimeout = new MarkdownImageOptions
        {
            RemoteTimeout = TimeSpan.FromMilliseconds(uint.MaxValue)
        };
        Should.Throw<ArgumentOutOfRangeException>(excessiveTimeout.Validate);
    }

    [AvaloniaFact]
    public async Task ImagePolicyIsEnforcedBeforeInvokingACustomLoader()
    {
        var loader = new PolicyRejectingImageLoader();
        var control = new MarkdownTextBlock
        {
            ImageLoader = loader,
            ImageOptions = MarkdownImageOptions.BlockRemote
        };
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.RenderCompleted += OnRenderCompleted;
        try
        {
            control.Markdown = "![Blocked](https://example.com/image.png)";
            await completed.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            loader.InvocationCount.ShouldBe(0);
        }
        finally
        {
            control.RenderCompleted -= OnRenderCompleted;
        }

        void OnRenderCompleted(object? sender, EventArgs args) => completed.TrySetResult();
    }

    [AvaloniaFact]
    public async Task BoundedImageLoaderPreservesWbmpCompatibility()
    {
        byte[] wbmp = [0x00, 0x00, 0x01, 0x01, 0x00];
        var request = new MarkdownImageLoadRequest
        {
            Source = new Uri($"data:image/vnd.wap.wbmp;base64,{Convert.ToBase64String(wbmp)}"),
            Options = MarkdownImageOptions.Default
        };

        using var bitmap = await DefaultMarkdownImageLoader.Instance.LoadAsync(
            request,
            TestContext.Current.CancellationToken);

        bitmap.PixelSize.ShouldBe(new PixelSize(1, 1));
    }

    [AvaloniaFact]
    public async Task BoundedImageLoaderPreservesBitmapCoreHeaderCompatibility()
    {
        var bitmapBytes = new byte[30];
        bitmapBytes[0] = (byte)'B';
        bitmapBytes[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bitmapBytes.AsSpan(2, 4), (uint)bitmapBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bitmapBytes.AsSpan(10, 4), 26);
        BinaryPrimitives.WriteUInt32LittleEndian(bitmapBytes.AsSpan(14, 4), 12);
        BinaryPrimitives.WriteUInt16LittleEndian(bitmapBytes.AsSpan(18, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bitmapBytes.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bitmapBytes.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bitmapBytes.AsSpan(24, 2), 24);

        var request = new MarkdownImageLoadRequest
        {
            Source = new Uri($"data:image/bmp;base64,{Convert.ToBase64String(bitmapBytes)}"),
            Options = MarkdownImageOptions.Default
        };

        using var bitmap = await DefaultMarkdownImageLoader.Instance.LoadAsync(
            request,
            TestContext.Current.CancellationToken);

        bitmap.PixelSize.ShouldBe(new PixelSize(1, 1));
    }

    [Fact]
    public async Task IcoPixelLimitUsesEmbeddedPayloadDimensions()
    {
        var ico = new byte[46];
        ico[2] = 1;
        ico[4] = 1;
        ico[6] = 1;
        ico[7] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(14, 4), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(18, 4), 22);
        "\x89PNG\r\n\x1a\n"u8.CopyTo(ico.AsSpan(22));
        BinaryPrimitives.WriteInt32BigEndian(ico.AsSpan(38, 4), 4096);
        BinaryPrimitives.WriteInt32BigEndian(ico.AsSpan(42, 4), 4096);
        var request = new MarkdownImageLoadRequest
        {
            Source = new Uri($"data:image/x-icon;base64,{Convert.ToBase64String(ico)}"),
            Options = new MarkdownImageOptions { MaximumPixelCount = 1024 }
        };

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            DefaultMarkdownImageLoader.Instance.LoadAsync(
                request,
                TestContext.Current.CancellationToken));

        exception.Message.ShouldBe("The decoded Markdown image exceeds the configured pixel limit.");
    }

    [Fact]
    public async Task BoundedImageStreamNeverReadsOrSeeksPastItsLimit()
    {
        await using var stream = new MarkdownBoundedReadStream(
            new MemoryStream(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
            8);
        var buffer = new byte[32];

        (await stream.ReadAsync(buffer, TestContext.Current.CancellationToken)).ShouldBe(8);
        stream.Position.ShouldBe(8);
        (await stream.ReadAsync(buffer, TestContext.Current.CancellationToken)).ShouldBe(0);
        Should.Throw<ArgumentOutOfRangeException>(() => stream.Seek(1, SeekOrigin.End));
    }

    [AvaloniaFact]
    public async Task RerenderCancelsAnInFlightImageLoadAndCompletesItsGeneration()
    {
        var loader = new CancellationAwareImageLoader();
        var control = new MarkdownTextBlock { ImageLoader = loader };
        control.Markdown = "![Pending](data:image/png;base64,AAAA)";

        await loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        control.IsRendering.ShouldBeTrue();

        control.Markdown = "Replacement";

        await loader.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        control.IsRendering.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task UnexpectedImageLoaderFailuresBecomeRenderedErrorState()
    {
        var uiThread = Environment.CurrentManagedThreadId;
        var loader = new FailingImageLoader();
        var control = new MarkdownTextBlock { ImageLoader = loader };
        control.Markdown = "![Broken](data:image/png;base64,AAAA)";
        await loader.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        loader.ThreadId.ShouldNotBe(uiThread);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        control.RenderCompleted += OnRenderCompleted;

        try
        {
            loader.Fail();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            control.IsRendering.ShouldBeFalse();
            var hasRenderedFailure = EnumerateControls(control.Inlines!)
                .OfType<TextBlock>()
                .Any(text => text.Text is { } value &&
                             value.Contains("Unexpected loader failure", StringComparison.Ordinal));
            hasRenderedFailure.ShouldBeTrue();
        }
        finally
        {
            control.RenderCompleted -= OnRenderCompleted;
        }

        void OnRenderCompleted(object? sender, EventArgs args) => completed.TrySetResult();
    }

    [AvaloniaTheory]
    [InlineData(14d)]
    [InlineData(18d)]
    public void InlineFormattingInheritsDocumentFontSize(double fontSize)
    {
        var control = CreateMarkdown(
            "Plain [link](https://example.com) **bold** *italic* ***both*** ~~strike~~ ==mark== ++insert++.",
            fontSize);

        var runs = EnumerateInlines(control.Inlines!).OfType<Run>().ToArray();
        foreach (var text in new[] { "link", "bold", "italic", "both", "strike", "mark", "insert" })
            runs.Single(run => run.Text == text).FontSize.ShouldBe(fontSize, 0.01);
    }

    [AvaloniaFact]
    public void FormattedLinksPreserveInlineStructureSelectionAndInteraction()
    {
        const string text = "A bold and italic link.";
        var hyperlink = new SolidColorBrush(Color.Parse("#FF2563EB"));
        var palette = new MarkdownThemePalette { HyperlinkForeground = hyperlink };
        var control = CreateMarkdown(
            "A [**bold** and *italic*](https://example.com) link.",
            palette: palette);
        var window = new Window { Width = 480, Height = 140, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var inlines = EnumerateInlines(control.Inlines!).ToArray();
            inlines.OfType<Bold>().Single().Inlines.OfType<Run>().Single().Text.ShouldBe("bold");
            inlines.OfType<Italic>().Single().Inlines.OfType<Run>().Single().Text.ShouldBe("italic");
            inlines.OfType<Span>().ShouldContain(span =>
                ReferenceEquals(span.Foreground, hyperlink) &&
                span.TextDecorations == TextDecorations.Underline);

            MarkdownDocumentSelection.SelectAll(control);
            MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(text);

            var segment = MarkdownDocumentSelection.GetSegmentControls(control)
                .Single(candidate => GetText(candidate) == text);
            var ownerPoint = GetOwnerPoint(control, segment, text.IndexOf("bold", StringComparison.Ordinal) + 1);
            var result = control.HitTestMarkdown(ownerPoint);
            result.ShouldNotBeNull();
            result.AstNode.Node.ShouldBeOfType<MarkdownLiteralInline>();
            ShouldHaveSelfOrAncestor<MarkdownLinkInline>(result);
            window.MouseMove(GetWindowPoint(control, segment, window, text.IndexOf("bold", StringComparison.Ordinal) + 1));
            control.Cursor.ShouldNotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CopyIncludesInlineAdornmentText()
    {
        var control = CreateMarkdown("Before ==mark== ++insert++ H~2~ and 2^10^ after.");

        MarkdownDocumentSelection.SelectAll(control);

        MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
            "Before mark insert H2 and 210 after.");
    }

    [AvaloniaFact]
    public async Task PublicSelectionApiSelectsAndCopiesRenderedText()
    {
        var expected = $"First paragraph.{Environment.NewLine}{Environment.NewLine}Second paragraph.";
        var control = CreateMarkdown("First paragraph.\n\nSecond paragraph.");
        var window = new Window { Width = 480, Height = 160, Content = control };
        window.Show();

        try
        {
            window.UpdateLayout();
            MarkdownSelection.CanCopy(control).ShouldBeFalse();

            MarkdownSelection.SelectAll(control);

            MarkdownSelection.CanCopy(control).ShouldBeTrue();
            MarkdownSelection.GetSelectedText(control).ShouldBe(expected);
            await MarkdownSelection.CopyAsync(control);
            var clipboard = TopLevel.GetTopLevel(control)?.Clipboard
                            ?? throw new InvalidOperationException("The Markdown control was not attached to a clipboard-enabled top level.");
            (await clipboard.TryGetTextAsync()).ShouldBe(expected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HeadingAndScriptsRetainIntentionalScaling()
    {
        var control = CreateMarkdown("# Heading\n\nH~2~O and 2^10^.", 14);
        var text = control.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
        text.Single(block => GetText(block) == "Heading").FontSize.ShouldBeGreaterThan(14);
        text.Where(block => GetText(block) is "2" or "10")
            .ShouldAllBe(block => block.FontSize < 14);
    }

    [AvaloniaFact]
    public void HeadingsUseTheThemePaletteForeground()
    {
        var control = CreateMarkdown(
            "# ATX heading\n\nSetext heading\n==============",
            foreground: Brushes.Black,
            palette: MarkdownThemePalette.Dark);
        var headings = control.GetVisualDescendants()
            .OfType<SelectableTextBlock>()
            .Where(block => GetText(block).EndsWith("heading", StringComparison.Ordinal))
            .ToArray();

        headings.Length.ShouldBe(2);
        headings.ShouldAllBe(block => ReferenceEquals(block.Foreground, MarkdownThemePalette.Dark.Foreground));
    }

    [AvaloniaFact]
    public void ImplicitThemePalettePreservesTheConfiguredForeground()
    {
        var foreground = new SolidColorBrush(Color.Parse("#FFCC3300"));
        var control = CreateMarkdown("# Heading\n\nBody", foreground: foreground);
        var renderedText = control.GetVisualDescendants()
            .OfType<SelectableTextBlock>()
            .Where(block => GetText(block) is "Heading" or "Body")
            .ToArray();

        renderedText.Length.ShouldBe(2);
        renderedText.ShouldAllBe(block => ReferenceEquals(block.Foreground, foreground));
    }

    [AvaloniaFact]
    public void DarkPaletteThemesLinksTablesCodeAndAlerts()
    {
        var controller = MarkdownRenderingServices.CreateController(
            new ProMarkdown.Plugin.Alerts.AlertsMarkdownPlugin());
        var control = CreateMarkdown(
            "[link](https://example.com)\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n~~~text\ncode\n~~~\n\n> [!CAUTION]\n> danger",
            14,
            Brushes.White,
            MarkdownThemePalette.Dark,
            controller);

        var colors = EnumerateControls(control.Inlines!)
            .Select(GetBackground)
            .OfType<ISolidColorBrush>()
            .Select(brush => brush.Color)
            .ToArray();
        colors.ShouldNotContain(Colors.White);

        var link = EnumerateInlines(control.Inlines!)
            .OfType<Span>()
            .Single(span => span.Inlines.OfType<Run>().Any(run => run.Text == "link"));
        ((ISolidColorBrush)link.Foreground!).Color.ShouldBe(Color.Parse("#58A6FF"));
    }

    [AvaloniaFact]
    public void InlineFormattingUsesPaletteWithoutPostNormalization()
    {
        var markedBackground = new SolidColorBrush(Color.Parse("#FF12AB34"));
        var insertedForeground = new SolidColorBrush(Color.Parse("#FF3412AB"));
        var palette = new MarkdownThemePalette
        {
            MarkedTextBackground = markedBackground,
            InsertedTextForeground = insertedForeground
        };
        var control = CreateMarkdown("==marked== ++inserted++", palette: palette);
        var spans = EnumerateInlines(control.Inlines!).OfType<Span>().ToArray();

        spans.Single(span => span.Inlines.OfType<Run>().Any(run => run.Text == "marked"))
            .Background.ShouldBeSameAs(markedBackground);
        spans.Single(span => span.Inlines.OfType<Run>().Any(run => run.Text == "inserted"))
            .Foreground.ShouldBeSameAs(insertedForeground);
    }

    [AvaloniaFact]
    public void ThemePaletteDoesNotRewriteThirdPartyPluginColors()
    {
        var background = new SolidColorBrush(Color.Parse("#FF123456"));
        var plugin = new FixedColorBlockPlugin(background);
        var controller = MarkdownRenderingServices.CreateController(plugin);
        _ = CreateMarkdown("third party", palette: MarkdownThemePalette.Dark, controller: controller);

        plugin.LastBorder.ShouldNotBeNull();
        plugin.LastBorder.Background.ShouldBeSameAs(background);
    }

    [AvaloniaFact]
    public void ThirdPartyPluginBindingsRemainActive()
    {
        var source = new BrushBindingSource(new SolidColorBrush(Color.Parse("#FFFFFFFF")));
        var plugin = new FixedColorBlockPlugin(Brushes.Transparent);
        var border = plugin.LastBorder = new Border();
        using var binding = border.Bind(
            Border.BackgroundProperty,
            new Binding(nameof(BrushBindingSource.Brush))
            {
                Source = source,
                Mode = BindingMode.OneWay
            });
        plugin.UseExistingBorder = true;
        var controller = MarkdownRenderingServices.CreateController(plugin);
        var control = new MarkdownTextBlock
        {
            FontSize = 14,
            Foreground = Brushes.Black,
            ThemePalette = MarkdownThemePalette.Dark,
            RenderController = controller,
            TextWrapping = TextWrapping.Wrap
        };
        control.Markdown = "third party";

        border.Background.ShouldBeSameAs(source.Brush);
        var replacement = new SolidColorBrush(Color.Parse("#FF123456"));
        source.Brush = replacement;
        border.Background.ShouldBeSameAs(replacement);
    }

    [AvaloniaFact]
    public void SelectionBrushSynchronizationPreservesSegmentBindings()
    {
        var source = new BrushBindingSource(null);
        var segment = new SelectableTextBlock { Text = "bound selection" };
        using var binding = segment.Bind(
            SelectableTextBlock.SelectionBrushProperty,
            new Binding(nameof(BrushBindingSource.Brush))
            {
                Source = source,
                Mode = BindingMode.OneWay
            });
        var control = CreateInlineDocument(segment);

        control.SelectionBrush = Brushes.Orange;
        var replacement = new SolidColorBrush(Color.Parse("#FF123456"));
        source.Brush = replacement;

        segment.SelectionBrush.ShouldBeSameAs(replacement);
    }

    [AvaloniaFact]
    public void DarkPaletteThemesEveryAlertAndKeepsWarningDistinctFromCaution()
    {
        var controller = MarkdownRenderingServices.CreateController(
            new ProMarkdown.Plugin.Alerts.AlertsMarkdownPlugin());
        var control = CreateMarkdown(
            "> [!NOTE]\n> Note body.\n\n" +
            "> [!TIP]\n> Tip body.\n\n" +
            "> [!IMPORTANT]\n> Important body.\n\n" +
            "> [!WARNING]\n> Warning body.\n\n" +
            "> [!CAUTION]\n> Caution body.",
            14,
            MarkdownThemePalette.Dark.Foreground,
            MarkdownThemePalette.Dark,
            controller);

        var expected = new Dictionary<string, (IBrush Background, IBrush Accent)>(StringComparer.Ordinal)
        {
            ["Note"] = (MarkdownThemePalette.Dark.NoteBackground, MarkdownThemePalette.Dark.NoteAccent),
            ["Tip"] = (MarkdownThemePalette.Dark.TipBackground, MarkdownThemePalette.Dark.TipAccent),
            ["Important"] = (MarkdownThemePalette.Dark.ImportantBackground, MarkdownThemePalette.Dark.ImportantAccent),
            ["Warning"] = (MarkdownThemePalette.Dark.WarningBackground, MarkdownThemePalette.Dark.WarningAccent),
            ["Caution"] = (MarkdownThemePalette.Dark.CautionBackground, MarkdownThemePalette.Dark.CautionAccent)
        };

        foreach (var pair in expected)
        {
            var surface = EnumerateControls(control.Inlines!)
                .OfType<Border>()
                .Single(border => border.Background is not null &&
                                  EnumerateControls(border).OfType<TextBlock>()
                                      .Any(text => text.Text == pair.Key));
            var title = EnumerateControls(surface).OfType<TextBlock>()
                .Single(text => text.Text == pair.Key);
            var rail = EnumerateControls(surface).OfType<Border>()
                .Single(border => Math.Abs(border.Width - 4) < 0.01);

            surface.Background.ShouldBeSameAs(pair.Value.Background);
            title.Foreground.ShouldBeSameAs(pair.Value.Accent);
            rail.Background.ShouldBeSameAs(pair.Value.Accent);
        }

        MarkdownThemePalette.Dark.WarningBackground.ShouldNotBeSameAs(MarkdownThemePalette.Dark.CautionBackground);
        MarkdownThemePalette.Dark.WarningAccent.ShouldNotBeSameAs(MarkdownThemePalette.Dark.CautionAccent);
    }

    [AvaloniaFact]
    public void DarkPaletteThemesNeutralCustomContainers()
    {
        var control = CreateMarkdown(
            ":::details\nNeutral body.\n:::",
            foreground: MarkdownThemePalette.Dark.Foreground,
            palette: MarkdownThemePalette.Dark);

        var surface = EnumerateControls(control.Inlines!)
            .OfType<Border>()
            .Single(border => ReferenceEquals(
                border.Background,
                MarkdownThemePalette.Dark.SurfaceRaised));

        surface.Background.ShouldBeSameAs(MarkdownThemePalette.Dark.SurfaceRaised);
    }

    [AvaloniaFact]
    public void NestedMarkdownUsesTheParentCustomPalette()
    {
        var hyperlink = new SolidColorBrush(Color.Parse("#FF2563EB"));
        var noteAccent = new SolidColorBrush(Color.Parse("#FFB91C1C"));
        var palette = new MarkdownThemePalette
        {
            HyperlinkForeground = hyperlink,
            NoteAccent = noteAccent
        };
        var controller = MarkdownRenderingServices.CreateController(
            new ProMarkdown.Plugin.Alerts.AlertsMarkdownPlugin());
        var control = CreateMarkdown(
            "> [!NOTE]\n> [nested link](https://example.com)",
            palette: palette,
            controller: controller);

        var nested = EnumerateControls(control.Inlines!)
            .OfType<MarkdownTextBlock>()
            .Single();
        nested.ThemePalette.ShouldBeSameAs(palette);
        var link = EnumerateInlines(nested.Inlines!)
            .OfType<Span>()
            .Single(span => span.Inlines.OfType<Run>().Any(run => run.Text == "nested link"));
        link.Foreground.ShouldBeSameAs(hyperlink);
    }

    [Fact]
    public void DerivedForegroundPalettePreservesSemanticBrushes()
    {
        var mutedForeground = new SolidColorBrush(Color.Parse("#FF9CA3AF"));
        var palette = new MarkdownThemePalette
        {
            IsDark = true,
            Foreground = Brushes.White,
            MutedForeground = mutedForeground,
            Accent = Brushes.CornflowerBlue,
            Border = Brushes.DimGray
        };
        var derived = palette.WithForeground(mutedForeground);

        derived.ShouldNotBeSameAs(palette);
        derived.IsDark.ShouldBeTrue();
        derived.Foreground.ShouldBeSameAs(mutedForeground);
        derived.MutedForeground.ShouldBeSameAs(palette.MutedForeground);
        derived.Accent.ShouldBeSameAs(palette.Accent);
        derived.Border.ShouldBeSameAs(palette.Border);
    }

    [AvaloniaFact]
    public void FirstNonEmptyRenderRetainsItsInlines()
    {
        var control = new MarkdownTextBlock
        {
            FontSize = 14,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap
        };

        control.Markdown = "First render";

        MarkdownDocumentSelection.GetSegmentControls(control)
            .Single(segment => GetText(segment) == "First render");
    }

    [AvaloniaFact]
    public void TaskListsUseReadOnlyNativeCheckBoxesByDefault()
    {
        var control = CreateMarkdown("- [x] done\n- [ ] todo");
        var checkBoxes = control.GetVisualDescendants().OfType<CheckBox>().ToArray();
        checkBoxes.Length.ShouldBe(2);
        checkBoxes[0].IsChecked.ShouldBe(true);
        checkBoxes[1].IsChecked.ShouldBe(false);
        checkBoxes.ShouldAllBe(checkBox => !checkBox.IsHitTestVisible && !checkBox.Focusable);
    }

    [AvaloniaFact]
    public void BlockSelectionKeepsTaskListItemsSeparate()
    {
        var control = CreateMarkdown("- [ ] first task\n- [ ] second task");
        var segments = MarkdownDocumentSelection.GetSegmentControls(control);
        var first = segments.Single(segment => GetText(segment).Contains("first task", StringComparison.Ordinal));
        var second = segments.Single(segment => GetText(segment).Contains("second task", StringComparison.Ordinal));

        MarkdownDocumentSelection.SelectBlock(control, first);

        Math.Max(first.SelectionStart, first.SelectionEnd).ShouldBe(GetText(first).Length);
        second.SelectionStart.ShouldBe(second.SelectionEnd);
    }

    [AvaloniaFact]
    public void InteractiveTaskListCommandReceivesExactSourceChange()
    {
        MarkdownTaskListToggleRequest? received = null;
        var control = CreateMarkdown("- [ ] same\n- [ ] same");
        control.TaskListToggleCommand = new DelegateCommand(value => received = (MarkdownTaskListToggleRequest)value!);
        control.IsTaskListInteractive = true;
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();

            var second = MarkdownTaskListNormalizer.EnumerateCheckBoxes(control.Inlines).ElementAt(1);
            second.IsChecked = true;
            second.Command!.Execute(second.CommandParameter);

            received.ShouldNotBeNull();
            received.MarkerOffset.ShouldBe(control.Markdown!.LastIndexOf("[ ]", StringComparison.Ordinal) + 1);
            received.UpdatedMarkdown.ShouldBe("- [ ] same\n- [x] same");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InteractiveTaskListTracksCommandAvailabilityAndUnsubscribesOnDetach()
    {
        var command = new MutableCommand(canExecute: false);
        var control = CreateMarkdown("- [ ] task");
        control.TaskListToggleCommand = command;
        control.IsTaskListInteractive = true;
        var window = new Window { Width = 320, Height = 120, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var checkBox = MarkdownTaskListNormalizer.EnumerateCheckBoxes(control.Inlines).Single();
            command.SubscriberCount.ShouldBe(1);
            checkBox.Command!.CanExecute(checkBox).ShouldBeFalse();

            command.SetCanExecute(true);

            checkBox.Command.CanExecute(checkBox).ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }

        command.SubscriberCount.ShouldBe(0);
    }

    [AvaloniaFact]
    public void OuterDocumentDoesNotConfigureNestedTaskListCheckBoxes()
    {
        var command = new MutableCommand(canExecute: true);
        var controller = MarkdownRenderingServices.CreateController(
            new ProMarkdown.Plugin.Alerts.AlertsMarkdownPlugin());
        var control = CreateMarkdown(
            "> [!NOTE]\n> - [ ] nested task",
            controller: controller);
        control.TaskListToggleCommand = command;
        control.IsTaskListInteractive = true;
        var window = new Window { Width = 320, Height = 180, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            control.Markdown += "\n";
            window.UpdateLayout();

            var nested = EnumerateControls(control.Inlines!)
                .OfType<MarkdownTextBlock>()
                .Single();
            var checkBox = MarkdownTaskListNormalizer.EnumerateCheckBoxes(nested.Inlines).Single();
            checkBox.Command.ShouldBeNull();
            checkBox.IsHitTestVisible.ShouldBeFalse();
            checkBox.Focusable.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WidthChangesReflowWithoutRegeneratingTheDocument()
    {
        var control = CreateMarkdown(
            "This paragraph is intentionally long enough to wrap when the available width becomes narrow.");
        var window = new Window { Width = 600, Height = 180, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var original = control.LastRenderResult;
            window.Width = 260;
            window.UpdateLayout();
            control.LastRenderResult.ShouldBeSameAs(original);
            control.GetVisualDescendants()
                .OfType<SelectableTextBlock>()
                .Single(block => GetText(block).StartsWith("This paragraph", StringComparison.Ordinal))
                .TextWrapping.ShouldBe(TextWrapping.Wrap);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ParagraphsWrapBelowTheFormerMinimumDocumentWidth()
    {
        const string text = "Narrow Markdown content must wrap instead of being clipped by a minimum width.";
        var control = CreateMarkdown(text);
        var window = new Window { Width = 120, Height = 240, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            var paragraph = MarkdownDocumentSelection.GetSegmentControls(control)
                .Single(segment => GetText(segment) == text);
            paragraph.MaxWidth.ShouldBeLessThanOrEqualTo(control.Bounds.Width);
            paragraph.TextLayout.TextLines.Count.ShouldBeGreaterThan(1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NestedBlockContentExpandsWhenTheDocumentBecomesWider()
    {
        const string text = "Quoted content should expand again after the document becomes wider.";
        var control = CreateMarkdown($"> {text}");
        control.Width = 140;
        var window = new Window { Width = 640, Height = 240, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            var paragraph = MarkdownDocumentSelection.GetSegmentControls(control)
                .Single(segment => GetText(segment) == text);
            var narrowWidth = paragraph.MaxWidth;

            control.Width = 620;
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            paragraph.MaxWidth.ShouldBeGreaterThan(narrowWidth + 200);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ThematicBreaksStretchAndRoundedBordersKeepTheirStrokeUnclipped()
    {
        var control = CreateMarkdown("---\n\n| A |\n|---|\n| B |\n\n~~~text\nvalue\n~~~");
        var window = new Window { Width = 420, Height = 320, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var rules = control.GetVisualDescendants().OfType<MarkdownThematicBreak>().ToArray();
            rules.Length.ShouldBe(1);
            rules[0].Bounds.Width.ShouldBeGreaterThan(300);

            var roundedBorders = EnumerateControls(control.Inlines!)
                .OfType<Border>()
                .Where(border => HasRoundedCorner(border.CornerRadius) && HasBorder(border.BorderThickness))
                .ToArray();
            roundedBorders.Length.ShouldBeGreaterThanOrEqualTo(2);
            roundedBorders.ShouldAllBe(border => !border.ClipToBounds);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ThematicBreakUsesItsContainingTextFlowWidth()
    {
        var rule = new MarkdownThematicBreak();
        var quoteContent = new TextBlock
        {
            Padding = new Thickness(24, 0),
            Inlines = new InlineCollection { new InlineUIContainer(rule) }
        };
        var quoteSurface = new Border
        {
            Padding = new Thickness(20, 0),
            Child = quoteContent
        };
        var control = CreateInlineDocument(quoteSurface);
        var window = new Window { Width = 420, Height = 220, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            var ruleOrigin = rule.TranslatePoint(default, control)
                             ?? throw new InvalidOperationException("The quoted rule was not positioned in the document.");
            var contentOrigin = quoteContent.TranslatePoint(default, control)
                                ?? throw new InvalidOperationException("The quote content was not positioned in the document.");

            ruleOrigin.X.ShouldBeGreaterThanOrEqualTo(contentOrigin.X - 0.01);
            (ruleOrigin.X + rule.Bounds.Width).ShouldBeLessThanOrEqualTo(
                contentOrigin.X + quoteContent.Bounds.Width + 0.01);
            var expectedWidth = quoteContent.Bounds.Width -
                                quoteContent.Padding.Left -
                                quoteContent.Padding.Right;
            Math.Abs(rule.Bounds.Width - expectedWidth).ShouldBeLessThan(
                1,
                $"rule bounds={rule.Bounds.Width}, rule width={rule.Width}, content bounds={quoteContent.Bounds.Width}");
            rule.Bounds.Width.ShouldBeLessThan(control.Bounds.Width);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DragSelectionKeepsThePressedCharacterAsAnchor()
    {
        const string firstText = "Anchor paragraph with enough text.";
        const string secondText = "Focus paragraph with enough text.";
        var control = CreateMarkdown($"{firstText}\n\n{secondText}");
        var segments = MarkdownDocumentSelection.GetSegmentControls(control).ToArray();
        var first = segments.Single(segment => GetText(segment) == firstText);
        var second = segments.Single(segment => GetText(segment) == secondText);

        MarkdownDocumentSelection.SelectRange(control, first, 8, second, 8);

        Math.Min(first.SelectionStart, first.SelectionEnd).ShouldBe(8);
        Math.Max(first.SelectionStart, first.SelectionEnd).ShouldBe(firstText.Length);
        Math.Min(second.SelectionStart, second.SelectionEnd).ShouldBe(0);
        Math.Max(second.SelectionStart, second.SelectionEnd).ShouldBe(8);
    }

    [AvaloniaFact]
    public void DoubleAndTripleClickUseWordAndBlockGranularity()
    {
        const string text = "Alpha selectable omega.";
        var control = CreateMarkdown(text);
        var segment = MarkdownDocumentSelection.GetSegmentControls(control)
            .Single(candidate => GetText(candidate) == text);
        var start = text.IndexOf("selectable", StringComparison.Ordinal);

        MarkdownDocumentSelection.SelectWord(control, segment, start + 3);
        Math.Min(segment.SelectionStart, segment.SelectionEnd).ShouldBe(start);
        Math.Max(segment.SelectionStart, segment.SelectionEnd).ShouldBe(start + "selectable".Length);

        MarkdownDocumentSelection.SelectBlock(control, segment);
        Math.Min(segment.SelectionStart, segment.SelectionEnd).ShouldBe(0);
        Math.Max(segment.SelectionStart, segment.SelectionEnd).ShouldBe(text.Length);
    }

    [AvaloniaFact]
    public void RoutedPointerDragSelectsAcrossRenderedParagraphs()
    {
        const string firstText = "Anchor paragraph with enough text.";
        const string secondText = "Focus paragraph with enough text.";
        var control = CreateMarkdown($"{firstText}\n\n{secondText}");
        var window = new Window { Width = 600, Height = 180, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            var first = segments.Single(segment => GetText(segment) == firstText);
            var second = segments.Single(segment => GetText(segment) == secondText);
            var anchor = GetWindowPoint(control, first, window, 8);
            var focus = GetWindowPoint(control, second, window, 8);

            window.MouseMove(anchor);
            window.MouseDown(anchor, MouseButton.Left);
            window.MouseMove(focus, RawInputModifiers.LeftMouseButton);
            window.MouseUp(focus, MouseButton.Left);

            Math.Min(first.SelectionStart, first.SelectionEnd).ShouldBeGreaterThan(0);
            Math.Max(first.SelectionStart, first.SelectionEnd).ShouldBe(firstText.Length);
            Math.Min(second.SelectionStart, second.SelectionEnd).ShouldBe(0);
            Math.Max(second.SelectionStart, second.SelectionEnd).ShouldBeGreaterThan(0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ThirdPartySelectableTextBlockMaxWidthIsPreserved()
    {
        var selectableText = new SelectableTextBlock
        {
            MaxWidth = 640,
            Text = "Custom render controllers can return ordinary selectable text that must wrap with the document.",
            TextWrapping = TextWrapping.Wrap
        };
        var control = CreateInlineDocument(selectableText);
        var window = new Window { Width = 180, Height = 220, Content = control };
        window.Show();

        try
        {
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            selectableText.MaxWidth.ShouldBe(640);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SegmentHitTestingUsesVisibleTextCoordinatesAfterScrolling()
    {
        var paragraphs = Enumerable.Range(1, 30)
            .Select(index => new SelectableTextBlock
            {
                Text = $"Paragraph {index} with selectable text.",
                TextWrapping = TextWrapping.Wrap
            })
            .ToArray();
        var stack = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 48),
            RenderTransform = new TranslateTransform(0, 48)
        };
        stack.Children.AddRange(paragraphs);
        var control = CreateInlineDocument(stack);
        var scrollViewer = new ScrollViewer
        {
            BringIntoViewOnFocusChange = false,
            Height = 120,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = control
        };
        var window = new Window { Width = 600, Height = 160, Content = scrollViewer };
        window.Show();

        try
        {
            window.UpdateLayout();
            var target = paragraphs[14];
            var targetOrigin = target.TranslatePoint(default, control).ShouldNotBeNull();
            scrollViewer.Offset = new Vector(0, targetOrigin.Y - 60);
            window.UpdateLayout();
            var initialOffset = scrollViewer.Offset;
            var targetCharacter = target.TextLayout.HitTestTextPosition(4);
            var targetOwnerPoint = target.TranslatePoint(
                new Point(
                    target.Padding.Left + targetCharacter.X + Math.Max(targetCharacter.Width, 1) / 2,
                    target.Padding.Top + targetCharacter.Y + targetCharacter.Height / 2),
                control).ShouldNotBeNull();
            MarkdownDocumentSelection.TryHitTestSegment(
                control,
                targetOwnerPoint,
                out var hitSegment,
                out _,
                out var hitBounds).ShouldBeTrue();
            hitSegment.ShouldBeSameAs(target);
            Math.Abs(hitBounds.X - targetOrigin.X).ShouldBeLessThan(0.01);
            Math.Abs(hitBounds.Y - targetOrigin.Y).ShouldBeLessThan(0.01);
            scrollViewer.Offset.Y.ShouldBe(initialOffset.Y, 0.01);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PointerSelectionDoesNotMoveTheContainingScrollViewerWhenTakingFocus()
    {
        var markdown = string.Join(
            "\n\n",
            Enumerable.Range(1, 30).Select(index => $"Paragraph {index} with selectable text."));
        var control = CreateMarkdown(markdown);
        var scrollViewer = new ScrollViewer
        {
            Height = 120,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = control
        };
        var window = new Window { Width = 600, Height = 160, Content = scrollViewer };
        window.Show();

        try
        {
            window.UpdateLayout();
            var target = MarkdownDocumentSelection.GetSegmentControls(control)
                .Single(candidate => GetText(candidate).StartsWith("Paragraph 15 ", StringComparison.Ordinal));
            MarkdownDocumentSelection.TryGetSegmentBounds(control, target, out var targetBounds).ShouldBeTrue();
            scrollViewer.Offset = new Vector(0, targetBounds.Y - 60);
            window.UpdateLayout();
            var initialOffset = scrollViewer.Offset;
            var anchor = GetWindowPoint(control, target, window, 4);
            var focus = GetWindowPoint(control, target, window, 18);

            ResetClickSequence(window, anchor);
            window.MouseMove(anchor);
            window.MouseDown(anchor, MouseButton.Left);
            window.MouseMove(focus, RawInputModifiers.LeftMouseButton);
            window.MouseUp(focus, MouseButton.Left);

            scrollViewer.Offset.Y.ShouldBe(initialOffset.Y, 0.01);
            window.FocusManager?.GetFocusedElement().ShouldBe(control);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RoutedPointerDragUsesOneSelectionOwnerAcrossNestedMarkdown()
    {
        const string nestedText = "Nested paragraph with enough text.";
        const string followingText = "Following paragraph with enough text.";
        var controller = MarkdownRenderingServices.CreateController(
            new ProMarkdown.Plugin.Alerts.AlertsMarkdownPlugin());
        var control = CreateMarkdown(
            $"> [!NOTE]\n> {nestedText}\n\n{followingText}",
            controller: controller);
        var window = new Window { Width = 600, Height = 240, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            segments.ShouldNotContain(segment => segment is MarkdownTextBlock);
            var nested = segments.Single(segment => GetText(segment) == nestedText);
            var following = segments.Single(segment => GetText(segment) == followingText);
            var anchor = GetWindowPoint(control, nested, window, 7);
            var focus = GetWindowPoint(control, following, window, 9);

            window.MouseMove(anchor);
            window.MouseDown(anchor, MouseButton.Left);
            window.MouseMove(focus, RawInputModifiers.LeftMouseButton);
            window.MouseUp(focus, MouseButton.Left);

            Math.Min(nested.SelectionStart, nested.SelectionEnd).ShouldBeGreaterThan(0);
            Math.Max(nested.SelectionStart, nested.SelectionEnd).ShouldBe(nestedText.Length);
            Math.Min(following.SelectionStart, following.SelectionEnd).ShouldBe(0);
            Math.Max(following.SelectionStart, following.SelectionEnd).ShouldBeGreaterThan(0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RoutedPointerDragCanStartOnEmptyDocumentSurface()
    {
        const string text = "Short selectable text.";
        var control = CreateMarkdown(text);
        var window = new Window { Width = 600, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segment = MarkdownDocumentSelection.GetSegmentControls(control)
                .Single(candidate => GetText(candidate) == text);
            MarkdownDocumentSelection.TryGetSegmentBounds(control, segment, out var bounds).ShouldBeTrue();
            var ownerOrigin = control.TranslatePoint(default, window)
                              ?? throw new InvalidOperationException("The Markdown document was not positioned in the test window.");
            var emptySurfacePoint = ownerOrigin + new Vector(control.Bounds.Width - 4, bounds.Center.Y);
            var focus = GetWindowPoint(control, segment, window, 2);

            window.MouseMove(emptySurfacePoint);
            window.MouseDown(emptySurfacePoint, MouseButton.Left);
            window.MouseMove(focus, RawInputModifiers.LeftMouseButton);
            window.MouseUp(focus, MouseButton.Left);

            Math.Min(segment.SelectionStart, segment.SelectionEnd).ShouldBe(2);
            Math.Max(segment.SelectionStart, segment.SelectionEnd).ShouldBe(text.Length);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RoutedDoubleAndTripleClickUseWordAndBlockGranularity()
    {
        const string text = "Alpha selectable omega.";
        var control = CreateMarkdown(text);
        var window = new Window { Width = 600, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segment = MarkdownDocumentSelection.GetSegmentControls(control)
                .Single(candidate => GetText(candidate) == text);
            var wordStart = text.IndexOf("selectable", StringComparison.Ordinal);
            var point = GetWindowPoint(control, segment, window, wordStart + 3);

            ResetClickSequence(window, point);
            Click(window, point);
            Click(window, point);
            Math.Min(segment.SelectionStart, segment.SelectionEnd).ShouldBe(wordStart);
            Math.Max(segment.SelectionStart, segment.SelectionEnd).ShouldBe(wordStart + "selectable".Length);

            Click(window, point);
            Math.Min(segment.SelectionStart, segment.SelectionEnd).ShouldBe(0);
            Math.Max(segment.SelectionStart, segment.SelectionEnd).ShouldBe(text.Length);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RoutedDoubleClickDragExtendsSelectionByWordsAcrossParagraphs()
    {
        const string firstText = "Alpha selectable omega.";
        const string secondText = "Beta destination tail.";
        var control = CreateMarkdown($"{firstText}\n\n{secondText}");
        var window = new Window { Width = 600, Height = 180, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            var first = segments.Single(segment => GetText(segment) == firstText);
            var second = segments.Single(segment => GetText(segment) == secondText);
            var wordStart = firstText.IndexOf("selectable", StringComparison.Ordinal);
            var wordEnd = secondText.IndexOf("destination", StringComparison.Ordinal) + "destination".Length;
            var anchor = GetWindowPoint(control, first, window, wordStart + 3);
            var focus = GetWindowPoint(control, second, window, wordEnd - 2);

            ResetClickSequence(window, anchor);
            Click(window, anchor);
            window.MouseDown(anchor, MouseButton.Left);
            window.MouseMove(focus, RawInputModifiers.LeftMouseButton);
            window.MouseUp(focus, MouseButton.Left);

            Math.Min(first.SelectionStart, first.SelectionEnd).ShouldBe(wordStart);
            Math.Max(first.SelectionStart, first.SelectionEnd).ShouldBe(firstText.Length);
            Math.Min(second.SelectionStart, second.SelectionEnd).ShouldBe(0);
            Math.Max(second.SelectionStart, second.SelectionEnd).ShouldBe(wordEnd);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RoutedTripleClickDragExtendsSelectionByBlocksAcrossParagraphs()
    {
        const string firstText = "Alpha selectable omega.";
        const string secondText = "Beta destination tail.";
        var control = CreateMarkdown($"{firstText}\n\n{secondText}");
        var window = new Window { Width = 600, Height = 180, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            var first = segments.Single(segment => GetText(segment) == firstText);
            var second = segments.Single(segment => GetText(segment) == secondText);
            var anchor = GetWindowPoint(control, first, window, 8);
            var focus = GetWindowPoint(control, second, window, 8);

            ResetClickSequence(window, anchor);
            Click(window, anchor);
            Click(window, anchor);
            window.MouseDown(anchor, MouseButton.Left);
            window.MouseMove(focus, RawInputModifiers.LeftMouseButton);
            window.MouseUp(focus, MouseButton.Left);

            MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
                firstText + Environment.NewLine + Environment.NewLine + secondText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TripleClickSelectsTheEntireFlowWhenScaledTextSplitsTheParagraph()
    {
        const string expectedText = "H2O follows.";
        var flowGroup = new object();
        var prefix = new SelectableTextBlock { Text = "H" };
        var subscript = new SelectableTextBlock { Text = "2", FontSize = 10 };
        var suffix = new SelectableTextBlock { Text = "O follows." };
        MarkdownDocumentSelection.RegisterSegment(prefix, flowGroup);
        MarkdownDocumentSelection.RegisterSegment(subscript, flowGroup);
        MarkdownDocumentSelection.RegisterSegment(suffix, flowGroup);
        var paragraph = new TextBlock
        {
            Inlines = new InlineCollection
            {
                new InlineUIContainer(prefix),
                new InlineUIContainer(subscript),
                new InlineUIContainer(suffix)
            }
        };
        var control = CreateInlineDocument(paragraph);
        var window = new Window { Width = 600, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = GetWindowPoint(control, subscript, window, 0);

            ResetClickSequence(window, point);
            Click(window, point);
            Click(window, point);
            Click(window, point);

            MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(expectedText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InteractiveInlineControlsBypassDocumentSelectionPointerHandling()
    {
        var button = new Button { Content = "Retry", Focusable = false };
        var panel = new StackPanel { Children = { button } };
        var control = CreateInlineDocument(panel);
        var window = new Window { Width = 600, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentSelection.ShouldBypassDocumentInput(control, button).ShouldBeTrue();
            MarkdownDocumentSelection.ShouldBypassDocumentInput(
                    control,
                    new SelectableTextBlock { Text = "Selectable" })
                .ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EmbeddedTextEditorShortcutIsNotInterceptedByDocumentSelection()
    {
        var shortcutReceived = false;
        var textBox = new TextBox
        {
            Text = "Editable Mermaid source",
            SelectionStart = 8,
            SelectionEnd = 8
        };
        textBox.AddHandler(InputElement.KeyDownEvent, OnTextBoxKeyDown, RoutingStrategies.Bubble);
        var control = CreateInlineDocument(textBox);
        var window = new Window { Width = 600, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            textBox.Focus().ShouldBeTrue();

            var modifiers = OperatingSystem.IsMacOS()
                ? RawInputModifiers.Meta
                : RawInputModifiers.Control;
            window.KeyPress(Key.A, modifiers, PhysicalKey.A, "a");
            window.KeyRelease(Key.A, modifiers, PhysicalKey.A, "a");

            shortcutReceived.ShouldBeTrue();
            textBox.SelectionStart.ShouldBe(0);
            textBox.SelectionEnd.ShouldBe(textBox.Text.Length);
            MarkdownDocumentSelection.GetSelectedText(control).ShouldBeEmpty();
        }
        finally
        {
            textBox.RemoveHandler(InputElement.KeyDownEvent, OnTextBoxKeyDown);
            window.Close();
        }

        void OnTextBoxKeyDown(object? _, KeyEventArgs args)
        {
            var expectedModifier = OperatingSystem.IsMacOS()
                ? KeyModifiers.Meta
                : KeyModifiers.Control;
            if (args.Key != Key.A || (args.KeyModifiers & expectedModifier) == 0)
                return;

            shortcutReceived = true;
            textBox.SelectAll();
            args.Handled = true;
        }
    }

    [AvaloniaFact]
    public void HiddenInlineFallbackTextIsExcludedUntilItBecomesVisible()
    {
        var visibleText = new SelectableTextBlock { Text = "Visible source" };
        var hiddenText = new SelectableTextBlock { Text = "Hidden fallback" };
        var hiddenHost = new StackPanel
        {
            IsVisible = false,
            Children = { hiddenText }
        };
        MarkdownDocumentSelection.RegisterSegment(visibleText);
        MarkdownDocumentSelection.RegisterSegment(hiddenText);
        var panel = new StackPanel { Children = { visibleText, hiddenHost } };
        var control = CreateInlineDocument(panel);
        var window = new Window { Width = 600, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();

            MarkdownDocumentSelection.GetSegmentControls(control).ShouldBe([visibleText]);

            hiddenHost.IsVisible = true;
            window.UpdateLayout();

            MarkdownDocumentSelection.GetSegmentControls(control).ShouldBe([visibleText, hiddenText]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectAllCommandTracksDynamicallyVisibleDocumentText()
    {
        var text = new SelectableTextBlock { Text = "Dynamically visible source" };
        var host = new StackPanel
        {
            IsVisible = false,
            Children = { text }
        };
        MarkdownDocumentSelection.RegisterSegment(text);
        var control = CreateInlineDocument(host);
        var commandNotifications = 0;
        control.SelectAllCommand.CanExecuteChanged += OnCanExecuteChanged;
        var window = new Window { Width = 600, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            control.SelectAllCommand.CanExecute(null).ShouldBeFalse();

            host.IsVisible = true;
            window.UpdateLayout();

            control.SelectAllCommand.CanExecute(null).ShouldBeTrue();
            commandNotifications.ShouldBe(1);

            host.IsVisible = false;
            window.UpdateLayout();

            control.SelectAllCommand.CanExecute(null).ShouldBeFalse();
            commandNotifications.ShouldBe(2);
        }
        finally
        {
            control.SelectAllCommand.CanExecuteChanged -= OnCanExecuteChanged;
            window.Close();
        }

        void OnCanExecuteChanged(object? sender, EventArgs args) => commandNotifications++;
    }

    [AvaloniaFact]
    public void PointHitTestingResolvesLinksInsideSelectableSegments()
    {
        const string text = "A rendered link remains interactive.";
        var control = CreateMarkdown(
            "An earlier paragraph establishes a non-zero segment origin.\n\n" +
            "A rendered [link](https://example.com) remains interactive.");
        var window = new Window { Width = 600, Height = 220, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segment = MarkdownDocumentSelection.GetSegmentControls(control)
                .Single(candidate => GetText(candidate) == text);
            var linkOffset = text.IndexOf("link", StringComparison.Ordinal) + 1;
            var ownerPoint = GetOwnerPoint(control, segment, linkOffset);
            var result = control.HitTestMarkdown(ownerPoint);

            ShouldHaveSelfOrAncestor<MarkdownLinkInline>(result);
            MarkdownDocumentSelection.TryGetSegmentBounds(control, segment, out var bounds).ShouldBeTrue();
            result!.HighlightRects.ShouldNotBeEmpty();
            result.HighlightRects.ShouldAllBe(rect => rect.Top >= bounds.Top);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OuterPointHitTestingDoesNotPairNestedAstNodesWithTheOuterParseResult()
    {
        const string nestedText = "A nested link remains interactive.";
        var controller = MarkdownRenderingServices.CreateController(
            new ProMarkdown.Plugin.Alerts.AlertsMarkdownPlugin());
        var control = CreateMarkdown(
            "> [!NOTE]\n> A [nested link](https://example.com) remains interactive.",
            controller: controller);
        var window = new Window { Width = 600, Height = 220, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var nested = control.GetVisualDescendants()
                .OfType<MarkdownTextBlock>()
                .Single();
            var segment = MarkdownDocumentSelection.GetSegmentControls(nested)
                .Single(candidate => GetText(candidate) == nestedText);
            var linkOffset = nestedText.IndexOf("link", StringComparison.Ordinal) + 1;
            var nestedPoint = GetOwnerPoint(nested, segment, linkOffset);
            var outerPoint = nested.TranslatePoint(nestedPoint, control)
                             ?? throw new InvalidOperationException("The nested Markdown document was not positioned in the outer document.");

            ShouldHaveSelfOrAncestor<MarkdownLinkInline>(nested.HitTestMarkdown(nestedPoint));
            var outerResult = control.HitTestMarkdown(outerPoint);

            if (outerResult is not null)
            {
                outerResult.ParseResult.ShouldBeSameAs(control.LastParseResult);
                outerResult.AstNode.Node.ShouldNotBeOfType<MarkdownLinkInline>();
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AbbreviationRemainsInTheParagraphFlowAndPreservesWhitespace()
    {
        const string sentence = "The abbreviation API has a definition and remains selectable.";
        var control = CreateMarkdown($"{sentence}\n\n*[API]: Application programming interface");
        var window = new Window { Width = 240, Height = 220, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            var paragraph = MarkdownDocumentSelection.GetSegmentControls(control)
                .Single(segment => GetText(segment) == sentence);
            GetText(paragraph).ShouldContain("API has");
            paragraph.TextLayout.TextLines.Count.ShouldBeGreaterThan(1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AbbreviationToolTipsRemainAvailableInsideScaledHeadings()
    {
        var control = CreateMarkdown("# API heading\n\n*[API]: Application programming interface");

        var heading = MarkdownDocumentSelection.GetSegmentControls(control)
            .OfType<MarkdownWrappingSelectableTextBlock>()
            .Single(segment => GetText(segment) == "API heading");

        var range = heading.ToolTipRanges.ShouldHaveSingleItem();
        range.Start.ShouldBe(0);
        range.Length.ShouldBe(3);
        range.ToolTip.ShouldBe("Application programming interface");
        heading.Inlines!
            .OfType<MarkdownToolTipSpan>()
            .Single()
            .IsSet(TextElement.FontSizeProperty)
            .ShouldBeFalse();
    }

    [AvaloniaFact]
    public void AbbreviationHoverDoesNotRequireAPopupHost()
    {
        var control = CreateMarkdown("API text\n\n*[API]: Application programming interface");
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segment = MarkdownDocumentSelection.GetSegmentControls(control)
                .OfType<MarkdownWrappingSelectableTextBlock>()
                .Single(candidate => GetText(candidate) == "API text");
            var point = GetWindowPoint(control, segment, window, 1);
            var outsideRange = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent);

            segment.RaiseEvent(outsideRange);
            outsideRange.Cancel.ShouldBeTrue();

            window.MouseMove(point);

            ToolTip.GetTip(segment).ShouldBe("Application programming interface");
            var insideRange = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent);
            segment.RaiseEvent(insideRange);
            insideRange.Cancel.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InlineTextControlsProvidedByPluginsRemainControls()
    {
        var plugin = new InlineTextControlPlugin();
        var controller = MarkdownRenderingServices.CreateController(plugin);
        var control = CreateMarkdown("plugin", controller: controller);

        var renderedControl = plugin.LastControl.ShouldNotBeNull();
        EnumerateControls(control.Inlines!)
            .ShouldContain(candidate => ReferenceEquals(candidate, renderedControl));
        renderedControl.Margin.ShouldBe(new Thickness(7));
    }

    [AvaloniaFact]
    public void RoundedBordersProvidedByPluginsKeepTheirOriginalStructure()
    {
        var plugin = new RoundedBorderPlugin();
        var controller = MarkdownRenderingServices.CreateController(plugin);
        _ = CreateMarkdown("plugin", controller: controller);

        var renderedBorder = plugin.LastBorder.ShouldNotBeNull();
        renderedBorder.ClipToBounds.ShouldBeTrue();
        renderedBorder.Child.ShouldBeSameAs(plugin.LastContent);
    }

    [AvaloniaFact]
    public void InlineControlsRemainLaidOutBetweenSelectableHeadingSegments()
    {
        var control = CreateMarkdown("# Before ![Diagram](relative-image.png) after");
        var window = new Window { Width = 600, Height = 240, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            segments.ShouldContain(segment => GetText(segment) == "Before ");
            segments.ShouldContain(segment => GetText(segment) == " after");

            var imageFallback = control.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == "Diagram"));
            imageFallback.Bounds.Width.ShouldBeGreaterThan(0);
            imageFallback.Bounds.Height.ShouldBeGreaterThan(0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NestedInlineControlsRemainLaidOutBetweenSelectableHeadingSegments()
    {
        var control = CreateMarkdown("# Before *styled ![Diagram](relative-image.png) text* after");
        var window = new Window { Width = 600, Height = 240, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            segments.ShouldContain(segment => GetText(segment) == "Before styled ");
            segments.ShouldContain(segment => GetText(segment) == " text after");

            segments
                .Single(segment => GetText(segment) == "Before styled ")
                .Inlines!
                .OfType<Italic>()
                .Single()
                .FontStyle
                .ShouldBe(FontStyle.Italic);

            var imageFallback = control.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == "Diagram"));
            imageFallback.Bounds.Width.ShouldBeGreaterThan(0);
            imageFallback.Bounds.Height.ShouldBeGreaterThan(0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NestedInlineControlsRemainLaidOutBetweenSelectableParagraphSegments()
    {
        var control = CreateMarkdown("Before *styled ![Diagram](relative-image.png) text* after");
        var window = new Window { Width = 600, Height = 240, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            segments.ShouldContain(segment => GetText(segment) == "Before styled ");
            segments.ShouldContain(segment => GetText(segment) == " text after");

            var imageFallback = control.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text == "Diagram"));
            imageFallback.Bounds.Width.ShouldBeGreaterThan(0);
            imageFallback.Bounds.Height.ShouldBeGreaterThan(0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CopyAcrossBlocksProducesExactPlainText()
    {
        const string headingText = "Heading";
        const string paragraphText = "Paragraph with a formatted link.";
        var control = CreateMarkdown($"# {headingText}\n\nParagraph with a [formatted link](https://example.com).");
        var segments = MarkdownDocumentSelection.GetSegmentControls(control);
        var heading = segments.Single(segment => GetText(segment) == headingText);
        var paragraph = segments.Single(segment => GetText(segment) == paragraphText);

        MarkdownDocumentSelection.SelectRange(control, heading, 0, paragraph, paragraphText.Length);

        MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
            headingText + Environment.NewLine + Environment.NewLine + paragraphText);
    }

    [AvaloniaFact]
    public void CopyAcrossFencedCodeAndParagraphDoesNotEmitObjectReplacementCharacters()
    {
        var control = CreateMarkdown("```text\nfirst line\nsecond line\n```\n\nFollowing paragraph.");

        MarkdownDocumentSelection.SelectAll(control);
        var copied = MarkdownDocumentSelection.GetSelectedText(control);

        copied.ShouldContain("first line");
        copied.ShouldContain("second line");
        copied.ShouldContain("Following paragraph.");
        copied.ShouldNotContain('\uFFFC');
    }

    [AvaloniaFact]
    public void CopyAcrossTableCellsPreservesRowsAndColumns()
    {
        var control = CreateMarkdown("| Component | Version |\n|---|---|\n| Desktop | 1.2.3 |");
        var window = new Window { Width = 600, Height = 220, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            var first = segments.Single(segment => GetText(segment) == "Component");
            var last = segments.Single(segment => GetText(segment) == "1.2.3");

            MarkdownDocumentSelection.SelectRange(control, first, 0, last, "1.2.3".Length);

            MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
                "Component\tVersion" + Environment.NewLine + "Desktop\t1.2.3");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CopyFromTableCellsIncludesInlineAdornmentText()
    {
        var control = CreateMarkdown(
            "| Value |\n|---|\n| API ==mark== H~2~ |\n\n*[API]: Application programming interface");
        var window = new Window { Width = 500, Height = 220, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();

            var segments = MarkdownDocumentSelection.GetSegmentControls(control);
            var segmentTexts = segments.Select(GetText).ToArray();
            segmentTexts.ShouldContain("API mark H2");
            var first = segments.Single(segment => GetText(segment) == "Value");
            var last = segments.Single(segment => GetText(segment) == "API mark H2");
            MarkdownDocumentSelection.SelectRange(control, first, 0, last, "API mark H2".Length);

            MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
                "Value" + Environment.NewLine + "API mark H2");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LiteralBoldQuoteGlyphIsNotConvertedIntoABlockQuote()
    {
        var control = CreateMarkdown("**│ **This is ordinary bold text.");

        EnumerateControls(control.Inlines!)
            .OfType<Border>()
            .Count(IsQuoteRail)
            .ShouldBe(0);
    }

    [AvaloniaFact]
    public void BlockQuotesUseContinuousRailsIncludingNestedQuotes()
    {
        var control = CreateMarkdown("> First line.\n>\n> Second line.\n> > Nested line.");

        EnumerateControls(control.Inlines!)
            .OfType<Border>()
            .Count(IsQuoteRail)
            .ShouldBe(2);
    }

    [AvaloniaFact]
    public void BlockQuoteRailsUseThePaletteQuoteBorder()
    {
        var quoteBorder = new SolidColorBrush(Color.Parse("#FF123456"));
        var palette = new MarkdownThemePalette { QuoteBorder = quoteBorder };
        var control = CreateMarkdown("> First line.\n> > Nested line.", palette: palette);

        var rails = EnumerateControls(control.Inlines!)
            .OfType<Border>()
            .Where(IsQuoteRail)
            .ToArray();

        rails.ShouldNotBeEmpty();
        rails.ShouldAllBe(rail => ReferenceEquals(rail.Background, quoteBorder));
    }

    [AvaloniaFact]
    public void CopyAcrossBlockQuoteBlankLinesPreservesTheBlankLine()
    {
        var control = CreateMarkdown("> First line.\n>\n> Second line.");
        var segments = MarkdownDocumentSelection.GetSegmentControls(control);
        var first = segments.Single(segment => GetText(segment) == "First line.");
        var second = segments.Single(segment => GetText(segment) == "Second line.");

        MarkdownDocumentSelection.SelectRange(
            control,
            first,
            0,
            second,
            "Second line.".Length);

        MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
            "First line." + Environment.NewLine + Environment.NewLine + "Second line.");
    }

    [AvaloniaFact]
    public void CopyAcrossSoftLinesInOneBlockQuoteParagraphUsesOneLineBreak()
    {
        var control = CreateMarkdown("> First soft line.\n> Second soft line.");
        var segments = MarkdownDocumentSelection.GetSegmentControls(control);
        var first = segments.Single(segment => GetText(segment) == "First soft line.");
        var second = segments.Single(segment => GetText(segment) == "Second soft line.");

        MarkdownDocumentSelection.SelectRange(
            control,
            first,
            0,
            second,
            "Second soft line.".Length);

        MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
            "First soft line." + Environment.NewLine + "Second soft line.");
    }

    [AvaloniaFact]
    public void BlockSelectionIncludesAllSoftLinesInOneQuotedParagraph()
    {
        var control = CreateMarkdown("> First soft line.\n> Second soft line.");
        var first = MarkdownDocumentSelection.GetSegmentControls(control)
            .Single(segment => GetText(segment) == "First soft line.");

        MarkdownDocumentSelection.SelectBlock(control, first);

        MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
            "First soft line." + Environment.NewLine + "Second soft line.");
    }

    [AvaloniaFact]
    public void BlockSelectionIncludesAllSoftLinesInOneParagraphButNotTheNextBlock()
    {
        var control = CreateMarkdown(
            "First soft line.\nSecond soft line.\n\nFollowing paragraph.");
        var first = MarkdownDocumentSelection.GetSegmentControls(control)
            .Single(segment => GetText(segment) == "First soft line.");

        MarkdownDocumentSelection.SelectBlock(control, first);

        MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
            "First soft line." + Environment.NewLine + "Second soft line.");
    }

    [AvaloniaFact]
    public void CopyAcrossTightQuotedListItemsUsesSingleLineBreaks()
    {
        var control = CreateMarkdown("> 1. First item.\n> 2. Second item.");
        var segments = MarkdownDocumentSelection.GetSegmentControls(control);
        var first = segments.Single(segment => GetText(segment).Contains("First item.", StringComparison.Ordinal));
        var second = segments.Single(segment => GetText(segment).Contains("Second item.", StringComparison.Ordinal));

        MarkdownDocumentSelection.SelectRange(
            control,
            first,
            0,
            second,
            GetText(second).Length);

        MarkdownDocumentSelection.GetSelectedText(control).ShouldBe(
            GetText(first) + Environment.NewLine + GetText(second));
    }

    [AvaloniaFact]
    public void LiteralTaskGlyphIsNotReplacedByANativeCheckBox()
    {
        var control = CreateMarkdown("☐ literal glyph\n\n- [ ] actual task");

        control.GetVisualDescendants().OfType<CheckBox>().Count().ShouldBe(1);
        MarkdownDocumentSelection.GetSegmentControls(control)
            .Select(GetText)
            .ShouldContain(text => text.Contains("☐ literal glyph", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void ClearingMarkdownClearsDocumentSelectionState()
    {
        var control = CreateMarkdown("Selected text");
        var segment = MarkdownDocumentSelection.GetSegmentControls(control).Single();
        MarkdownDocumentSelection.SelectBlock(control, segment);
        MarkdownDocumentSelection.GetSelectedText(control).ShouldBe("Selected text");

        control.Markdown = string.Empty;

        MarkdownDocumentSelection.GetSelectedText(control).ShouldBeEmpty();
        MarkdownDocumentSelection.GetSegmentControls(control).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void FontFamilyChangesRegenerateFontDependentContent()
    {
        var control = CreateMarkdown("Font dependent text");
        var original = control.LastRenderResult;

        control.FontFamily = new FontFamily("Consolas");

        control.LastRenderResult.ShouldNotBeSameAs(original);
    }

    [AvaloniaFact]
    public void FailedRenderingDisposesResourcesCreatedByTheFailedGeneration()
    {
        var resource = new TrackingDisposable();
        var control = new MarkdownTextBlock
        {
            RenderController = new ThrowingRenderController(resource)
        };

        Should.Throw<InvalidOperationException>(() => control.Markdown = "Trigger rendering");

        resource.IsDisposed.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void DetachingInvalidatesTheActiveRenderGeneration()
    {
        var controller = new CapturingRenderController();
        var control = CreateMarkdown("Rendered content", controller: controller);
        var window = new Window { Width = 320, Height = 160, Content = control };
        window.Show();
        try
        {
            var context = controller.LastContext.ShouldNotBeNull();
            context.IsCurrentRender(context.RenderGeneration).ShouldBeTrue();

            window.Content = null;

            context.IsCurrentRender(context.RenderGeneration).ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    private static MarkdownTextBlock CreateInlineDocument(Control content)
    {
        var control = new MarkdownTextBlock
        {
            FontSize = 14,
            Foreground = Brushes.Black,
            RenderController = new InlineControlRenderController(content),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        control.Markdown = "Inline control";
        return control;
    }

    private static MarkdownTextBlock CreateMarkdown(
        string markdown,
        double fontSize = 14,
        IBrush? foreground = null,
        MarkdownThemePalette? palette = null,
        IMarkdownRenderController? controller = null) =>
        new()
        {
            Markdown = markdown,
            FontSize = fontSize,
            Foreground = foreground ?? Brushes.Black,
            ThemePalette = palette,
            RenderController = controller ?? MarkdownRenderingServices.DefaultController,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

    private static string GetText(SelectableTextBlock control)
    {
        var start = control.SelectionStart;
        var end = control.SelectionEnd;
        control.SelectAll();
        var text = control.SelectedText;
        control.SelectionStart = start;
        control.SelectionEnd = end;
        return text;
    }

    private static TMarkdownObject ShouldHaveSelfOrAncestor<TMarkdownObject>(MarkdownHitTestResult? result)
        where TMarkdownObject : Markdig.Syntax.MarkdownObject
    {
        result.ShouldNotBeNull();
        for (Markdig.Syntax.MarkdownObject? current = result.AstNode.Node; current is not null;)
        {
            if (current is TMarkdownObject match)
                return match;
            if (!result.ParseResult.TryGetParent(current, out current))
                break;
        }

        throw new ShouldAssertException(
            $"The hit Markdown node should have a {typeof(TMarkdownObject).Name} ancestor.");
    }

    private static Point GetWindowPoint(
        MarkdownTextBlock owner,
        SelectableTextBlock segment,
        Window window,
        int offset)
    {
        var ownerPoint = GetOwnerPoint(owner, segment, offset);
        var ownerOrigin = owner.TranslatePoint(default, window)
                          ?? throw new InvalidOperationException("The Markdown document was not positioned in the test window.");
        return ownerOrigin + (Vector)ownerPoint;
    }

    private static Point GetOwnerPoint(
        MarkdownTextBlock owner,
        SelectableTextBlock segment,
        int offset)
    {
        MarkdownDocumentSelection.TryGetSegmentBounds(owner, segment, out var bounds).ShouldBeTrue();
        var characterBounds = segment.TextLayout.HitTestTextPosition(offset);
        return bounds.TopLeft + new Vector(
            segment.Padding.Left + characterBounds.X + Math.Max(characterBounds.Width, 1) / 2,
            segment.Padding.Top + characterBounds.Y + characterBounds.Height / 2);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void ResetClickSequence(Window window, Point target)
    {
        var resetPoint = new Point(
            target.X < window.ClientSize.Width / 2 ? window.ClientSize.Width - 2 : 2,
            target.Y < window.ClientSize.Height / 2 ? window.ClientSize.Height - 2 : 2);
        Click(window, resetPoint);
        window.MouseMove(target);
    }

    private static IEnumerable<Inline> EnumerateInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is Span span)
            {
                foreach (var child in EnumerateInlines(span.Inlines))
                    yield return child;
            }
            if (inline is InlineUIContainer { Child: { } control })
            {
                foreach (var child in EnumerateInlines(control))
                    yield return child;
            }
        }
    }

    private static IEnumerable<Inline> EnumerateInlines(Control control)
    {
        if (control is TextBlock { Inlines: { } inlines })
        {
            foreach (var inline in EnumerateInlines(inlines))
                yield return inline;
        }
        foreach (var child in EnumerateChildren(control))
        {
            foreach (var inline in EnumerateInlines(child))
                yield return inline;
        }
    }

    private static IEnumerable<Control> EnumerateControls(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var child in EnumerateControls(span.Inlines))
                    yield return child;
            }
            if (inline is InlineUIContainer { Child: { } control })
            {
                foreach (var child in EnumerateControls(control))
                    yield return child;
            }
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control control)
    {
        yield return control;
        foreach (var child in EnumerateChildren(control))
        {
            foreach (var nested in EnumerateControls(child))
                yield return nested;
        }
    }

    private static IEnumerable<Control> EnumerateChildren(Control control)
    {
        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    yield return child;
                break;
            case Decorator { Child: { } child }:
                yield return child;
                break;
            case ContentControl { Content: Control child }:
                yield return child;
                break;
        }
    }

    private static IBrush? GetBackground(Control control) => control switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        TextBlock text => text.Background,
        _ => null
    };

    private static bool HasBorder(Thickness thickness) =>
        thickness.Left > 0 || thickness.Top > 0 || thickness.Right > 0 || thickness.Bottom > 0;

    private static bool HasRoundedCorner(CornerRadius radius) =>
        radius.TopLeft > 0 || radius.TopRight > 0 || radius.BottomRight > 0 || radius.BottomLeft > 0;

    private static bool IsQuoteRail(Border border) =>
        Math.Abs(border.CornerRadius.TopLeft - 1.5) < 0.01 &&
        border.HorizontalAlignment == HorizontalAlignment.Stretch &&
        border.VerticalAlignment == VerticalAlignment.Stretch;

    private sealed class ThrowingRenderController(TrackingDisposable resource) : IMarkdownRenderController
    {
        public MarkdownRenderResult Render(MarkdownRenderRequest request)
        {
            request.Context.ResourceTracker.Track(resource);
            throw new InvalidOperationException("Expected render failure.");
        }
    }

    private sealed class CapturingRenderController : IMarkdownRenderController
    {
        public MarkdownRenderContext? LastContext { get; private set; }

        public MarkdownRenderResult Render(MarkdownRenderRequest request)
        {
            LastContext = request.Context;
            return MarkdownRenderResult.Empty(request.Context.ResourceTracker);
        }
    }

    private sealed class InlineControlRenderController(Control content) : IMarkdownRenderController
    {
        public MarkdownRenderResult Render(MarkdownRenderRequest request) => new(
            new InlineCollection { new InlineUIContainer(content) },
            request.Context.ResourceTracker,
            MarkdownParseResult.Empty,
            MarkdownRenderMap.Empty);
    }

    private sealed class InlineTextControlPlugin : IMarkdownPlugin, IMarkdownInlineRenderingPlugin
    {
        public TextBlock? LastControl { get; private set; }

        public int Order => 0;

        public void Register(MarkdownPluginRegistry registry) =>
            registry.AddInlineRenderingPlugin(this);

        public bool CanRender(Markdig.Syntax.Inlines.Inline inline) => inline is MarkdownLiteralInline;

        public bool TryRender(MarkdownInlineRenderingPluginContext context)
        {
            LastControl = new TextBlock
            {
                Margin = new Thickness(7),
                Text = context.Inline.ToString()
            };
            context.AddInlineControl(LastControl);
            return true;
        }
    }

    private sealed class RoundedBorderPlugin : IMarkdownPlugin, IMarkdownBlockRenderingPlugin
    {
        public Border? LastBorder { get; private set; }

        public TextBlock? LastContent { get; private set; }

        public int Order => 0;

        public void Register(MarkdownPluginRegistry registry) =>
            registry.AddBlockRenderingPlugin(this);

        public bool CanRender(Markdig.Syntax.Block block) => block is Markdig.Syntax.ParagraphBlock;

        public bool TryRender(MarkdownBlockRenderingPluginContext context)
        {
            LastContent = new TextBlock { Text = "plugin" };
            LastBorder = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Child = LastContent,
                ClipToBounds = true,
                CornerRadius = new CornerRadius(8)
            };
            context.AddBlockControl(LastBorder);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Target, IDisposable Lease) CreateDisposedRenderActivityWithOutstandingLease()
    {
        var target = new object();
        var activity = new MarkdownRenderActivity(() => GC.KeepAlive(target));
        var lease = activity.Begin();
        activity.Complete();
        activity.Dispose();
        return (new WeakReference(target), lease);
    }

    private sealed class FixedColorBlockPlugin(IBrush background) : IMarkdownPlugin, IMarkdownBlockRenderingPlugin
    {
        public Border LastBorder { get; set; } = new() { Background = background };

        public bool UseExistingBorder { get; set; }

        public void Register(MarkdownPluginRegistry registry) => registry.AddBlockRenderingPlugin(this);

        public bool CanRender(Markdig.Syntax.Block block) => block is Markdig.Syntax.ParagraphBlock;

        public bool TryRender(MarkdownBlockRenderingPluginContext context)
        {
            if (!UseExistingBorder)
                LastBorder = new Border { Background = background };
            context.AddBlockControl(LastBorder);
            return true;
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class AsyncActivityPlugin : IMarkdownPlugin, IMarkdownBlockRenderingPlugin
    {
        public List<IDisposable> Leases { get; } = [];

        public MarkdownRenderContext? LastContext { get; private set; }

        public void Register(MarkdownPluginRegistry registry) => registry.AddBlockRenderingPlugin(this);

        public bool CanRender(Markdig.Syntax.Block block) => block is Markdig.Syntax.ParagraphBlock;

        public bool TryRender(MarkdownBlockRenderingPluginContext context)
        {
            LastContext = context.RenderContext;
            Leases.Add(context.RenderContext.BeginAsyncOperation());
            context.AddBlockControl(new TextBlock { Text = "async" });
            return true;
        }
    }

    private sealed class NestedMarkdownActivityPlugin(IMarkdownImageLoader imageLoader)
        : IMarkdownPlugin, IMarkdownBlockRenderingPlugin
    {
        public void Register(MarkdownPluginRegistry registry) => registry.AddBlockRenderingPlugin(this);

        public bool CanRender(Markdig.Syntax.Block block) => block is Markdig.Syntax.ParagraphBlock;

        public bool TryRender(MarkdownBlockRenderingPluginContext context)
        {
            var control = new MarkdownTextBlock
            {
                ImageLoader = imageLoader,
                Markdown = "![Pending](data:image/png;base64,AAAA)"
            };
            context.RenderContext.TrackNestedRendering(control);
            context.AddBlockControl(control);
            return true;
        }
    }

    private sealed class AttachedNestedMarkdownActivityPlugin
        : IMarkdownPlugin, IMarkdownBlockRenderingPlugin
    {
        private AttachedAsyncRenderController? _controller;
        private Border? _host;

        public bool HasOperation => _controller?.HasOperation ?? false;

        public void Register(MarkdownPluginRegistry registry) => registry.AddBlockRenderingPlugin(this);

        public bool CanRender(Markdig.Syntax.Block block) => block is Markdig.Syntax.ParagraphBlock;

        public bool TryRender(MarkdownBlockRenderingPluginContext context)
        {
            var controller = new AttachedAsyncRenderController();
            var control = new MarkdownTextBlock
            {
                RenderController = controller,
                Markdown = "nested"
            };
            controller.Control = control;
            _controller = controller;
            _host = new Border { Child = control };
            context.RenderContext.TrackNestedRendering(control);
            context.AddBlockControl(_host);
            return true;
        }

        public void Complete() => _controller?.Complete();

        public void Detach()
        {
            if (_host is not null)
                _host.Child = null;
        }
    }

    private sealed class AttachedAsyncRenderController : IMarkdownRenderController
    {
        private IDisposable? _operation;

        public MarkdownTextBlock? Control { get; set; }

        public bool HasOperation => Volatile.Read(ref _operation) is not null;

        public MarkdownRenderResult Render(MarkdownRenderRequest request)
        {
            if (Control is { } control && TopLevel.GetTopLevel(control) is not null)
            {
                var operation = request.Context.BeginAsyncOperation();
                Interlocked.Exchange(ref _operation, operation)?.Dispose();
            }

            return MarkdownRenderResult.Empty(request.Context.ResourceTracker);
        }

        public void Complete() => Interlocked.Exchange(ref _operation, null)?.Dispose();
    }

    private sealed class NestedMarkdownResourcePlugin : IMarkdownPlugin, IMarkdownBlockRenderingPlugin
    {
        public OwnedResourceRenderController? LastController { get; private set; }

        public void Register(MarkdownPluginRegistry registry) => registry.AddBlockRenderingPlugin(this);

        public bool CanRender(Markdig.Syntax.Block block) => block is Markdig.Syntax.ParagraphBlock;

        public bool TryRender(MarkdownBlockRenderingPluginContext context)
        {
            LastController = new OwnedResourceRenderController();
            var control = new MarkdownTextBlock
            {
                Markdown = "nested",
                RenderController = LastController
            };
            context.RenderContext.TrackNestedRendering(control);
            context.AddBlockControl(control);
            return true;
        }
    }

    private sealed class OwnedResourceRenderController : IMarkdownRenderController
    {
        public TrackingDisposable? LastResource { get; private set; }

        public MarkdownRenderResult Render(MarkdownRenderRequest request)
        {
            LastResource = new TrackingDisposable();
            request.Context.ResourceTracker.Track(LastResource);
            return MarkdownRenderResult.Empty(request.Context.ResourceTracker);
        }
    }

    private sealed class CancellationAwareImageLoader : IMarkdownImageLoader
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Bitmap> LoadAsync(
            MarkdownImageLoadRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Canceled.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("The cancellation-aware test loader unexpectedly completed.");
        }
    }

    private sealed class FailingImageLoader : IMarkdownImageLoader
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ThreadId { get; private set; }

        public async Task<Bitmap> LoadAsync(
            MarkdownImageLoadRequest request,
            CancellationToken cancellationToken)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            throw new ImageLoaderTestException("Unexpected loader failure.");
        }

        public void Fail() => _release.TrySetResult();
    }

    private sealed class FatalImageLoader : IMarkdownImageLoader
    {
        public Task<Bitmap> LoadAsync(
            MarkdownImageLoadRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<Bitmap>(new OutOfMemoryException("fatal image failure"));
    }

    private sealed class PolicyRejectingImageLoader : IMarkdownImageLoader
    {
        public int InvocationCount { get; private set; }

        public Task<Bitmap> LoadAsync(
            MarkdownImageLoadRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromException<Bitmap>(
                new InvalidOperationException("The blocked custom loader was invoked."));
        }
    }

    private sealed class ImageLoaderTestException(string message) : Exception(message);

    private sealed class DelegateCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }

    private sealed class BrushBindingSource(IBrush? brush) : INotifyPropertyChanged
    {
        private IBrush? _brush = brush;

        public event PropertyChangedEventHandler? PropertyChanged;

        public IBrush? Brush
        {
            get => _brush;
            set
            {
                if (ReferenceEquals(_brush, value))
                    return;

                _brush = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Brush)));
            }
        }
    }

    private sealed class MutableCommand(bool canExecute) : ICommand
    {
        private EventHandler? _canExecuteChanged;
        private bool _canExecute = canExecute;

        public int SubscriberCount { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add
            {
                _canExecuteChanged += value;
                SubscriberCount++;
            }
            remove
            {
                _canExecuteChanged -= value;
                SubscriberCount--;
            }
        }

        public bool CanExecute(object? parameter) => _canExecute;

        public void Execute(object? parameter)
        {
        }

        public void SetCanExecute(bool value)
        {
            _canExecute = value;
            _canExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
