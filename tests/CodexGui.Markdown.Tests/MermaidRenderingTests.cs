using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.VisualTree;
using CodexGui.Markdown.Plugin.Mermaid;
using CodexGui.Markdown.Services;
using Mermaider;
using Shouldly;
using Xunit;

namespace CodexGui.Markdown.Tests;

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

            releaseThemedRender.TrySetResult();
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<StackPanel>()
                .Any(panel => panel.IsVisible && panel.Children.OfType<Button>()
                    .Any(button => Equals(button.Content, "Retry"))));
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
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Button>()
                .Any(button => button.IsVisible && Equals(button.Content, "Retry")));
            diagram.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Single(text => text.Text == source)
                .Text.ShouldBe(source);

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

    private static CodexGui.Markdown.Controls.MarkdownTextBlock CreateMermaidMarkdown(
        string markdown,
        MermaiderSvgRenderer renderer)
    {
        var control = new CodexGui.Markdown.Controls.MarkdownTextBlock
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
