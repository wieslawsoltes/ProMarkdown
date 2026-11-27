using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using ProMarkdown.Controls;
using ProMarkdown.Plugin.Mermaid;
using ProMarkdown.Services;
using Mermaider;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class MermaidRenderingTests
{
    private const string AnimationSource = """
        flowchart LR
            A[Client] --> B[Server]

        animate once crisp
          step "Send the request"
            send A -> B "GET"
          scenario "Success" as ok
            step "Receive the reply"
              send B -> A "OK" as ok
          scenario "Timeout" as fail
            step "Server stops responding"
              fail B "Timeout"
        """;

    private const string ChartAnimationSource = """
        xychart-beta
            x-axis [Mon, Tue, Wed]
            y-axis 0 --> 100
            bar [34, 52, 48]

        animate scrub
          step "Traffic rises on Tuesday"
            set #2 75
        """;

    [AvaloniaFact]
    public async Task AnimatedFencePlaysAndLetsUsersJumpBetweenStepsAndEndings()
    {
        var preview = new MarkdownTextBlock
        {
            Markdown = $"```mermaid\n{AnimationSource}\n```",
            RenderController = MarkdownRenderingServices.CreateController([new MermaidMarkdownPlugin()])
        };
        var window = new Window { Width = 620, Height = 380, Content = preview };
        window.Styles.Add(new FluentTheme());
        window.Show();
        try
        {
            await WaitForAsync(() => preview.GetVisualDescendants().OfType<MermaidDiagramControl>().Any());
            var diagram = preview.GetVisualDescendants().OfType<MermaidDiagramControl>().Single();
            await WaitForAsync(() => diagram.HasImage || diagram.HasError);
            diagram.HasError.ShouldBeFalse(diagram.ErrorText);
            var host = diagram.GetVisualDescendants().OfType<ContentControl>().Single(control => control.Name == "PART_Playback");
            var playback = host.Content.ShouldBeOfType<MermaidPlayback>();
            host.IsVisible.ShouldBeTrue();
            await WaitForAsync(() => host.GetVisualDescendants().OfType<Slider>().Any());
            var slider = host.GetVisualDescendants().OfType<Slider>().Single();
            var image = diagram.GetVisualDescendants().OfType<Image>().Single(candidate => candidate.Name == "PART_DiagramImage");
            var svg = image.Source.ShouldBeOfType<SvgImage>();
            var firstFrame = svg.Source;
            var buttons = host.GetVisualDescendants().OfType<Button>().Where(button => button.Content is PathIcon).ToArray();
            var playPause = buttons.Single(button => ReferenceEquals(button.Command, playback.PlayPauseCommand));
            var replay = buttons.Single(button => ReferenceEquals(button.Command, playback.ReplayCommand));
            var next = buttons.Single(button => ReferenceEquals(button.Command, playback.NextCommand));
            var previous = buttons.Single(button => ReferenceEquals(button.Command, playback.PreviousCommand));
            var playPauseIcon = playPause.Content.ShouldBeOfType<PathIcon>();
            var playIcon = playPauseIcon.Data.ShouldNotBeNull();
            var playPausePeer = ControlAutomationPeer.CreatePeerForElement(playPause).ShouldNotBeNull();
            playPausePeer.GetName().ShouldBe("Play");
            foreach (var button in buttons)
            {
                button.Bounds.Size.ShouldBe(new Size(28, 28));
                button.TranslatePoint(new Rect(button.Bounds.Size).Center, window)!.Value.Y
                    .ShouldBe(slider.TranslatePoint(new Rect(slider.Bounds.Size).Center, window)!.Value.Y);
                ToolTip.GetTip(button).ShouldBe(AutomationProperties.GetName(button));
            }

            playback.PlayPauseCommand.Execute(null);
            await WaitForAsync(() => slider.Value > 0 && !ReferenceEquals(svg.Source, firstFrame));
            playPausePeer.GetName().ShouldBe("Pause");
            ToolTip.GetTip(playPause).ShouldBe("Pause");
            playPauseIcon.Data.ShouldNotBeSameAs(playIcon);
            playback.PlayPauseCommand.Execute(null);
            playPausePeer.GetName().ShouldBe("Play");
            playPauseIcon.Data.ShouldBeSameAs(playIcon);

            var endings = diagram.GetVisualDescendants().OfType<ComboBox>().Single();
            endings.SelectedIndex.ShouldBe(0);
            var selectorCenter = endings.TranslatePoint(new Rect(endings.Bounds.Size).Center, window).ShouldNotBeNull();
            window.MouseDown(selectorCenter, MouseButton.Left);
            window.MouseUp(selectorCenter, MouseButton.Left);
            var popup = endings.GetVisualDescendants().OfType<Popup>().Single();
            endings.IsDropDownOpen.ShouldBeTrue();
            popup.IsOpen.ShouldBeTrue();
            await WaitForAsync(() => endings.ContainerFromIndex(1) is ComboBoxItem { IsArrangeValid: true });
            var timeoutItem = endings.ContainerFromIndex(1).ShouldBeOfType<ComboBoxItem>();
            var popupRoot = TopLevel.GetTopLevel(timeoutItem).ShouldNotBeNull();
            var timeoutCenter = timeoutItem.TranslatePoint(new Rect(timeoutItem.Bounds.Size).Center, popupRoot).ShouldNotBeNull();
            popupRoot.MouseDown(timeoutCenter, MouseButton.Left);
            popupRoot.MouseUp(timeoutCenter, MouseButton.Left);
            await WaitForAsync(() => !endings.IsDropDownOpen && playback.ScenarioIndex == 1 && slider.Value == 0);
            next.Command!.Execute(null);
            await WaitForAsync(() => slider.Value > 0);
            next.Command!.Execute(null);
            await WaitForAsync(() => !next.IsEffectivelyEnabled && previous.IsEffectivelyEnabled);
            ControlAutomationPeer.CreatePeerForElement(diagram)!.GetHelpText().ShouldContain("Server stops responding");

            replay.Command!.Execute(null);
            await WaitForAsync(() => slider.Value < 0.5);
            playPausePeer.GetName().ShouldBe("Pause");
            await WaitForAsync(() => !playback.IsPlaying && slider.Value == slider.Maximum);

            var endFrame = svg.Source;
            var midpoint = slider.Maximum / 2;
            slider.SetCurrentValue(RangeBase.ValueProperty, midpoint);
            await WaitForAsync(() => Math.Abs(playback.PositionSeconds - midpoint) < 0.001 &&
                                     !ReferenceEquals(svg.Source, endFrame));

            Grid.GetRow(endings).ShouldBe(0);
            window.Width = 300;
            await WaitForAsync(() => Grid.GetRow(endings) == 1 && slider.Bounds.Width > 100);
            playPause.TranslatePoint(new Rect(playPause.Bounds.Size).Center, window)!.Value.Y
                .ShouldBe(slider.TranslatePoint(new Rect(slider.Bounds.Size).Center, window)!.Value.Y);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task HostAndAuthoredReducedMotionReturnTheFinalStillFrame()
    {
        var renderer = new MermaiderSvgRenderer();
        var token = TestContext.Current.CancellationToken;
        var request = new MermaidSvgRenderRequest(AnimationSource, MarkdownThemePalette.Light, "Inter", 14);
        var moving = await renderer.RenderDiagramAsync(request, enableAnimation: true, token);
        moving.Animation.ShouldNotBeNull();

        var reduced = await renderer.RenderDiagramAsync(request, enableAnimation: false, token);
        reduced.Animation.ShouldBeNull();
        reduced.Svg.ShouldNotContain("@keyframes");
        reduced.Svg.ShouldContain("2/2");
        reduced.Svg.ShouldNotBe(moving.Svg);

        var authored = new MermaidSvgRenderRequest(
            AnimationSource.Replace("once crisp", "once still", StringComparison.Ordinal), request.Palette, "Inter", 14);
        var still = await renderer.RenderDiagramAsync(authored, enableAnimation: true, token);
        still.Animation.ShouldBeNull();
        still.Svg.ShouldBe(reduced.Svg);
    }

    [Theory]
    [InlineData("flowchart LR\nanimate --> B")]
    [InlineData("flowchart LR\nanimate\nA --> animate")]
    [InlineData("flowchart LR\nanimate\nstep\nA --> animate --> step")]
    [InlineData("flowchart LR\nanimate\nsend\nA --> animate --> send")]
    [InlineData("flowchart LR\nA --> B\nanimate")]
    [InlineData("flowchart LR\nsubgraph S\nanimate\nend\nA --> animate")]
    [InlineData("stateDiagram-v2\nanimate --> Other")]
    [InlineData("sequenceDiagram\nanimate ->> Receiver: Hello")]
    [InlineData("classDiagram\nanimate --> Other")]
    [InlineData("erDiagram\nanimate ||--o{ Other : includes")]
    [InlineData("gantt\n  dateFormat YYYY-MM-DD\nanimate once :task, 2024-01-01, 1d")]
    [InlineData("mindmap\nanimate\n  child")]
    [InlineData("block-beta\nanimate\nother")]
    [InlineData("treeView-beta\nanimate\n    child")]
    [InlineData("treeView-beta\nroot\nanimate once")]
    [InlineData("treeView-beta\nroot\nanimate once %% label")]
    [InlineData("treeView-beta\nroot\nanimate once ## Status: draft")]
    [InlineData("treeView-beta\nroot\nanimate once :::selected")]
    [InlineData("block-beta\ncolumns 4\nA\nanimate once item:2")]
    [InlineData("block-beta\ncolumns 4\nA\nanimate once item[\"Read: 2\"]")]
    [InlineData("block-beta\nA\nanimate once\n%% ordinary comment\n")]
    [InlineData("flowchart LR\naccDescr {\nanimate once\n}\nA --> B")]
    [InlineData("sequenceDiagram\r\naccDescr{\r\nanimate once\r\n  }\r\nA ->> B: Hello")]
    public async Task StaticDiagramsCanContainAnimateWithoutStartingAScore(string source)
    {
        var renderer = new MermaiderSvgRenderer();
        var token = TestContext.Current.CancellationToken;
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var result = await renderer.RenderDiagramAsync(request, enableAnimation: true, token);

        result.Animation.ShouldBeNull();
        result.IsAnimationFrame.ShouldBeFalse();
        result.Svg.ShouldBe(await renderer.RenderAsync(request, token));
    }

    [Theory]
    [InlineData("mindmap\nroot[First line\nanimate once\nLast line]\n child")]
    [InlineData("mindmap\nroot[First line\n%% ]\nanimate once\nLast line]\n child")]
    [InlineData("treeView-beta\n\"First line\nanimate once\nLast line\"\n    child")]
    [InlineData("treeView-beta\n\"First line\n%% \"\nanimate once\nLast line\"\n    child")]
    [InlineData("block-beta\nA[\"First line\nanimate once\nLast line\"]")]
    [InlineData("block-beta\nA[\"First line\n%% \"\nanimate once\nLast line\"]")]
    public async Task MultilineLabelsCanContainAnimateBeforeTheScore(string source)
    {
        var renderer = new MermaiderSvgRenderer();
        var token = TestContext.Current.CancellationToken;
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var result = await renderer.RenderDiagramAsync(request, enableAnimation: true, token);
        result.IsAnimationFrame.ShouldBeFalse();
        result.Svg.ShouldBe(await renderer.RenderAsync(request, token));

        var withScore = await renderer.RenderDiagramAsync(
            new MermaidSvgRenderRequest(source + "\n\nanimate once\n  build", request.Palette, "Inter", 14),
            enableAnimation: true, token);
        withScore.IsAnimationFrame.ShouldBeTrue();
        withScore.Animation.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("mindmap\nanimate once\n child")]
    [InlineData("treeView-beta\nanimate once\n    child")]
    [InlineData("treeView-beta\nsrc\nanimate once\n    child")]
    [InlineData("treeView-beta\nroot\nanimate once ## Status: draft")]
    [InlineData("treeView-beta\nroot\nanimate once :::selected\n    child")]
    [InlineData("treeView-beta\nroot\nanimate once icon(none)\n    child")]
    [InlineData("treeView-beta\nroot\nanimate once icon(lucide:folder)\n    child")]
    [InlineData("block-beta\ncolumns 4\nA\nanimate once item:2\nA --> item")]
    [InlineData("block-beta\ncolumns 4\nA\nanimate once item[\"Item\"]:2\nA --> item")]
    [InlineData("block-beta\ncolumns 4\nA\nanimate once item[\"Read: 2\"] Other\nA --> item")]
    [InlineData("block-beta\ncolumns 4\nA\nanimate once item[Read: 2]Other\nA --> Other")]
    [InlineData("block-beta\ncolumns 4\nA\nanimate once item:2Other[\"Other\"]\nA --> Other")]
    public async Task StandaloneLabelsCanMatchScoreOptionsAndPrecedeAnAnimation(string source)
    {
        var renderer = new MermaiderSvgRenderer();
        var token = TestContext.Current.CancellationToken;
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var result = await renderer.RenderDiagramAsync(request, enableAnimation: true, token);
        result.IsAnimationFrame.ShouldBeFalse();
        result.Svg.ShouldBe(await renderer.RenderAsync(request, token));

        var withScore = await renderer.RenderDiagramAsync(
            new MermaidSvgRenderRequest(source + "\n\nanimate once\n  build", request.Palette, "Inter", 14),
            enableAnimation: true, token);
        withScore.Animation.ShouldNotBeNull();
    }

    [Fact]
    public async Task OnlyLinesOutsideBlockGroupsCanStartAScore()
    {
        const string diagram = """
            block-beta
            block:group
            A
            block:nested:2
            animate once
            X
            end
            animate once
            Y
            end
            A --> Y
            """;
        MermaidScoreSyntax.Parse(diagram).ShouldBeNull();
        var source = diagram + "\n\nanimate once\n  build";
        MermaidScoreSyntax.Parse(source).ShouldNotBeNull().Headers.Single().Line.ShouldBe(13);
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var result = await new MermaiderSvgRenderer().RenderDiagramAsync(
            request, enableAnimation: true, TestContext.Current.CancellationToken);

        result.Animation.ShouldNotBeNull();
    }

    [Fact]
    public async Task OnlyLinesOutsideFlowchartSubgraphsCanStartAScore()
    {
        const string diagram = """
            flowchart LR
            subgraph Outer
                subgraph Inner
                    A["Label starts
            end
            animate once
            Label ends"]
            animate
                    A --> animate
                end
            animate
                animate --> B
            end
            """;
        MermaidScoreSyntax.Parse(diagram).ShouldBeNull();
        var source = diagram + "\n\nanimate once\n  send A -> animate \"Message\"";
        MermaidScoreSyntax.Parse(source).ShouldNotBeNull().Headers.Single().Line.ShouldBe(15);
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var result = await new MermaiderSvgRenderer().RenderDiagramAsync(
            request, enableAnimation: true, TestContext.Current.CancellationToken);

        result.Animation.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("flowchart LR\nA --> B", "send A -> B \"Hello\"")]
    [InlineData("sequenceDiagram\nA ->> B: Hello", "play")]
    public async Task FrontMatterCanContainAnimateBeforeTheScore(string diagram, string statement)
    {
        const string frontMatter = "---\ntitle: \"First\nanimate once\nLast\"\n---\n";
        var source = frontMatter + diagram;
        var renderer = new MermaiderSvgRenderer();
        var token = TestContext.Current.CancellationToken;
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var result = await renderer.RenderDiagramAsync(request, enableAnimation: true, token);
        result.IsAnimationFrame.ShouldBeFalse();
        result.Svg.ShouldBe(await renderer.RenderAsync(request, token));

        var withScore = await renderer.RenderDiagramAsync(
            new MermaidSvgRenderRequest($"{source}\n\nanimate once\n  {statement}", request.Palette, "Inter", 14),
            enableAnimation: true, token);
        withScore.Animation.ShouldNotBeNull();
    }

    [Fact]
    public async Task MindmapWithARootNamedAnimateKeepsItsOneSpaceChildIndent()
    {
        const string source = "mindmap\nanimate\n child\n\nanimate once\n  build";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var result = await new MermaiderSvgRenderer().RenderDiagramAsync(
            request, enableAnimation: true, TestContext.Current.CancellationToken);

        var animation = result.Animation.ShouldNotBeNull();
        animation.RenderFrame(animation.ScoreDuration).ShouldContain(">child<");
    }

    [Theory]
    [InlineData("animate --> B", "animate once")]
    [InlineData("animate\nA --> B", "animate")]
    [InlineData("animate", "animate once")]
    [InlineData("animate\nstep\nA --> animate --> step", "animate once")]
    [InlineData("animate\nsend\nA --> animate --> send", "animate once")]
    public async Task AnimationCanFollowAFlowchartNodeNamedAnimate(string statement, string header)
    {
        var source = $"flowchart LR\n{statement}\n\n{header}\n  fail animate \"Unavailable\"";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);
        var result = await new MermaiderSvgRenderer().RenderDiagramAsync(
            request, enableAnimation: true, TestContext.Current.CancellationToken);

        result.Animation.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("animate once")]
    [InlineData("animate")]
    public async Task AnimationCanFollowASequenceMessageFromAParticipantNamedAnimate(string header)
    {
        var source = $"sequenceDiagram\nanimate ->> Receiver: Hello\n\n{header}\n  play";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);
        var result = await new MermaiderSvgRenderer().RenderDiagramAsync(
            request, enableAnimation: true, TestContext.Current.CancellationToken);

        result.Animation.ShouldNotBeNull();
    }

    [Fact]
    public async Task AnimationCanFollowAnAccessibilityDescriptionThatContainsAnimate()
    {
        const string source = """
            flowchart LR
            accDescr {
            animate once
            }
            A --> B

            animate once
              send A -> B "Request"
            """;
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);
        var result = await new MermaiderSvgRenderer().RenderDiagramAsync(
            request, enableAnimation: true, TestContext.Current.CancellationToken);

        result.Animation.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("accDescr {Orders; rush}\nA --> B", "Orders; rush", 1)]
    [InlineData("accDescr {Orders; rush}; A --> B", "Orders; rush", 1)]
    [InlineData("accDescr {Outdated}; accDescr {Orders; rush}; A --> B", "Orders; rush", 1)]
    [InlineData("A --> B; accDescr {Orders; rush}", "Orders; rush", 1)]
    [InlineData("accDescr {animate once}\nA --> B", "animate once", 1)]
    [InlineData("accDescr {Orders; rush}; A --> B; B --> C", "Orders; rush", 2)]
    [InlineData("A --> B; B --> C; accDescr {Orders; rush}", "Orders; rush", 2)]
    [InlineData("accDescr {%% Urgent}\nA --> B", "%% Urgent", 1)]
    [InlineData("accDescr {\nFirst\n  %% Urgent\nLast\n}\nA --> B", "First\n%% Urgent\nLast", 1)]
    public async Task PlainAndScoredFlowchartsRenderCompactAccessibilityDescriptions(string body, string description, int edgeCount)
    {
        var renderer = new MermaiderSvgRenderer();
        var token = TestContext.Current.CancellationToken;
        var source = $"flowchart LR\n{body}";
        var plain = await renderer.RenderAsync(new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14), token);
        var scored = await renderer.RenderDiagramAsync(
            new MermaidSvgRenderRequest(source + "\nanimate once\n  send A -> B \"Request\"", MarkdownThemePalette.Light, "Inter", 14),
            enableAnimation: true, token);

        var animation = scored.Animation.ShouldNotBeNull();
        foreach (var svgText in new[] { plain, scored.Svg, animation.RenderFrame(TimeSpan.FromMilliseconds(100)) })
        {
            var svg = XElement.Parse(svgText);
            svg.Element(svg.Name.Namespace + "desc")!.Value.ReplaceLineEndings("\n").ShouldBe(description);
            svg.Descendants().Count(element => (string?)element.Attribute("class") == "edge").ShouldBe(edgeCount);
            svg.Descendants().ShouldContain(element => (string?)element.Attribute("data-from") == "A" &&
                (string?)element.Attribute("data-to") == "B");
        }
    }

    [Fact]
    public async Task CompactAccessibilityDescriptionPreservesAuthoredScoreDiagnosticLine()
    {
        const string source = "flowchart LR\naccDescr {Orders; rush}; A --> B\nanimate once\n  send A -> Missing \"Request\"";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, TestContext.Current.CancellationToken));

        error.Diagnostics.ShouldContain(diagnostic => diagnostic.Line == 4 && diagnostic.Message.Contains("Missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("accDescr {Orders; rush}\nA --> B\nB / broken", 4, 3, "B / broken")]
    [InlineData("accDescr {Orders; rush}; A --> B; B / broken", 2, 37, "accDescr {Orders; rush}; A --> B; B / broken")]
    [InlineData("B / broken\naccDescr {Orders; rush}", 2, 3, "B / broken")]
    [InlineData("accDescr {B / broken}\nA --> B\nB / broken", 4, 3, "B / broken")]
    [InlineData("accDescr {\nB / broken\n}\naccDescr {Orders}\nA --> B\nB / broken", 7, 3, "B / broken")]
    [InlineData("accDescr {Orders}\nA --> B\n  C[\"First line\n    Second line\"] / broken", 5, 15, "Second line\"] / broken")]
    [InlineData("accDescr {Orders}\r\nA --> B\r\n  C[\"First line\r\n    Second line\"] / broken", 5, 15, "Second line\"] / broken")]
    [InlineData("accDescr {Orders}\nA --> B\n  C[\"First line\n   \n    Second line\"] / broken", 6, 15, "Second line\"] / broken")]
    [InlineData("accDescr {C[\"First line\nSecond line\"] / broken}\nA --> B\n  C[\"First line\n    Second line\"] / broken", 6, 15, "Second line\"] / broken")]
    [InlineData("A --> B\nC[\"First line\nSecond line\"] / broken", 4, 15, "Second line\"] / broken")]
    [InlineData("accDescr {\nOrders\n}\nA --> B\nC[\"First line\nSecond line\"] / broken", 7, 15, "Second line\"] / broken")]
    public async Task PlainAndScoredFlowchartsPreserveAuthoredDiagramErrorLocation(
        string body, int line, int column, string sourceLine)
    {
        var renderer = new MermaiderSvgRenderer();
        var token = TestContext.Current.CancellationToken;
        var source = $"flowchart LR\n{body}";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);
        var scored = new MermaidSvgRenderRequest(source + "\nanimate once\n  build", MarkdownThemePalette.Light, "Inter", 14);

        var plainError = await Should.ThrowAsync<Mermaider.MermaidParseException>(() => renderer.RenderAsync(request, token));
        var scoreError = await Should.ThrowAsync<Mermaider.MermaidParseException>(() => renderer.RenderDiagramAsync(scored, true, token));
        foreach (var error in new[] { plainError, scoreError })
        {
            error.Line.ShouldBe(line);
            error.Column.ShouldBe(column);
            error.SourceLine.ShouldBe(sourceLine);
        }
    }

    [Theory]
    [InlineData("A --> B", "")]
    [InlineData("A --> B", "\nanimate once\n  build")]
    [InlineData("A e1@--> B", "\ne1@{ animate: true }")]
    public async Task FlowchartDiagnosticsSkipMatchingStatementsInFrontMatter(string link, string suffix)
    {
        var source = $"---\ntitle: |\n  B / broken\n---\nflowchart LR\n{link}\nB / broken{suffix}";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.MermaidParseException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, true, TestContext.Current.CancellationToken));

        error.Line.ShouldBe(7);
        error.Column.ShouldBe(3);
        error.SourceLine.ShouldBe("B / broken");
    }

    [Theory]
    [InlineData("accTitle: Notes on [\"")]
    [InlineData("accDescr : Notes on [\"")]
    public async Task AnimationCanFollowAccessibilityTextThatLooksLikeALabel(string directive)
    {
        var source = $"flowchart LR\n{directive}\nA --> B\nanimate once\n  build";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var result = await new MermaiderSvgRenderer().RenderDiagramAsync(
            request, enableAnimation: true, TestContext.Current.CancellationToken);

        result.Animation.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("sequenceDiagram\nA ->> B: Hello", "unknown", "play")]
    [InlineData("sequenceDiagram\nA ->> B: Hello", "mode:invalid", "play")]
    [InlineData("flowchart LR\nanimate\nstep\nA --> animate --> step", "mode:invalid", "build")]
    public async Task InvalidScoreOptionsRetainTheAnimationDiagnostic(string diagram, string option, string statement)
    {
        var source = $"{diagram}\nanimate once {option}\n  {statement}";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, TestContext.Current.CancellationToken));
        error.Message.ShouldContain($"Unknown option '{option}'");
    }

    [Theory]
    [InlineData("treeView-beta\nroot\n    child", "animate once unknown\n  build", "Unknown option 'unknown'", 5)]
    [InlineData("treeView-beta\nroot\n    child", "animate \"once", "A quoted text is not closed", 5)]
    [InlineData("treeView-beta\nroot\n    child", "animate once\n  expand Missing", "There is no node 'Missing'", 6)]
    [InlineData("treeView-beta\nroot\n    child", "animate %% first score\n  expand Missing", "There is no node 'Missing'", 6)]
    [InlineData("block-beta\nA B", "animate once unknown\n  build", "Unknown option 'unknown'", 4)]
    [InlineData("block-beta\nA B\nA --> B", "animate once 3:2\n  build", "Unknown option '3:2'", 5)]
    [InlineData("block-beta\nA B", "animate once \"bad item:2\"\n  build", "Unknown option 'bad item:2'", 4)]
    [InlineData("treeView-beta\nroot\n    child", "animate once \"bad ## Status: draft\"\n  build", "Unknown option 'bad ## Status: draft'", 5)]
    [InlineData("treeView-beta\nroot\n    child", "animate once \"bad icon(none)\"\n  build", "Unknown option 'bad icon(none)'", 5)]
    [InlineData("block-beta\nA B\nA --> B", "animate once\n  fail Missing \"Unavailable\"", "There is no node 'Missing'", 6)]
    public async Task ConfiguredTreeAndBlockScoresRetainErrors(
        string diagram, string score, string expectedMessage, int expectedLine)
    {
        var request = new MermaidSvgRenderRequest($"{diagram}\n\n{score}", MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, TestContext.Current.CancellationToken));

        error.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Line == expectedLine && diagnostic.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("treeView-beta\nroot\n    child", "animate once")]
    [InlineData("block-beta\nA B\nA --> B", "animate once")]
    [InlineData("treeView-beta\nroot\n    child", "animate %% first score")]
    [InlineData("block-beta\nA B\nA --> B", "animate once %% play: first score")]
    [InlineData("block-beta\nA B\nA --> B", "animate once\u00a0crisp %% first score")]
    public async Task SecondHeaderCannotReplaceAScoreThatAlreadyHasAStep(string diagram, string header)
    {
        var score = $"""
            {header}
              step "First score"
                build
            animate once
              build
            """;
        var request = new MermaidSvgRenderRequest($"{diagram}\n\n{score}", MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, TestContext.Current.CancellationToken));

        error.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Line == 8 && diagnostic.Message.Contains("Unknown verb 'animate'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("flowchart LR\nA --> B", "animate", "build", "animate once")]
    [InlineData("flowchart LR\nA --> B", "animate", "build", "animate")]
    [InlineData("sequenceDiagram\nA ->> B: Hello", "animate %% first score", "play", "animate once")]
    public async Task SecondHeaderCannotReplaceAnActiveBareScore(
        string diagram, string firstHeader, string statement, string secondHeader)
    {
        var source = $"{diagram}\n\n{firstHeader}\n  {statement}\n{secondHeader}\n  {statement}";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, TestContext.Current.CancellationToken));

        error.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Line == 6 && diagnostic.Message.Contains("Unknown verb 'animate'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\nanimate once\n  build")]
    public async Task BareFlowchartScoreReportsAnEmptyCaptionedStep(string followingScore)
    {
        var source = "flowchart LR\nA --> B\nanimate\n  step \"Empty\"" + followingScore;
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, TestContext.Current.CancellationToken));

        error.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Line == 4 && diagnostic.Message.Contains("This step is empty", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("\nanimate once\n  build", false)]
    [InlineData("", true)]
    [InlineData("\nanimate once\n  build", true)]
    public async Task RejectedFlowchartFallbackReportsOnlyTheActualBareScore(string followingScore, bool repeatInDescription)
    {
        var diagram = "flowchart LR\n" +
            (repeatInDescription ? "accDescr {\nstep \"Empty\"\n}\n" : "") +
            "animate\nstep\nA --> animate --> step";
        var source = diagram + "\n\nanimate\n  step \"Empty\"" + followingScore;
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);
        var token = TestContext.Current.CancellationToken;
        var renderer = new MermaiderSvgRenderer();

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            renderer.RenderDiagramAsync(request, enableAnimation: true, token));

        var expectedLine = repeatInDescription ? 10 : 7;
        error.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Line >= expectedLine);
        error.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Line == expectedLine && diagnostic.Message.Contains("This step is empty", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\nanimate once\n  build")]
    public async Task RejectedFlowchartFallbackSkipsDescriptionsBetweenScoreCandidates(string followingScore)
    {
        const string diagram = """
            flowchart LR
            animate
            step
            A --> animate --> step
            animate
            step
            C --> D
            accDescr {
            step "Empty"
            }

            animate
              step "Empty"
            """;
        var request = new MermaidSvgRenderRequest(diagram + followingScore, MarkdownThemePalette.Light, "Inter", 14);
        var token = TestContext.Current.CancellationToken;

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, token));

        error.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Line >= 13);
        error.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Line == 13 && diagnostic.Message.Contains("This step is empty", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlowchartNodeDeclarationsCanExceedTheAnimationStepLimit(bool appendScore)
    {
        var diagram = "flowchart LR\nA --> B\n" +
            string.Join('\n', Enumerable.Range(0, 65).Select(index => $"animate\nstep\nItem{index}"));
        var source = appendScore ? diagram + "\n\nanimate once\n  send A -> B \"Message\"" : diagram;
        var syntax = MermaidScoreSyntax.Parse(source).ShouldNotBeNull();
        var error = Should.Throw<Mermaider.Animate.AnimationException>(() =>
            Mermaider.Animate.MermaidAnimator.Compile(syntax.PrepareSource(source, syntax.Headers[0])));
        syntax.GetNextCandidateIndex(0, error, out var requiresValidFlowchart).ShouldBe(65);
        requiresValidFlowchart.ShouldBeTrue();

        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);
        var renderer = new MermaiderSvgRenderer();
        var token = TestContext.Current.CancellationToken;
        var result = await renderer.RenderDiagramAsync(request, enableAnimation: true, token);

        if (appendScore)
            result.Animation.ShouldNotBeNull();
        else
            result.Svg.ShouldBe(await renderer.RenderAsync(request, token));
    }

    [Fact]
    public async Task BareFlowchartScoresStillEnforceTheAnimationStepLimit()
    {
        var source = "flowchart LR\nA --> B\nanimate\n" + string.Join('\n', Enumerable.Repeat("  build", 65));
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, TestContext.Current.CancellationToken));

        error.Message.ShouldContain("The score has 65 steps; the limit is 64.");
    }

    [Theory]
    [InlineData("animate once\n    build", 3)]
    [InlineData("animate once unknown\n    fourth label", 3)]
    [InlineData("animate once\n    fourth label", 4)]
    public void ScoreDiagnosticsCanSkipSeveralTreeLabelsInOnePass(string finalCandidate, int nextIndex)
    {
        var source = $"""
            treeView-beta
            root
            animate once
                first label
            animate %% second label
                second label
            animate once
                third label
            {finalCandidate}
            """;
        var syntax = MermaidScoreSyntax.Parse(source).ShouldNotBeNull();
        syntax.Headers.Count.ShouldBe(4);
        var error = Should.Throw<Mermaider.Animate.AnimationException>(() =>
            Mermaider.Animate.MermaidAnimator.Compile(syntax.PrepareSource(source, syntax.Headers[0])));

        syntax.GetNextCandidateIndex(0, error, out var requiresValidFlowchart).ShouldBe(nextIndex);
        requiresValidFlowchart.ShouldBeFalse();
    }

    [Theory]
    [InlineData("  typo")]
    [InlineData("typo")]
    public async Task BareScoresInSequenceDiagramsReportUnknownCommands(string statement)
    {
        var source = $"sequenceDiagram\nA ->> B: Hello\nanimate\n{statement}";
        var request = new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14);

        var error = await Should.ThrowAsync<Mermaider.Animate.AnimationException>(() =>
            new MermaiderSvgRenderer().RenderDiagramAsync(request, enableAnimation: true, TestContext.Current.CancellationToken));
        error.Message.ShouldContain("line 4");
        error.Message.ShouldContain("Unknown verb 'typo'");
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SeekSurvivesLeavingTheViewportDuringFrameRendering(bool scrubsOnScroll, bool newerSeek)
    {
        using var frames = new DelayedPlaybackFrame(TimeSpan.FromSeconds(4), scrubsOnScroll);
        var playback = frames.Playback;
        playback.UpdateViewport(true, 0);
        playback.PositionSeconds = 1;
        await frames.WaitForFirstRequestAsync();
        playback.UpdateViewport(false, 0);
        if (newerSeek)
            playback.PositionSeconds = 2;
        playback.UpdateViewport(true, 0.75);
        frames.ReleaseDelayedRequest();

        await WaitForAsync(() => playback.PositionSeconds == (newerSeek ? 2 : 1));
        frames.DisplayedFrames.ShouldBe(1);
        frames.FrameRequests.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task SeekBackToDisplayedFrameSupersedesAnInFlightSeek()
    {
        using var frames = new DelayedPlaybackFrame(TimeSpan.FromSeconds(4));
        var playback = frames.Playback;
        playback.UpdateViewport(true, 0);
        playback.PositionSeconds = 2;
        (await frames.WaitForFirstRequestAsync()).ShouldBe(TimeSpan.FromSeconds(2));
        playback.PositionSeconds = 0;
        frames.ReleaseDelayedRequest();

        await WaitForAsync(() => frames.DisplayedFrames == 1);
        playback.PositionSeconds.ShouldBe(0);
    }

    [AvaloniaTheory]
    [InlineData(false, 2.999)]
    [InlineData(true, 0.999)]
    public async Task StepCommandsNavigateFromThePendingSeek(bool backwards, double expectedPosition)
    {
        using var frames = new DelayedPlaybackFrame(TimeSpan.FromSeconds(3), steps:
        [
            new("First", TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new("Second", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
            new("Third", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3))
        ]);
        var playback = frames.Playback;
        playback.UpdateViewport(true, 0);

        var command = backwards ? playback.PreviousCommand : playback.NextCommand;
        bool? announcedCanExecute = null;
        command.CanExecuteChanged += (_, _) => announcedCanExecute = command.CanExecute(null);
        if (backwards)
            playback.PositionSeconds = 2.5;
        else
            command.Execute(null);
        await frames.WaitForFirstRequestAsync();

        if (backwards)
            command.Execute(null);
        command.Execute(null);
        announcedCanExecute.ShouldBe(false);
        frames.ReleaseDelayedRequest();

        (await frames.WaitForSecondRequestAsync()).ShouldBe(TimeSpan.FromSeconds(expectedPosition));
        await WaitForAsync(() => frames.DisplayedFrames == 1);
        playback.PositionSeconds.ShouldBe(expectedPosition);
    }

    [AvaloniaFact]
    public async Task LatestVisibleScrollPositionIsRenderedAfterAnInFlightSeek()
    {
        using var frames = new DelayedPlaybackFrame(TimeSpan.FromSeconds(4), scrubsOnScroll: true);
        var playback = frames.Playback;
        playback.UpdateViewport(true, 0);
        playback.PositionSeconds = 2;
        (await frames.WaitForFirstRequestAsync()).ShouldBe(TimeSpan.FromSeconds(2));
        playback.UpdateViewport(true, 0.25);
        playback.UpdateViewport(true, 0.75);
        frames.ReleaseDelayedRequest();

        var scrollPosition = await frames.WaitForSecondRequestAsync();
        scrollPosition.TotalSeconds.ShouldBe(3, 0.083);
        await WaitForAsync(() => playback.PositionSeconds == scrollPosition.TotalSeconds);
        frames.DisplayedFrames.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task PlayStartsAtTheInFlightSeekTarget()
    {
        using var frames = new DelayedPlaybackFrame(TimeSpan.FromSeconds(30));
        var playback = frames.Playback;
        var target = TimeSpan.FromSeconds(2);
        playback.UpdateViewport(true, 0);
        playback.PositionSeconds = target.TotalSeconds;
        (await frames.WaitForFirstRequestAsync()).ShouldBe(target);
        playback.PlayPauseCommand.Execute(null);
        frames.ReleaseDelayedRequest();

        (await frames.WaitForSecondRequestAsync()).ShouldBeGreaterThan(target);
        playback.IsPlaying.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task PlayRestartsWhenAnInFlightSeekTargetsTheEnd()
    {
        var duration = TimeSpan.FromSeconds(4);
        using var frames = new DelayedPlaybackFrame(duration);
        var playback = frames.Playback;
        playback.UpdateViewport(true, 0);
        playback.PositionSeconds = duration.TotalSeconds;
        (await frames.WaitForFirstRequestAsync()).ShouldBe(duration);
        playback.PlayPauseCommand.Execute(null);
        frames.ReleaseDelayedRequest();

        (await frames.WaitForSecondRequestAsync()).ShouldBe(TimeSpan.Zero);
        playback.IsPlaying.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task RepeatingPlaybackWrapsAfterSeekingToTheEndOfANearMaximumTimeline()
    {
        var seconds = Math.BitDecrement(TimeSpan.MaxValue.TotalSeconds);
        using var frames = new DelayedPlaybackFrame(TimeSpan.FromSeconds(seconds), repeats: true);
        var playback = frames.Playback;
        playback.UpdateViewport(true, 0);
        playback.PositionSeconds = seconds;
        (await frames.WaitForFirstRequestAsync()).ShouldBe(TimeSpan.FromSeconds(seconds));
        playback.PlayPauseCommand.Execute(null);
        frames.ReleaseDelayedRequest();

        var wrapped = await frames.WaitForSecondRequestAsync();
        wrapped.ShouldBeGreaterThanOrEqualTo(TimeSpan.Zero);
        wrapped.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        playback.IsPlaying.ShouldBeTrue();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PausePreservesTheReplaySeek(bool queued)
    {
        using var frames = new DelayedPlaybackFrame(TimeSpan.FromSeconds(4), delayedRequest: queued ? 1 : 2);
        var playback = frames.Playback;
        var seekTarget = TimeSpan.FromSeconds(2);
        playback.UpdateViewport(true, 0);
        playback.PositionSeconds = seekTarget.TotalSeconds;
        (await frames.WaitForFirstRequestAsync()).ShouldBe(seekTarget);
        if (!queued)
            await WaitForAsync(() => playback.PositionSeconds == seekTarget.TotalSeconds);

        playback.ReplayCommand.Execute(null);
        if (!queued)
            (await frames.WaitForSecondRequestAsync()).ShouldBe(TimeSpan.Zero);
        playback.PlayPauseCommand.Execute(null);
        frames.ReleaseDelayedRequest();

        (await frames.WaitForSecondRequestAsync()).ShouldBe(TimeSpan.Zero);
        await WaitForAsync(() => frames.DisplayedFrames == (queued ? 1 : 2));
        playback.PositionSeconds.ShouldBe(0);
        playback.IsPlaying.ShouldBeFalse();
        frames.FrameRequests.ShouldBe(2);
    }

    [AvaloniaFact]
    public async Task FinalFrameOfOneShotPlaybackSurvivesLeavingTheViewport()
    {
        var duration = TimeSpan.FromMilliseconds(30);
        using var frames = new DelayedPlaybackFrame(duration);
        var playback = frames.Playback;
        playback.UpdateViewport(true, 0);
        playback.PlayPauseCommand.Execute(null);
        (await frames.WaitForFirstRequestAsync()).ShouldBe(duration);
        playback.IsPlaying.ShouldBeFalse();
        playback.UpdateViewport(false, 0);
        playback.UpdateViewport(true, 0);
        frames.ReleaseDelayedRequest();

        await WaitForAsync(() => playback.PositionSeconds == duration.TotalSeconds);
        playback.IsPlaying.ShouldBeFalse();
        frames.DisplayedFrames.ShouldBe(1);
        frames.FrameRequests.ShouldBe(2);
    }

    [Theory]
    [InlineData(false, true, "#fff")]
    [InlineData(true, true, "#000")]
    [InlineData(true, false, "#000")]
    public async Task AnimatedLabelsContrastWithTheirThemeFill(bool dark, bool enableAnimation, string expectedForeground)
    {
        var renderer = new MermaiderSvgRenderer();
        var palette = dark ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light;
        var token = TestContext.Current.CancellationToken;

        foreach (var (source, label) in new[] { (AnimationSource, "GET"), (ChartAnimationSource, "75") })
        {
            var request = new MermaidSvgRenderRequest(source, palette, "Inter", 14);
            var result = await renderer.RenderDiagramAsync(request, enableAnimation, token);
            result.IsAnimationFrame.ShouldBeTrue();
            var svg = result.Animation is { } animation ? animation.RenderFrame(animation.ScoreDuration) : result.Svg;
            var frame = XDocument.Parse(MermaidSvgSanitizer.SanitizeMermaiderFrame(svg));
            var ns = frame.Root!.Name.Namespace;
            var text = frame.Descendants(ns + "text").Single(element => element.Value == label);
            text.Attribute("fill")!.Value.ShouldBe(expectedForeground);
            text.Parent!.Element(ns + "rect")!.Attribute("fill")!.Value.ShouldStartWith("var(--accent");
        }
    }

    [AvaloniaFact]
    public async Task ScrollingAnAnimatedChartReusesFramesWhenChangingDirection()
    {
        var frameLoads = 0;
        using var diagram = new MermaidDiagramControl(
            new MermaiderSvgRenderer(), ChartAnimationSource, MarkdownThemePalette.Light, "Inter", 14,
            svg =>
            {
                Interlocked.Increment(ref frameLoads);
                return SvgSource.LoadFromSvg(MermaidSvgSanitizer.SanitizeMermaiderSvg(svg));
            });
        var scroll = new ScrollContentPresenter
        {
            Height = 220,
            CanVerticallyScroll = true,
            ClipToBounds = true,
            Content = new StackPanel
            {
                Children = { new Border { Height = 300 }, diagram, new Border { Height = 480 } }
            }
        };
        var window = new Window { Width = 500, Height = 240, Content = scroll };
        window.Styles.Add(new FluentTheme());
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.GetVisualDescendants().OfType<Slider>().Any());
            window.UpdateLayout();
            var slider = diagram.GetVisualDescendants().OfType<Slider>().Single();
            slider.Value.ShouldBe(0);
            scroll.Offset = new Vector(0, 320);
            window.UpdateLayout();
            await WaitForAsync(() => slider.Value > 0);
            var position = slider.Value;
            scroll.Offset = new Vector(0, 430);
            window.UpdateLayout();
            await WaitForAsync(() => slider.Value > position);

            var loads = Volatile.Read(ref frameLoads);
            scroll.Offset = new Vector(0, 320);
            window.UpdateLayout();
            await WaitForAsync(() => slider.Value == position);
            Volatile.Read(ref frameLoads).ShouldBe(loads);

            scroll.Offset = default;
            window.UpdateLayout();
            scroll.Offset = new Vector(0, 430);
            window.UpdateLayout();
            await WaitForAsync(() => slider.Value > position);
            Volatile.Read(ref frameLoads).ShouldBe(loads + 1);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void GeneratedScaleGrowsTheRenderedBarAroundItsBaseline()
    {
        const string frame = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <rect x="40" y="60" width="20" height="20" fill="#00ff00"
                    style="opacity:1;transform:translate(0px,0px) scale(1,2);transform-box:view-box;transform-origin:50px 80px" />
            </svg>
            """;
        using var source = SvgSource.LoadFromSvg(MermaidSvgSanitizer.SanitizeMermaiderSvg(frame));
        using var pixels = new SkiaSharp.SKBitmap(100, 100);
        using var canvas = new SkiaSharp.SKCanvas(pixels);
        canvas.Clear(SkiaSharp.SKColors.Transparent);
        canvas.DrawPicture(source.Picture);
        canvas.Flush();

        pixels.GetPixel(50, 45).ShouldBe(SkiaSharp.SKColors.Lime);
        pixels.GetPixel(50, 75).ShouldBe(SkiaSharp.SKColors.Lime);
        pixels.GetPixel(50, 85).Alpha.ShouldBe((byte)0);
    }

    [AvaloniaFact]
    public async Task InvalidAnimationScoreShowsTheDiagnosticAndLeavesSourceAvailable()
    {
        var source = AnimationSource.Replace("fail B", "fail Unknown", StringComparison.Ordinal);
        using var diagram = new MermaidDiagramControl(
            new MermaiderSvgRenderer(), source, MarkdownThemePalette.Light, "Inter", 14);
        await WaitForAsync(() => diagram.HasError);
        diagram.HasImage.ShouldBeFalse();
        diagram.ErrorText.ShouldContain("line");
        diagram.ErrorText.ShouldContain("Unknown");
        diagram.SourceText.ShouldBe(source);
    }

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
    public async Task PlaybackErrorHandlerFailuresReachTheDispatcherExceptionBoundary()
    {
        var frameFailure = new InvalidOperationException("frame rendering failed");
        var handlerFailure = new InvalidOperationException("playback error handler failed");
        var observed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            if (!ReferenceEquals(args.Exception, handlerFailure))
                return;
            args.Handled = true;
            observed.TrySetResult(args.Exception);
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        try
        {
            var token = TestContext.Current.CancellationToken;
            var duration = TimeSpan.FromSeconds(2);
            using var playback = new MermaidPlayback(
                new MermaidAnimation((_, _) => throw frameFailure, duration, duration, [], Repeats: false),
                token,
                _ => throw new InvalidOperationException("A failed frame cannot be loaded."),
                _ => throw new InvalidOperationException("A failed frame cannot be presented."),
                exception =>
                {
                    exception.ShouldBeSameAs(frameFailure);
                    throw handlerFailure;
                });

            playback.UpdateViewport(visible: true, scrollProgress: 0);
            playback.PositionSeconds = 1;

            var exception = await observed.Task.WaitAsync(TimeSpan.FromSeconds(2), token);
            exception.ShouldBeSameAs(handlerFailure);
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

    [AvaloniaTheory]
    [MemberData(nameof(SupportedDiagrams))]
    public async Task MermaiderRendersEverySupportedDiagramGrammar(string name, string source)
    {
        var renderer = new MermaiderSvgRenderer();
        foreach (var palette in new[] { MarkdownThemePalette.Light, MarkdownThemePalette.Dark })
        {
            var svg = await renderer.RenderAsync(
                source,
                palette,
                "Inter",
                14,
                TestContext.Current.CancellationToken);

            svg.ShouldContain("<svg", customMessage: name);
            svg.ShouldContain("viewBox", customMessage: name);
            svg.ShouldNotContain("<script", Case.Insensitive, customMessage: name);
            svg.ShouldNotContain("<foreignObject", Case.Insensitive, customMessage: name);

            var sanitizedSvg = MermaidSvgSanitizer.SanitizeMermaiderSvg(svg);
            sanitizedSvg.ShouldNotContain("color-mix(", Case.Insensitive, customMessage: name);
            using var svgSource = SvgSource.LoadFromSvg(sanitizedSvg);
            var image = new SvgImage { Source = svgSource };
            image.Size.Width.ShouldBeGreaterThan(0, customMessage: name);
            image.Size.Height.ShouldBeGreaterThan(0, customMessage: name);
        }
    }

    [Fact]
    public async Task StateDiagramTransitionLabelsDoNotOverlapStatesOrPseudostates()
    {
        const string source = """
            stateDiagram-v2
                [*] --> Idle
                Idle --> Editing: Type markdown
                Editing --> Previewing: Rebuild preview
                Previewing --> Idle: Await next change
                Previewing --> [*]: Close sample
            """;
        var renderer = new MermaiderSvgRenderer();
        var svg = await renderer.RenderAsync(source, MarkdownThemePalette.Light, "Inter", 14, CancellationToken.None);
        var document = XDocument.Parse(svg);
        var ns = document.Root!.Name.Namespace;
        var labels = document.Descendants(ns + "g")
            .Where(group => (string?)group.Attribute("class") == "edge-label")
            .ToArray();
        var states = document.Descendants(ns + "g")
            .Where(group => (string?)group.Attribute("class") == "node")
            .SelectMany(group => group.Elements().Where(shape => shape.Name.LocalName is "rect" or "circle"))
            .ToArray();
        labels.Length.ShouldBe(4);
        states.ShouldNotBeEmpty();

        foreach (var label in labels)
        {
            var labelBounds = Bounds(label.Element(ns + "rect").ShouldNotBeNull());
            foreach (var state in states)
            {
                labelBounds.Intersects(Bounds(state)).ShouldBeFalse(
                    $"Transition '{(string?)label.Attribute("data-label")}' overlaps state '{(string?)state.Parent!.Attribute("data-id")}'.");
            }
        }

        static Rect Bounds(XElement shape)
        {
            if (shape.Name.LocalName == "circle")
            {
                var radius = (double)shape.Attribute("r")!;
                return new Rect((double)shape.Attribute("cx")! - radius, (double)shape.Attribute("cy")! - radius,
                    radius * 2, radius * 2);
            }

            return new Rect((double)shape.Attribute("x")!, (double)shape.Attribute("y")!,
                (double)shape.Attribute("width")!, (double)shape.Attribute("height")!);
        }
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

    public enum AbandonedRenderOutcome
    {
        OperationCanceled,
        TaskCanceled,
        Failure
    }

    [Theory]
    [InlineData(AbandonedRenderOutcome.OperationCanceled, false)]
    [InlineData(AbandonedRenderOutcome.TaskCanceled, false)]
    [InlineData(AbandonedRenderOutcome.Failure, false)]
    [InlineData(AbandonedRenderOutcome.OperationCanceled, true)]
    [InlineData(AbandonedRenderOutcome.TaskCanceled, true)]
    [InlineData(AbandonedRenderOutcome.Failure, true)]
    public async Task AbandonedRenderCancellationIsQuietButFailuresRemainLogged(
        AbandonedRenderOutcome outcome,
        bool useTimeout)
    {
        const string diagnosticMessage = "Abandoned render diagnostic regression";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Stream? abandonedDestination = null;
        var renderer = new MermaiderSvgRenderer(async (source, destination, _, cancellationToken) =>
        {
            if (source == "abandoned")
            {
                abandonedDestination = destination;
                entered.TrySetResult();
                await release.Task.WaitAsync(TestContext.Current.CancellationToken);
                throw outcome switch
                {
                    AbandonedRenderOutcome.OperationCanceled => new OperationCanceledException(diagnosticMessage, cancellationToken),
                    AbandonedRenderOutcome.TaskCanceled => new TaskCanceledException(diagnosticMessage, null, cancellationToken),
                    _ => new InvalidOperationException(diagnosticMessage)
                };
            }

            await WriteMinimalSvgAsync(destination, cancellationToken);
        }, useTimeout ? TimeSpan.FromMilliseconds(150) : null);
        using var cancellation = new CancellationTokenSource();
        using var traceOutput = new StringWriter();
        using var listener = new TextWriterTraceListener(traceOutput);
        Trace.Listeners.Add(listener);
        try
        {
            var render = renderer.RenderAsync("abandoned", MarkdownThemePalette.Light, "Inter", 14, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            if (useTimeout)
                await Should.ThrowAsync<TimeoutException>(() => render);
            else
            {
                cancellation.Cancel();
                await Should.ThrowAsync<OperationCanceledException>(() => render);
            }

            release.TrySetResult();
            var svg = await renderer.RenderAsync("next", MarkdownThemePalette.Light, "Inter", 14,
                TestContext.Current.CancellationToken);
            svg.ShouldContain("<svg");
            abandonedDestination.ShouldNotBeNull().CanWrite.ShouldBeFalse();
            listener.Flush();
            if (outcome == AbandonedRenderOutcome.Failure)
                traceOutput.ToString().ShouldContain(diagnosticMessage);
            else
                traceOutput.ToString().ShouldNotContain(diagnosticMessage);
        }
        finally
        {
            release.TrySetResult();
            Trace.Listeners.Remove(listener);
        }
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
            await operation.Disposed.Task.WaitAsync(
                TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
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
    public async Task DiagramKeepsNaturalSizeAndOnlyScalesDownWhenItsHostNarrows()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"320\" height=\"160\" viewBox=\"0 0 320 160\"><rect width=\"320\" height=\"160\" /></svg>";
        var renderer = new MermaiderSvgRenderer(async (_, destination, _, cancellationToken) =>
            await destination.WriteAsync(Encoding.UTF8.GetBytes(svg), cancellationToken));
        using var diagram = new MermaidDiagramControl(renderer, "flowchart TD\nA --> B", MarkdownThemePalette.Light, "Inter", 14);
        var window = new Window { Width = 800, Height = 600, Content = new StackPanel { Children = { diagram } } };
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.HasImage);
            window.UpdateLayout();
            diagram.DesiredSize.ShouldBe(new Size(320, 160));
            diagram.Bounds.Height.ShouldBe(160);
            var invocationCount = renderer.RenderInvocationCount;

            window.Width = 160;
            await WaitForAsync(() =>
            {
                window.UpdateLayout();
                return Math.Abs(diagram.Bounds.Height - 80) < 0.01;
            });
            diagram.DesiredSize.ShouldBe(new Size(160, 80));
            diagram.Bounds.Height.ShouldBe(80);

            window.Width = 800;
            await WaitForAsync(() =>
            {
                window.UpdateLayout();
                return Math.Abs(diagram.Bounds.Height - 160) < 0.01;
            });
            diagram.DesiredSize.ShouldBe(new Size(320, 160));
            diagram.Bounds.Height.ShouldBe(160);
            renderer.RenderInvocationCount.ShouldBe(invocationCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TallDiagramFitsItsAvailableHeightWithoutUpscaling()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"160\" height=\"320\" viewBox=\"0 0 160 320\"><rect width=\"160\" height=\"320\" /></svg>";
        using var diagram = new MermaidDiagramControl(new StaticSvgRenderer(svg), "stateDiagram-v2", MarkdownThemePalette.Light,
            "Inter", 14, new MermaidMarkdownPluginOptions(), CancellationToken.None, static () => new CompletionTrackingDisposable());
        var window = new Window { Width = 800, Height = 160, Content = diagram };
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.HasImage);
            window.UpdateLayout();

            diagram.DesiredSize.ShouldBe(new Size(80, 160));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NaturalSizeDiagramLinksIgnoreTheEmptySpaceAroundTheImage()
    {
        var activationCount = 0;
        using var diagram = new MermaidDiagramControl(
            new StaticSvgRenderer(LinkedSvg), "flowchart TD\nA --> B", MarkdownThemePalette.Light, "Inter", 14,
            new MermaidMarkdownPluginOptions
            {
                ActivateLinkAsync = (_, _) =>
                {
                    Interlocked.Increment(ref activationCount);
                    return Task.CompletedTask;
                }
            }, CancellationToken.None, static () => new CompletionTrackingDisposable());
        var window = new Window { Width = 400, Height = 240, Content = diagram };
        window.Show();
        try
        {
            await WaitForAsync(() => diagram.HasImage);
            window.UpdateLayout();

            window.MouseDown(new Point(40, 120), MouseButton.Left);
            window.MouseUp(new Point(40, 120), MouseButton.Left);
            activationCount.ShouldBe(0);

            window.MouseDown(new Point(200, 120), MouseButton.Left);
            window.MouseUp(new Point(200, 120), MouseButton.Left);
            await WaitForAsync(() => activationCount == 1);
        }
        finally
        {
            window.Close();
        }
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDiagramShowsSelectableSourceAndCanBeRetried(bool failBeforeAttachment)
    {
        var releaseFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = 0;
        var renderer = new MermaiderSvgRenderer(async (_, destination, _, cancellationToken) =>
        {
            if (Interlocked.Increment(ref invocation) == 1)
            {
                await releaseFailure.Task.WaitAsync(cancellationToken);
                throw new InvalidOperationException("Invalid diagram syntax.");
            }

            await WriteMinimalSvgAsync(destination, cancellationToken);
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
        try
        {
            if (failBeforeAttachment)
            {
                releaseFailure.SetResult();
                await WaitForAsync(() => diagram.HasError);
            }

            window.Show();
            var retry = diagram.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, "Retry"));
            if (!failBeforeAttachment)
            {
                diagram.IsLoading.ShouldBeTrue();
                diagram.HasError.ShouldBeFalse();
                retry.IsEffectivelyVisible.ShouldBeFalse();
                releaseFailure.SetResult();
            }

            // IsVisible alone ignores the hidden parent error panel during the initial render.
            await WaitForAsync(() => retry.IsEffectivelyVisible);
            diagram.HasError.ShouldBeTrue();
            diagram.IsLoading.ShouldBeFalse();
            invocation.ShouldBe(1);
            var sourceText = diagram.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Single(text => text.Text == source);
            sourceText.Text.ShouldBe(source);
            sourceText.IsEffectivelyVisible.ShouldBeTrue();
            diagram.SourceFontSize.ShouldBe(12);
            sourceText.FontSize.ShouldBe(diagram.SourceFontSize);

            retry.Command.ShouldBeSameAs(diagram.RetryCommand);
            retry.Command!.Execute(retry.CommandParameter);

            await WaitForAsync(() => !diagram.IsLoading);
            diagram.HasError.ShouldBeFalse(diagram.ErrorText);
            diagram.HasImage.ShouldBeTrue();
            diagram.GetVisualDescendants().OfType<Image>()
                .ShouldContain(image => image.IsEffectivelyVisible);
            invocation.ShouldBe(2);
        }
        finally
        {
            releaseFailure.TrySetResult();
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
        await WaitForAsync(() => !markdown.IsRendering);
        var window = new Window { Width = 500, Height = 300, Content = markdown };
        window.Show();
        try
        {
            await WaitForAsync(() => !markdown.IsRendering);
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

    [AvaloniaTheory]
    [InlineData("```mermaid\nflowchart TD\nA --> B\n```")]
    [InlineData("```mmd\nflowchart TD\nA --> B\n```")]
    [InlineData(":::mermaid\nflowchart TD\nA --> B\n:::")]
    [InlineData(":::diagram mermaid\nflowchart TD\nA --> B\n:::")]
    public async Task ExistingFenceAndContainerSyntaxProducesNativeDiagramControls(string markdown)
    {
        var control = CreateMermaidMarkdown(markdown, new MermaiderSvgRenderer(
            (_, destination, _, cancellationToken) => WriteMinimalSvgAsync(destination, cancellationToken)));

        try
        {
            // The next headless test can reset the dispatcher as soon as this test returns.
            await WaitForAsync(() => !control.IsRendering);
            EnumerateMermaidControls(control.Inlines!).ShouldHaveSingleItem().HasImage.ShouldBeTrue();
        }
        finally
        {
            control.Markdown = string.Empty;
        }
    }

    [AvaloniaFact]
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
        await WaitForAsync(() => !control.IsRendering);
        var window = new Window { Width = 500, Height = 300, Content = control };
        window.Show();
        try
        {
            await WaitForAsync(() => !control.IsRendering);
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

    private sealed class DelayedPlaybackFrame : IDisposable
    {
        private const string Svg =
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20"><rect width="20" height="20" /></svg>""";

        private readonly TaskCompletionSource<TimeSpan> _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<TimeSpan> _secondRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _token = TestContext.Current.CancellationToken;
        private readonly int _delayedRequest;
        private SvgSource? _currentFrame;
        private int _frameRequests;

        public DelayedPlaybackFrame(TimeSpan duration, bool scrubsOnScroll = false,
            IReadOnlyList<Mermaider.Animate.AnimationStep>? steps = null, int delayedRequest = 1, bool repeats = false)
        {
            _delayedRequest = delayedRequest;
            var animation = new MermaidAnimation(CreateFrame, duration, duration, steps ?? [],
                Repeats: repeats, ScrubsOnScroll: scrubsOnScroll);
            Playback = new MermaidPlayback(animation, _token, text => SvgSource.LoadFromSvg(text), ReplaceFrame,
                exception => throw new InvalidOperationException("A test frame failed to render.", exception));
        }

        public MermaidPlayback Playback { get; }
        public int DisplayedFrames { get; private set; }
        public int FrameRequests => Volatile.Read(ref _frameRequests);

        public Task<TimeSpan> WaitForFirstRequestAsync() => _firstRequest.Task.WaitAsync(TimeSpan.FromSeconds(5), _token);

        public Task<TimeSpan> WaitForSecondRequestAsync() => _secondRequest.Task.WaitAsync(TimeSpan.FromSeconds(5), _token);

        public void ReleaseDelayedRequest() => _release.TrySetResult();

        public void Dispose()
        {
            Playback.Dispose();
            ReleaseDelayedRequest();
            _currentFrame?.Dispose();
        }

        private string CreateFrame(TimeSpan time, Int128 elapsedTicks)
        {
            var request = Interlocked.Increment(ref _frameRequests);
            if (request == 1)
                _firstRequest.SetResult(time);
            else if (request == 2)
                _secondRequest.SetResult(time);
            if (request == _delayedRequest)
                _release.Task.Wait(_token);
            return Svg;
        }

        private SvgSource? ReplaceFrame(SvgSource image)
        {
            DisplayedFrames++;
            var previous = _currentFrame;
            _currentFrame = image;
            return previous;
        }
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
