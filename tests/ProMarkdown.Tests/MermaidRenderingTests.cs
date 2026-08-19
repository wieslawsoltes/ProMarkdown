using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProMarkdown.Plugin.Mermaid;
using ProMarkdown.Services;
using Mermaider;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class MermaidRenderingTests
{
    public static TheoryData<string, string> SupportedDiagrams => new()
    {
        { "flowchart", "graph TD\nA[Start] --> B[End]" },
        { "sequence", "sequenceDiagram\nparticipant A\nparticipant B\nA->>B: Hello" },
        { "state", "stateDiagram-v2\n[*] --> Idle\nIdle --> [*]" },
        { "class", "classDiagram\nclass Animal { +eat() void }\nclass Dog\nAnimal <|-- Dog" },
        { "er", "erDiagram\nCUSTOMER ||--o{ ORDER : places\nCUSTOMER { string name PK }" },
        { "pie", "pie\ntitle Pets\n\"Dogs\" : 80\n\"Cats\" : 20" },
        { "quadrant", "quadrantChart\nx-axis Low --> High\ny-axis Low --> High\nA: [0.5, 0.5]" },
        { "timeline", "timeline\nsection Era\n2026 : Release" },
        { "gitgraph", "gitGraph\ncommit id: \"init\"\ncommit id: \"release\"" },
        { "radar", "radar-beta\naxis A, B, C\ncurve c1[\"One\"]{1, 2, 3}\nmax 3" },
        { "treemap", "treemap-beta\n\"One\": 60\n\"Two\": 40" },
        { "venn", "venn-beta\nset A[\"One\"]\nset B[\"Two\"]\nunion A, B[\"Both\"]" },
        { "mindmap", "mindmap\n  root((Root))\n    Child" },
        { "gantt", "gantt\ntitle Plan\ndateFormat YYYY-MM-DD\nsection Work\nTask :done, a1, 2026-01-01, 1d" },
        { "journey", "journey\ntitle Day\nsection Work\nTask: 5: Me" },
        { "c4", "C4Context\nPerson(user, \"User\")\nSystem(app, \"App\")\nRel(user, app, \"Uses\")" },
        { "sankey", "sankey-beta\nSource,Target,10" },
        { "xychart", "xychart-beta\nx-axis [a, b]\ny-axis 0 --> 10\nbar [2, 8]" },
        { "requirement", "requirementDiagram\nrequirement req {\nid: 1\ntext: Works\nrisk: low\nverifymethod: test\n}" },
        { "packet", "packet-beta\n0-15: \"Source\"\n16-31: \"Destination\"" },
        { "kanban", "kanban\nTodo\n  Task1\nDone\n  Task2" },
        { "architecture", "architecture-beta\ngroup cloud(cloud)[Cloud]\nservice api(server)[API] in cloud" },
        { "block", "block-beta\ncolumns 2\nA[\"A\"] B[\"B\"]" },
        { "treeview", "treeView-beta\n  root/\n    child.txt" }
    };

    [AvaloniaFact]
    public void HostStylesOverrideMermaidLayoutDefaults()
    {
        using var diagram = new MermaidDiagramControl(
            new PaletteRecordingRenderer(),
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions(),
            CancellationToken.None,
            static () => new CompletionTrackingDisposable());
        var window = new Window { Width = 500, Height = 300, Content = diagram };
        window.Styles.Add(new Style(selector => selector.OfType<MermaidDiagramControl>())
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, 120d),
                new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Center)
            }
        });
        window.Show();
        try
        {
            diagram.MinHeight.ShouldBe(120);
            diagram.HorizontalAlignment.ShouldBe(HorizontalAlignment.Center);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void FatalExceptionsAreNotConvertedIntoMermaidErrorState()
    {
        MermaidDiagramControl.IsRecoverableAsyncException(new InvalidOperationException()).ShouldBeTrue();
        MermaidDiagramControl.IsRecoverableAsyncException(new OutOfMemoryException()).ShouldBeFalse();
        MermaidDiagramControl.IsRecoverableAsyncException(new AccessViolationException()).ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task FatalRendererFailuresReachTheDispatcherExceptionBoundary()
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
            using var diagram = new MermaidDiagramControl(
                new ExceptionRenderer(new OutOfMemoryException("fatal renderer failure")),
                "flowchart TD\nA --> B",
                MarkdownThemePalette.Dark,
                "Inter",
                14,
                new MermaidMarkdownPluginOptions(),
                CancellationToken.None,
                static () => new CompletionTrackingDisposable());

            var exception = await observed.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            exception.Message.ShouldBe("fatal renderer failure");
            diagram.HasError.ShouldBeFalse();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
        }
    }

    [AvaloniaFact]
    public async Task PressHitTestsTheCurrentDiagramInsteadOfCachedHoverState()
    {
        var renderer = new DeferredSvgRenderer();
        var activated = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var diagram = new MermaidDiagramControl(
            renderer,
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions
            {
                ActivateLinkAsync = (uri, _) =>
                {
                    activated.TrySetResult(uri);
                    return Task.CompletedTask;
                }
            },
            CancellationToken.None,
            static () => new CompletionTrackingDisposable());
        var window = new Window { Width = 400, Height = 240, Content = diagram };
        window.Show();
        try
        {
            await renderer.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            window.MouseMove(new Point(200, 100));
            renderer.Complete(LinkedSvg);
            await WaitForAsync(() => diagram.HasImage);

            window.MouseDown(new Point(200, 100), MouseButton.Left);
            activated.Task.IsCompleted.ShouldBeFalse();
            window.MouseUp(new Point(200, 100), MouseButton.Left);

            var uri = await activated.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            uri.ShouldBe(new Uri("https://example.com/docs"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DraggingFromADiagramLinkDoesNotActivateIt()
    {
        var activated = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var diagram = new MermaidDiagramControl(
            new StaticSvgRenderer(LinkedSvg),
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions
            {
                ActivateLinkAsync = (uri, _) =>
                {
                    activated.TrySetResult(uri);
                    return Task.CompletedTask;
                }
            },
            CancellationToken.None,
            static () => new CompletionTrackingDisposable());
        var window = new Window { Width = 400, Height = 240, Content = diagram };
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.HasImage);

            window.MouseDown(new Point(200, 100), MouseButton.Left);
            window.MouseMove(new Point(220, 100));
            window.MouseUp(new Point(220, 100), MouseButton.Left);

            activated.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DiagramLinksSupportKeyboardAndAutomationActivation()
    {
        var activationCount = 0;
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var diagram = new MermaidDiagramControl(
            new StaticSvgRenderer(LinkedSvg),
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions
            {
                AccessibleName = "Architecture diagram",
                ActivateLinkAsync = (_, _) =>
                {
                    if (Interlocked.Increment(ref activationCount) >= 2)
                        activated.TrySetResult();
                    return Task.CompletedTask;
                }
            },
            CancellationToken.None,
            static () => new CompletionTrackingDisposable());
        var window = new Window { Width = 400, Height = 240, Content = diagram };
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.HasImage);
            diagram.Focusable.ShouldBeTrue();
            diagram.Focus().ShouldBeTrue();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            var peer = ControlAutomationPeer.CreatePeerForElement(diagram);
            peer.ShouldNotBeNull();
            peer.GetName().ShouldBe("Architecture diagram");
            var linkPeer = peer.GetChildren().Single();
            linkPeer.GetAutomationControlType().ShouldBe(AutomationControlType.Hyperlink);
            linkPeer.GetName().ShouldBe("Documentation");
            linkPeer.IsKeyboardFocusable().ShouldBeFalse();
            linkPeer.HasKeyboardFocus().ShouldBeFalse();
            linkPeer.SetFocus();
            linkPeer.HasKeyboardFocus().ShouldBeFalse();
            var invokeProvider = linkPeer.ShouldBeAssignableTo<IInvokeProvider>();
            invokeProvider.Invoke();

            await activated.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            activationCount.ShouldBe(2);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [MemberData(nameof(SupportedDiagrams))]
    public async Task MermaiderRendersEverySupportedDiagramGrammar(string name, string source)
    {
        var renderer = new MermaiderSvgRenderer();
        var svg = await renderer.RenderAsync(
            source,
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None);

        svg.ShouldContain("<svg", customMessage: name);
        svg.ShouldContain("viewBox", customMessage: name);
        svg.ShouldNotContain("<script", Case.Insensitive, customMessage: name);
        svg.ShouldNotContain("<foreignObject", Case.Insensitive, customMessage: name);
    }

    [Fact]
    public async Task DarkThemeUsesOpaqueContainerAndForegroundTokens()
    {
        var renderer = new MermaiderSvgRenderer();
        var svg = await renderer.RenderAsync(
            "flowchart TD\nA[Start] --> B[End]",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            CancellationToken.None);

        svg.ShouldContain("#292D33", Case.Insensitive);
        svg.ShouldContain("#E6EDF3", Case.Insensitive);
        svg.ShouldNotContain("fill-opacity=\"0", Case.Insensitive);
    }

    [Fact]
    public async Task CategoricalColorsDeriveFromAccentAndParticipateInCacheIdentity()
    {
        var configuredPalettes = new List<string[]>();
        var renderer = new MermaiderSvgRenderer((_, destination, options, cancellationToken) =>
        {
            configuredPalettes.Add(options.DataPalette ?? []);
            return WriteMinimalSvgAsync(destination, cancellationToken);
        });
        const string source = "pie\n\"One\" : 1\n\"Two\" : 2";
        var bluePalette = new MarkdownThemePalette
        {
            IsDark = true,
            Accent = new SolidColorBrush(Color.Parse("#FF2563EB"))
        };
        var redPalette = new MarkdownThemePalette
        {
            IsDark = true,
            Accent = new SolidColorBrush(Color.Parse("#FFDC2626"))
        };

        await renderer.RenderAsync(source, bluePalette, "Inter", 14, CancellationToken.None);
        await renderer.RenderAsync(source, redPalette, "Inter", 14, CancellationToken.None);

        renderer.RenderInvocationCount.ShouldBe(2);
        configuredPalettes.Count.ShouldBe(2);
        configuredPalettes.ShouldAllBe(colors => colors.Length == 8);
        configuredPalettes.SelectMany(static colors => colors)
            .ShouldAllBe(color => color.Length == 7 && color[0] == '#');
        configuredPalettes[0].ShouldNotBe(configuredPalettes[1]);
    }

    [Fact]
    public async Task RendererUsesForegroundForConnectorsAndCachesBorderSeparately()
    {
        var configuredColors = new List<(string? Accent, string? Line, string? Border)>();
        var renderer = new MermaiderSvgRenderer((_, destination, options, cancellationToken) =>
        {
            configuredColors.Add((options.Accent, options.Line, options.Border));
            return WriteMinimalSvgAsync(destination, cancellationToken);
        });
        const string source = "flowchart TD\nA --> B";
        var firstPalette = new MarkdownThemePalette
        {
            IsDark = true,
            Foreground = new SolidColorBrush(Color.Parse("#FFDECADE")),
            Accent = new SolidColorBrush(Color.Parse("#FF123456")),
            Border = new SolidColorBrush(Color.Parse("#FF654321"))
        };
        var secondPalette = new MarkdownThemePalette
        {
            IsDark = true,
            Foreground = firstPalette.Foreground,
            Accent = firstPalette.Accent,
            Border = new SolidColorBrush(Color.Parse("#FFABCDEF"))
        };

        await renderer.RenderAsync(source, firstPalette, "Inter", 14, CancellationToken.None);
        await renderer.RenderAsync(source, secondPalette, "Inter", 14, CancellationToken.None);

        renderer.RenderInvocationCount.ShouldBe(2);
        configuredColors.ShouldBe([
            ("#DECADE", "#DECADE", "#654321"),
            ("#DECADE", "#DECADE", "#ABCDEF")
        ]);
    }

    [Fact]
    public async Task RendererDeduplicatesAndHonorsLimitsAndCancellation()
    {
        var renderer = new MermaiderSvgRenderer();
        const string source = "flowchart TD\nA --> B";
        var first = renderer.RenderAsync(source, MarkdownThemePalette.Light, "Inter", 14, CancellationToken.None);
        var second = renderer.RenderAsync(source, MarkdownThemePalette.Light, "Inter", 14, CancellationToken.None);
        (await first).ShouldBe(await second);
        renderer.RenderInvocationCount.ShouldBe(1);

        await Should.ThrowAsync<InvalidOperationException>(() => renderer.RenderAsync(
            new string('x', MermaiderSvgRenderer.MaximumSourceCharacters + 1),
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None));
        await Should.ThrowAsync<OperationCanceledException>(() => renderer.RenderAsync(
            source,
            MarkdownThemePalette.Light,
            "Inter",
            14,
            new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task RendererRejectsInvalidSyntaxAndConfiguresTheFifteenSecondDeadline()
    {
        TimeSpan? deadline = null;
        var configuredRenderer = new MermaiderSvgRenderer((_, destination, options, cancellationToken) =>
        {
            deadline = options.Limits?.RenderDeadline;
            return WriteMinimalSvgAsync(destination, cancellationToken);
        });
        await configuredRenderer.RenderAsync(
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None);

        deadline.ShouldBe(TimeSpan.FromSeconds(15));

        var renderer = new MermaiderSvgRenderer();
        await Should.ThrowAsync<MermaidParseException>(() => renderer.RenderAsync(
            "this is not a supported Mermaid diagram",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None));
    }

    [Fact]
    public async Task RendererAppliesAnOperationWideTimeout()
    {
        var renderer = new MermaiderSvgRenderer(
            async (_, _, _, cancellationToken) =>
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
            TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<TimeoutException>(() => renderer.RenderAsync(
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None));
        await WaitForAsync(() => renderer.CachedResultCount == 0);
    }

    [Fact]
    public async Task RendererTimesOutWhenDelegateIgnoresCancellation()
    {
        var releaseFirstRender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderCalls = 0;
        var renderer = new MermaiderSvgRenderer(
            async (_, destination, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref renderCalls) == 1)
                    await releaseFirstRender.Task.ConfigureAwait(false);

                await WriteMinimalSvgAsync(destination, cancellationToken);
            },
            TimeSpan.FromMilliseconds(50));

        var timedOutRender = Should.ThrowAsync<TimeoutException>(() => renderer.RenderAsync(
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None));

        try
        {
            await timedOutRender.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseFirstRender.TrySetResult();
        }

        var svg = await renderer.RenderAsync(
            "flowchart TD\nC --> D",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            TestContext.Current.CancellationToken);

        svg.ShouldContain("<svg");
        renderCalls.ShouldBe(2);
        await WaitForAsync(() => renderer.CachedResultCount == 1);
    }

    [Fact]
    public async Task RendererTimesOutWhenDelegateBlocksBeforeReturningItsTask()
    {
        var renderer = new MermaiderSvgRenderer(
            (_, destination, _, cancellationToken) =>
            {
                Thread.Sleep(250);
                return WriteMinimalSvgAsync(destination, cancellationToken);
            },
            TimeSpan.FromMilliseconds(40));
        var started = Stopwatch.StartNew();

        await Should.ThrowAsync<TimeoutException>(() => renderer.RenderAsync(
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None));

        started.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(200));
        await WaitForAsync(() => renderer.CachedResultCount == 0);
    }

    [Fact]
    public async Task RendererStopsBufferingAtConfiguredOutputLimit()
    {
        var chunk = new byte[64 * 1024];
        var renderer = new MermaiderSvgRenderer(async (_, destination, _, cancellationToken) =>
        {
            var writeCount = MermaiderSvgRenderer.MaximumSvgCharacters / chunk.Length + 1;
            for (var index = 0; index < writeCount; index++)
                await destination.WriteAsync(chunk, cancellationToken);
        });

        await Should.ThrowAsync<InvalidOperationException>(() => renderer.RenderAsync(
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None));
        await WaitForAsync(() => renderer.CachedResultCount == 0);
    }

    [Fact]
    public async Task RendererTimeoutIncludesTimeWaitingForRenderSlot()
    {
        var firstRenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderCalls = 0;
        var renderer = new MermaiderSvgRenderer(
            async (_, destination, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref renderCalls) == 1)
                {
                    firstRenderStarted.TrySetResult();
                    await releaseFirstRender.Task.ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await destination.WriteAsync("<svg viewBox=\"0 0 1 1\"/>"u8.ToArray(), cancellationToken);
            },
            TimeSpan.FromMilliseconds(50));

        var firstRender = renderer.RenderAsync(
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None);
        await firstRenderStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        try
        {
            await Should.ThrowAsync<TimeoutException>(() => renderer.RenderAsync(
                "flowchart TD\nC --> D",
                MarkdownThemePalette.Light,
                "Inter",
                14,
                CancellationToken.None));
        }
        finally
        {
            releaseFirstRender.TrySetResult();
        }

        await Should.ThrowAsync<TimeoutException>(() => firstRender);
        await WaitForAsync(() => renderer.CachedResultCount == 0);
    }

    [AvaloniaFact]
    public async Task RenderingStartsOffTheUiThread()
    {
        var uiThread = Environment.CurrentManagedThreadId;
        var renderThread = uiThread;
        var renderer = new MermaiderSvgRenderer(async (_, destination, _, cancellationToken) =>
        {
            renderThread = Environment.CurrentManagedThreadId;
            await WriteMinimalSvgAsync(destination, cancellationToken);
        });

        await renderer.RenderAsync(
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            CancellationToken.None);

        renderThread.ShouldNotBe(uiThread);
    }

    [Fact]
    public async Task CancelingTheLastWaiterStopsAndRemovesTheStaleRender()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new MermaiderSvgRenderer(async (_, _, _, cancellationToken) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                canceled.TrySetResult();
                throw;
            }
        });
        using var cancellation = new CancellationTokenSource();
        var render = renderer.RenderAsync(
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14,
            cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await render);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await WaitForAsync(() => renderer.CachedResultCount == 0);
    }

    [Fact]
    public async Task CacheNeverExceedsConfiguredEntryOrOutputLimits()
    {
        var renderer = new MermaiderSvgRenderer(
            (_, destination, _, cancellationToken) => WriteMinimalSvgAsync(destination, cancellationToken));

        for (var index = 0; index < MermaiderSvgRenderer.MaximumCachedResults + 8; index++)
        {
            await renderer.RenderAsync(
                $"flowchart TD\nA{index} --> B{index}",
                MarkdownThemePalette.Light,
                "Inter",
                14,
                CancellationToken.None);
        }

        renderer.CachedResultCount.ShouldBeLessThanOrEqualTo(MermaiderSvgRenderer.MaximumCachedResults);
        renderer.CachedOutputBytes.ShouldBeLessThanOrEqualTo(MermaiderSvgRenderer.MaximumCacheBytes);
    }

    [Fact]
    public async Task CapacityPressureDoesNotCancelActiveRenderWaiters()
    {
        var renderer = new MermaiderSvgRenderer(async (_, destination, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            await WriteMinimalSvgAsync(destination, cancellationToken);
        });
        var cancellations = Enumerable.Range(0, MermaiderSvgRenderer.MaximumCachedResults)
            .Select(_ => new CancellationTokenSource())
            .ToArray();
        var renders = cancellations
            .Select((cancellation, index) => renderer.RenderAsync(
                $"flowchart TD\nA{index} --> B{index}",
                MarkdownThemePalette.Light,
                "Inter",
                14,
                cancellation.Token))
            .ToArray();

        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() => renderer.RenderAsync(
                "flowchart TD\nOverflow --> Rejected",
                MarkdownThemePalette.Light,
                "Inter",
                14,
                CancellationToken.None));

            renderer.CachedResultCount.ShouldBe(MermaiderSvgRenderer.MaximumCachedResults);
            renders.ShouldAllBe(render => !render.IsCompleted);
        }
        finally
        {
            foreach (var cancellation in cancellations)
                cancellation.Cancel();
            foreach (var render in renders)
                await Should.ThrowAsync<OperationCanceledException>(async () => await render);
            foreach (var cancellation in cancellations)
                cancellation.Dispose();
        }
    }

    [Fact]
    public void SanitizerProcessesTheRootAndBlockedElementsCaseInsensitively()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)">
              <SCRIPT>alert(1)</SCRIPT>
              <foreignObject><div>unsafe</div></foreignObject>
              <rect width="10" height="10" />
            </svg>
            """;

        var sanitized = MermaidSvgSanitizer.Sanitize(svg);

        sanitized.ShouldNotContain("onload", Case.Insensitive);
        sanitized.ShouldNotContain("script", Case.Insensitive);
        sanitized.ShouldNotContain("foreignObject", Case.Insensitive);
    }

    [Fact]
    public void SanitizerRemovesXmlBaseBeforeKeepingFragmentReferences()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" xml:base="https://example.com/external.svg">
              <defs><path id="node" d="M0 0 L10 10" /></defs>
              <use href="#node" />
            </svg>
            """;

        var sanitized = MermaidSvgSanitizer.Sanitize(svg);

        sanitized.ShouldNotContain("xml:base", Case.Insensitive);
        sanitized.ShouldContain("href=\"#node\"", Case.Insensitive);
    }

    [Fact]
    public void SanitizerRejectsExternalPaintReferencesAndForeignSvgNamespaces()
    {
        Should.Throw<InvalidOperationException>(() => MermaidSvgSanitizer.Sanitize(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect fill=\"url(https://example.com/fill.svg)\" /></svg>"));
        Should.Throw<InvalidOperationException>(() => MermaidSvgSanitizer.Sanitize(
            "<svg xmlns=\"https://example.com/not-svg\"><rect /></svg>"));
    }

    [Fact]
    public void SanitizerRemovesEmbeddedSvgImagesButKeepsSafeRasterImages()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <image id="vector" href="data:image/svg+xml;base64,PHN2Zy8+" />
              <image id="raster" href="data:image/png;base64,AA==" />
            </svg>
            """;

        var sanitized = MermaidSvgSanitizer.Sanitize(svg);

        sanitized.ShouldNotContain("data:image/svg+xml", Case.Insensitive);
        sanitized.ShouldContain("data:image/png;base64,AA==", Case.Insensitive);
    }

    [AvaloniaFact]
    public async Task DiagramAcquiresItsInitialOperationBeforeAttachmentAndObservesLinkFailures()
    {
        var beginCalls = 0;
        var operation = new CompletionTrackingDisposable();
        using var diagram = new MermaidDiagramControl(
            new NonCancelingRenderer(),
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions
            {
                ActivateLinkAsync = static (_, _) => Task.FromException(new InvalidOperationException("launch failed"))
            },
            CancellationToken.None,
            () =>
            {
                beginCalls++;
                return operation;
            });

        beginCalls.ShouldBe(1);
        diagram.Foreground.ShouldBeSameAs(MarkdownThemePalette.Dark.Foreground);
        await diagram.ActivateLinkSafelyAsync(new Uri("https://example.com"));

        diagram.Dispose();
        await operation.Disposed.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public async Task OffTreeDiagramCompletesItsParentRenderGeneration()
    {
        var renderer = new NonCancelingRenderer();
        var control = new ProMarkdown.Controls.MarkdownTextBlock
        {
            RenderController = MarkdownRenderingServices.CreateController(
                new MermaidMarkdownPlugin(renderer))
        };
        control.Markdown = "```mermaid\nflowchart TD\nA --> B\n```";

        await renderer.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        control.IsRendering.ShouldBeTrue();

        renderer.Complete();

        await WaitForAsync(() => !control.IsRendering);
        control.IsRendering.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task DetachingPendingDiagramClearsLoadingState()
    {
        var renderer = new NonCancelingRenderer();
        using var diagram = new MermaidDiagramControl(
            renderer,
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions(),
            CancellationToken.None,
            static () => new CompletionTrackingDisposable());
        var window = new Window { Width = 400, Height = 240, Content = diagram };
        window.Show();
        try
        {
            await renderer.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            diagram.IsLoading.ShouldBeTrue();

            window.Content = null;

            diagram.IsLoading.ShouldBeFalse();
        }
        finally
        {
            renderer.Complete();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OffTreeThemeReplacementRendersTheNewPalette()
    {
        var renderer = new PaletteRecordingRenderer();
        var lightPalette = new MarkdownThemePalette
        {
            Foreground = new SolidColorBrush(Color.Parse("#FF101010"))
        };
        var darkPalette = new MarkdownThemePalette
        {
            IsDark = true,
            Foreground = new SolidColorBrush(Color.Parse("#FFF0F0F0"))
        };
        using var diagram = new MermaidDiagramControl(
            renderer,
            "flowchart TD\nA --> B",
            lightPalette,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions(),
            CancellationToken.None,
            static () => new CompletionTrackingDisposable());

        await WaitForAsync(() => renderer.Requests.Count == 1 && !diagram.IsLoading);
        diagram.ThemePalette = darkPalette;
        await WaitForAsync(() => renderer.Requests.Count == 2 && !diagram.IsLoading);

        var renderedPalettes = renderer.Requests.ToArray()
            .Select(static request => request.Palette)
            .ToArray();
        renderedPalettes.Select(static palette => palette.IsDark).ShouldBe([false, true]);
        renderedPalettes
            .Select(static palette => palette.Foreground.ShouldBeAssignableTo<ISolidColorBrush>().Color)
            .ShouldBe([Color.Parse("#FF101010"), Color.Parse("#FFF0F0F0")]);
        diagram.Foreground.ShouldBeSameAs(darkPalette.Foreground);
    }

    [AvaloniaFact]
    public async Task ResizingARenderedDiagramDoesNotRegenerateSvg()
    {
        var renderer = new MermaiderSvgRenderer(
            (_, destination, _, cancellationToken) => WriteMinimalSvgAsync(destination, cancellationToken));
        using var diagram = new MermaidDiagramControl(
            renderer,
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Light,
            "Inter",
            14);
        var window = new Window { Width = 500, Height = 300, Content = diagram };
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Image>().Any(image => image.IsVisible));
            var invocationCount = renderer.RenderInvocationCount;

            window.Width = 280;
            window.UpdateLayout();
            await Task.Delay(50);

            renderer.RenderInvocationCount.ShouldBe(invocationCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task UiOwnedPaletteBrushIsSnapshottedBeforeBackgroundRendering()
    {
        var accentBrush = new SolidColorBrush(Color.Parse("#FF7C3AED"));
        accentBrush.Color.ShouldBe(Color.Parse("#FF7C3AED"));

        var renderer = new MermaiderSvgRenderer(
            (_, destination, _, cancellationToken) => WriteMinimalSvgAsync(destination, cancellationToken));
        var palette = new MarkdownThemePalette
        {
            IsDark = true,
            Accent = accentBrush
        };
        using var diagram = new MermaidDiagramControl(
            renderer,
            "flowchart TD\nA --> B",
            palette,
            "Inter",
            14);
        var window = new Window { Width = 500, Height = 300, Content = diagram };
        window.Show();

        try
        {
            await WaitForAsync(() => diagram.HasImage || diagram.HasError);

            diagram.HasError.ShouldBeFalse(diagram.ErrorText);
            diagram.HasImage.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DisposingDiagramReleasesItsOperationWhenInjectedRendererIgnoresCancellation()
    {
        var renderer = new NonCancelingRenderer();
        var operation = new CompletionTrackingDisposable();
        using var diagram = new MermaidDiagramControl(
            renderer,
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions(),
            CancellationToken.None,
            () => operation);
        var window = new Window { Width = 500, Height = 300, Content = diagram };
        window.Show();
        try
        {
            await renderer.Started.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            diagram.Dispose();

            await operation.Disposed.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            renderer.PendingRender.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            renderer.Complete();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ThemeReplacementShowsTheCachedDiagramUntilTheNewSvgIsReady()
    {
        var releaseThemedRender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var renderer = new MermaiderSvgRenderer(async (_, destination, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) > 1)
                await releaseThemedRender.Task.WaitAsync(cancellationToken);
            await WriteMinimalSvgAsync(destination, cancellationToken);
        });
        const string source = "flowchart TD\nA --> B";
        await renderer.RenderAsync(
            source,
            MarkdownThemePalette.Light,
            "Inter",
            14,
            TestContext.Current.CancellationToken);

        using var diagram = new MermaidDiagramControl(
            renderer,
            source,
            MarkdownThemePalette.Dark,
            "Inter",
            14);
        var window = new Window { Width = 500, Height = 300, Content = diagram };
        window.Show();
        try
        {
            await WaitForAsync(() => renderer.RenderInvocationCount >= 2);
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Image>().Any(image => image.IsVisible));
            releaseThemedRender.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            releaseThemedRender.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ValidationFailureEvictsTheResultAndPreservesTheCachedPreviewWithAnError()
    {
        var themedRenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseThemedRender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderCalls = 0;
        var parseCalls = 0;
        var renderer = new MermaiderSvgRenderer(async (_, destination, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref renderCalls) == 2)
            {
                themedRenderStarted.TrySetResult();
                await releaseThemedRender.Task.WaitAsync(cancellationToken);
            }

            await WriteMinimalSvgAsync(destination, cancellationToken);
        });
        const string source = "flowchart TD\nA --> B";
        await renderer.RenderAsync(
            source,
            MarkdownThemePalette.Light,
            "Inter",
            14,
            TestContext.Current.CancellationToken);

        SvgSource CreateSource(string svg)
        {
            if (Interlocked.Increment(ref parseCalls) == 2)
                throw new InvalidOperationException("The generated SVG could not be parsed.");
            return SvgSource.LoadFromSvg(MermaidSvgSanitizer.Sanitize(svg));
        }

        using var diagram = new MermaidDiagramControl(
            renderer,
            source,
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            CreateSource);
        var window = new Window { Width = 500, Height = 300, Content = diagram };
        window.Show();
        try
        {
            await themedRenderStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Image>()
                .Any(image => image.IsVisible));
            diagram.IsLoading.ShouldBeTrue();
            diagram.GetVisualDescendants().OfType<ProgressBar>()
                .ShouldAllBe(progress => !progress.IsVisible);

            releaseThemedRender.TrySetResult();
            await WaitForAsync(() => diagram.HasError);
            diagram.GetVisualDescendants().OfType<StackPanel>()
                .ShouldContain(panel => panel.IsVisible && panel.Children.OfType<Button>()
                    .Any(button => Equals(button.Content, "Retry")));
            diagram.GetVisualDescendants().OfType<Image>()
                .ShouldAllBe(image => image.IsVisible);

            var retry = diagram.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Retry"));
            retry.Command!.Execute(retry.CommandParameter);

            await WaitForAsync(() => renderCalls == 3);
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Image>()
                .Any(image => image.IsVisible));
            renderCalls.ShouldBe(3);
        }
        finally
        {
            releaseThemedRender.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ReattachingACachedThemePreviewResumesTheTargetThemeRender()
    {
        var themedRenderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var themedRenderCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = 0;
        var renderer = new MermaiderSvgRenderer(async (_, destination, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref invocation) == 2)
            {
                themedRenderStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    themedRenderCanceled.TrySetResult();
                    throw;
                }
            }

            await WriteMinimalSvgAsync(destination, cancellationToken);
        });
        const string source = "flowchart TD\nA --> B";
        await renderer.RenderAsync(
            source,
            MarkdownThemePalette.Light,
            "Inter",
            14,
            TestContext.Current.CancellationToken);

        using var diagram = new MermaidDiagramControl(
            renderer,
            source,
            MarkdownThemePalette.Dark,
            "Inter",
            14);
        var window = new Window { Width = 500, Height = 300, Content = diagram };
        window.Show();
        try
        {
            await themedRenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Image>().Any(image => image.IsVisible));

            window.Content = null;
            await themedRenderCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            window.Content = diagram;
            window.UpdateLayout();

            await WaitForAsync(() => renderer.RenderInvocationCount >= 3);
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<ProgressBar>()
                .All(progress => !progress.IsVisible));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task FailedDiagramShowsSelectableSourceAndCanBeRetried()
    {
        var invocation = 0;
        var renderer = new MermaiderSvgRenderer((_, destination, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref invocation) == 1)
                throw new InvalidOperationException("Invalid diagram syntax.");
            return WriteMinimalSvgAsync(destination, cancellationToken);
        });
        const string source = "flowchart TD\nA -->";
        const double fontSize = 10;
        using var diagram = new MermaidDiagramControl(
            renderer,
            source,
            MarkdownThemePalette.Dark,
            "Inter",
            fontSize);
        var window = new Window { Width = 500, Height = 300, Content = diagram };
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Button>()
                .Any(button => button.IsVisible && Equals(button.Content, "Retry")));
            var sourceText = diagram.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Single(text => text.Text == source);
            sourceText.Text.ShouldBe(source);
            diagram.SourceFontSize.ShouldBe(12);
            sourceText.FontSize.ShouldBe(diagram.SourceFontSize);

            var retry = diagram.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Retry"));
            retry.Command!.Execute(retry.CommandParameter);

            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Image>()
                .Any(image => image.IsVisible));
            invocation.ShouldBe(2);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MermaidOwnsInputInsideAMarkdownDocument()
    {
        var renderer = new MermaiderSvgRenderer(
            (_, _, _, _) => throw new InvalidOperationException("Invalid diagram syntax."));
        const string source = "flowchart TD\nA -->";
        var markdown = CreateMermaidMarkdown($"```mermaid\n{source}\n```", renderer);
        var window = new Window { Width = 500, Height = 300, Content = markdown };
        window.Show();
        try
        {
            var diagram = EnumerateMermaidControls(markdown.Inlines!).Single();
            await WaitForAsync(() => diagram.GetVisualDescendants()
                .OfType<SelectableTextBlock>()
                .Any(text => text.IsVisible && text.Text == source));
            var sourceText = diagram.GetVisualDescendants()
                .OfType<SelectableTextBlock>()
                .Single(text => text.Text == source);

            diagram.ShouldBeAssignableTo<IMarkdownInputBoundary>();
            MarkdownDocumentSelection.ShouldBypassDocumentInput(markdown, sourceText).ShouldBeTrue();
            sourceText.SelectAll();
            sourceText.SelectedText.ShouldBe(source);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DiagramWithoutLinksDoesNotEnterTheTabOrder()
    {
        using var diagram = new MermaidDiagramControl(
            new StaticSvgRenderer(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 50\"><rect width=\"100\" height=\"50\" /></svg>"),
            "flowchart TD\nA --> B",
            MarkdownThemePalette.Dark,
            "Inter",
            14,
            new MermaidMarkdownPluginOptions(),
            CancellationToken.None,
            static () => new CompletionTrackingDisposable());
        var window = new Window { Width = 400, Height = 240, Content = diagram };
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.HasImage);

            diagram.Focusable.ShouldBeFalse();
            var peer = ControlAutomationPeer.CreatePeerForElement(diagram);
            peer.ShouldNotBeNull();
            peer.GetChildren().ShouldBeEmpty();
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData("mermaid")]
    [InlineData("mmd")]
    [InlineData("mermaidjs")]
    [InlineData("diagram-mermaid")]
    public void ExistingFenceAliasesRemainCompatible(string alias)
    {
        MermaidSyntax.TryParseDescriptor(alias, null, out var normalized, out _).ShouldBeTrue();
        normalized.ShouldBe("mermaid");
    }

    [Theory]
    [InlineData("```mermaid\nflowchart TD\nA --> B\n```")]
    [InlineData("```mmd\nflowchart TD\nA --> B\n```")]
    [InlineData(":::mermaid\nflowchart TD\nA --> B\n:::")]
    [InlineData(":::diagram mermaid\nflowchart TD\nA --> B\n:::")]
    public void ExistingFenceAndContainerSyntaxProducesNativeDiagramControls(string markdown)
    {
        var control = CreateMermaidMarkdown(markdown, new MermaiderSvgRenderer(
            (_, destination, _, cancellationToken) => WriteMinimalSvgAsync(destination, cancellationToken)));

        EnumerateMermaidControls(control.Inlines!).Count().ShouldBe(1);
    }

    [Fact]
    public void NonMermaidFencesRemainOrdinarySelectableCodeBlocks()
    {
        const string source = "Console.WriteLine(\"ordinary code\");";
        var control = CreateMermaidMarkdown(
            $"```csharp\n{source}\n```",
            new MermaiderSvgRenderer((_, destination, _, cancellationToken) =>
                WriteMinimalSvgAsync(destination, cancellationToken)));

        EnumerateMermaidControls(control.Inlines!).ShouldBeEmpty();
        MarkdownDocumentSelection.SelectAll(control);
        MarkdownDocumentSelection.GetSelectedText(control).ShouldContain(source);
    }

    [AvaloniaFact]
    public async Task RendererReceivesNormalizedFenceSourceWithoutReinterpretation()
    {
        var capturedSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var renderer = new MermaiderSvgRenderer(async (source, destination, _, cancellationToken) =>
        {
            capturedSource.TrySetResult(source);
            await WriteMinimalSvgAsync(destination, cancellationToken);
        });
        var control = CreateMermaidMarkdown(
            "```mermaid\nflowchart TD\n  A[One] --> B[Two]\n```",
            renderer);
        var window = new Window { Width = 500, Height = 300, Content = control };
        window.Show();
        try
        {
            var source = await capturedSource.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            source.ShouldBe("flowchart TD\n  A[One] --> B[Two]");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ExistingPublicPluginEntryPointRemainsAvailable()
    {
        _ = new MermaidMarkdownPlugin();
        MermaidMarkdownPlugin.MermaidEditorId.ShouldBe("mermaid-diagram-editor");
    }

    private static ProMarkdown.Controls.MarkdownTextBlock CreateMermaidMarkdown(
        string markdown,
        MermaiderSvgRenderer renderer)
    {
        var control = new ProMarkdown.Controls.MarkdownTextBlock
        {
            FontSize = 14,
            Foreground = Brushes.Black,
            RenderController = MarkdownRenderingServices.CreateController(new TestMermaidPlugin(renderer)),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        control.Markdown = markdown;
        return control;
    }

    private static IEnumerable<MermaidDiagramControl> EnumerateMermaidControls(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var nested in EnumerateMermaidControls(span.Inlines))
                    yield return nested;
            }

            if (inline is InlineUIContainer { Child: { } child })
            {
                foreach (var nested in EnumerateMermaidControls(child))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<MermaidDiagramControl> EnumerateMermaidControls(Control control)
    {
        if (control is MermaidDiagramControl diagram)
            yield return diagram;
        if (control is TextBlock { Inlines: { } inlines })
        {
            foreach (var nested in EnumerateMermaidControls(inlines))
                yield return nested;
        }

        foreach (var child in control.GetVisualChildren().OfType<Control>())
        {
            foreach (var nested in EnumerateMermaidControls(child))
                yield return nested;
        }
    }

    private sealed class TestMermaidPlugin(MermaiderSvgRenderer renderer) : IMarkdownPlugin
    {
        public void Register(MarkdownPluginRegistry registry) => registry
            .AddParserPlugin(new MermaidParserPlugin())
            .AddBlockRenderingPlugin(new MermaidDiagramBlockRenderingPlugin(renderer));
    }

    private const string LinkedSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 50\"><a href=\"https://example.com/docs\" title=\"Documentation\"><rect width=\"100\" height=\"50\" /></a></svg>";

    private sealed class NonCancelingRenderer : IMermaidSvgRenderer
    {
        private readonly TaskCompletionSource<string> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> PendingRender => _completion.Task;

        public Task<string> RenderAsync(
            MermaidSvgRenderRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete() => _completion.TrySetResult(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 50\" />");
    }

    private sealed class PaletteRecordingRenderer : IMermaidSvgRenderer
    {
        public System.Collections.Concurrent.ConcurrentQueue<MermaidSvgRenderRequest> Requests { get; } = new();

        public Task<string> RenderAsync(
            MermaidSvgRenderRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(request);
            return Task.FromResult(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 50\" />");
        }
    }

    private sealed class StaticSvgRenderer(string svg) : IMermaidSvgRenderer
    {
        public Task<string> RenderAsync(
            MermaidSvgRenderRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(svg);
        }
    }

    private sealed class DeferredSvgRenderer : IMermaidSvgRenderer
    {
        private readonly TaskCompletionSource<string> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> RenderAsync(
            MermaidSvgRenderRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete(string svg) => _completion.TrySetResult(svg);
    }

    private sealed class ExceptionRenderer(Exception exception) : IMermaidSvgRenderer
    {
        public Task<string> RenderAsync(
            MermaidSvgRenderRequest request,
            CancellationToken cancellationToken) => Task.FromException<string>(exception);
    }

    private sealed class CompletionTrackingDisposable : IDisposable
    {
        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Disposed.TrySetResult();
    }

    private static async Task WriteMinimalSvgAsync(Stream destination, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 50\"><rect width=\"100\" height=\"50\" /></svg>");
        await destination.WriteAsync(bytes, cancellationToken);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            if (timeout.Elapsed >= TimeSpan.FromSeconds(10))
                throw new TimeoutException("The expected Mermaid visual state was not reached.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
