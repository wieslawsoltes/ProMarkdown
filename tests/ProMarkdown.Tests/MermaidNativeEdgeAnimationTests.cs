using System.Xml.Linq;
using ProMarkdown.Plugin.Mermaid;
using ProMarkdown.Services;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class MermaidNativeEdgeAnimationTests
{
    private const string NativeFlow = """
        ---
        config:
          flowchart:
            stepDuration: 0.5
            stepNumbers: true
        ---
        flowchart LR
            A["Client e9@--> literal"] request@-->|Query| B[Service] fetch@--> C[(Database)]
            A alternate@--> D[Cache]
            C reply@-.-> B
            request@{ step: 1, animate: true }; fetch@{ step: 2 }
            alternate@{ step: 1 }
            reply@{
              animation: 'fast'
            }
        """;

    [Fact]
    public async Task NativeEdgesMoveInOrderWithParallelStepsAndAnimatedDashes()
    {
        var result = await RenderAsync(NativeFlow);
        var animation = result.Animation.ShouldNotBeNull();
        animation.Repeats.ShouldBeTrue();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(1));
        animation.Steps.Select(step => step.Start.TotalSeconds).ShouldBe([0, 0.5]);

        var first = XElement.Parse(result.Svg);
        var later = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(0.17)));
        var secondStep = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(0.75)));
        var firstDots = VisibleDots(first).ToArray();
        firstDots.Length.ShouldBe(2);
        VisibleDots(secondStep).Count().ShouldBe(1);
        firstDots[0].Attribute("cx")!.Value.ShouldNotBe(VisibleDots(later).First().Attribute("cx")!.Value);
        Edge(later, "reply").Attribute("stroke-dashoffset")!.Value.ShouldNotBe(Edge(first, "reply").Attribute("stroke-dashoffset")!.Value);
        Edge(first, "request").Attribute("data-label")!.Value.ShouldBe("Query");
        Edge(first, "fetch").Attribute("data-from")!.Value.ShouldBe("B");
        first.Value.ShouldContain("Client e9@--> literal");
        WithClass(first, "edge-step-badge").Count().ShouldBe(3);
    }

    [Fact]
    public async Task NativeDashSpeedsKeepTheirOwnPeriodsAcrossShortSteps()
    {
        var result = await RenderAsync("""
            ---
            config:
              flowchart:
                stepDuration: 0.1
            ---
            flowchart LR
                A fastEdge@--> B
                A slowEdge@--> C
                fastEdge@{ step: 1, animation: fast }
                slowEdge@{ step: 1, animation: slow }
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromMilliseconds(100));
        var time = TimeSpan.FromMilliseconds(30);
        var first = XElement.Parse(animation.RenderFrame(time));
        Edge(first, "fastEdge").Attribute("stroke-dashoffset")!.Value.ShouldBe("898.65");
        Edge(first, "slowEdge").Attribute("stroke-dashoffset")!.Value.ShouldBe("899.46");

        var nextLoop = XElement.Parse(animation.CreateFrame(time, (animation.ScoreDuration + time).Ticks));
        Edge(nextLoop, "fastEdge").Attribute("stroke-dashoffset")!.Value.ShouldBe("894.15");
        Edge(nextLoop, "slowEdge").Attribute("stroke-dashoffset")!.Value.ShouldBe("897.66");
    }

    [Fact]
    public async Task AnimationDisabledRendersStaticDiagramAndConfiguredStepNumbers()
    {
        var result = await RenderAsync(NativeFlow, enableAnimation: false);
        result.Animation.ShouldBeNull();
        var svg = XElement.Parse(result.Svg);
        WithClass(svg, "edge-step-dot").ShouldBeEmpty();
        WithClass(svg, "edge-step-badge").Count().ShouldBe(3);
        WithClass(svg, "edge").ShouldAllBe(edge => edge.Attribute("stroke-dashoffset") == null);
    }

    [Theory]
    [InlineData("config: # Diagram options\n  flowchart:\n    stepDuration: 0.25\n    stepNumbers: true")]
    [InlineData("config:\n  flowchart: # Animation options\n    stepDuration: 0.25\n    stepNumbers: true")]
    [InlineData("config: { flowchart: { stepDuration: 0.25, stepNumbers: true } }")]
    [InlineData("config:\n  flowchart: { stepDuration: 0.25, stepNumbers: true }")]
    [InlineData("settings: &settings { flowchart: { stepDuration: 0.25, stepNumbers: true } }\nconfig: *settings")]
    [InlineData("playback: &playback { stepDuration: 0.25, stepNumbers: true }\nconfig:\n  flowchart: *playback")]
    [InlineData("seconds: &seconds 0.25\nbadges: &badges true\nconfig: { flowchart: { stepDuration: *seconds, stepNumbers: *badges } }")]
    [InlineData("{ seconds: &seconds 0.25, config: { flowchart: { stepDuration: *seconds, stepNumbers: true } } }")]
    [InlineData("seconds: &seconds 0.5\nconfig:\n  fallback: &seconds 0.25\n  flowchart: { stepDuration: *seconds, stepNumbers: true }\nlater: &seconds 0.75")]
    public async Task NativeEdgeSettingsAcceptYamlCommentsInlineMappingsAndAliases(string frontMatter)
    {
        var result = await RenderAsync($"---\n{frontMatter}\n---\nflowchart LR\nA e1@--> B\ne1@{{ step: 1 }}");

        result.Animation.ShouldNotBeNull().ScoreDuration.ShouldBe(TimeSpan.FromMilliseconds(250));
        WithClass(XElement.Parse(result.Svg), "edge-step-badge").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task NativeEdgeSettingsIgnoreSettingsNestedOutsideTheFlowchartMapping()
    {
        var result = await RenderAsync("""
            ---
            config:
              flowchart:
                unrelated: { stepDuration: 0.25, stepNumbers: true }
            other: { flowchart: { stepDuration: 0.25, stepNumbers: true } }
            ---
            flowchart LR
                A e1@--> B
                e1@{ step: 1 }
            """);

        result.Animation.ShouldNotBeNull().ScoreDuration.ShouldBe(TimeSpan.FromSeconds(1));
        WithClass(XElement.Parse(result.Svg), "edge-step-badge").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\t", "\n")]
    [InlineData(" \t", "\r\n")]
    public async Task NativeEdgeSettingsDedentByTheOpeningFence(string indent, string newline)
    {
        var source = $$"""
            {{indent}}---
            {{indent}}config:
            {{indent}}  flowchart:
            {{indent}}    stepDuration: 0.25
            {{indent}}    stepNumbers: true
            {{indent}}---
            flowchart LR
                A e1@--> B
                e1@{ step: 1 }
            """.ReplaceLineEndings(newline);

        var result = await RenderAsync(source);

        result.Animation.ShouldNotBeNull().ScoreDuration.ShouldBe(TimeSpan.FromMilliseconds(250));
        WithClass(XElement.Parse(result.Svg), "edge-step-badge").ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("", "A e1@--> B\ne1@{ step: 1 }", true)]
    [InlineData("  ", "A e1@--> B\ne1@{ step: 1 }", true)]
    [InlineData("\t", "A e1@--> B\ne1@{ step: 1 }", true)]
    [InlineData("", "A --> B", false)]
    [InlineData("", "A --> B\n\nanimate once\n  send A -> B \"Hello\"", true)]
    public async Task DelimiterInMultilineTitleDoesNotCloseFrontMatter(string indent, string body, bool animated)
    {
        var result = await RenderAsync($$"""
            {{indent}}---
            {{indent}}title: |
            {{indent}}  Orders
            {{indent}}  ---
            {{indent}}  Service flow
            {{indent}}config: { flowchart: { stepDuration: 0.25, stepNumbers: true } }
            {{indent}}---
            flowchart LR
            {{body}}
            """);

        (result.Animation is not null).ShouldBe(animated);
        var svg = XElement.Parse(result.Svg);
        svg.Value.ShouldContain("Orders --- Service flow");
        if (body.Contains("e1@", StringComparison.Ordinal))
        {
            result.Animation.ShouldNotBeNull().ScoreDuration.ShouldBe(TimeSpan.FromMilliseconds(250));
            WithClass(svg, "edge-step-badge").ShouldHaveSingleItem();
        }
    }

    [Theory]
    [InlineData("!!str true", "!!str 0.25", false, 1)]
    [InlineData("!!bool \"true\"", "!!float \"0.25\"", true, 0.25)]
    [InlineData("!!bool \"false\"", "!!int \"2\"", false, 2)]
    [InlineData("TrUe", "\"0.25\"", false, 1)]
    [InlineData("true", "0xA", true, 10)]
    [InlineData("true", "!!int \"0o2\"", true, 2)]
    [InlineData("true", "+0b10", true, 2)]
    [InlineData("true", "0o8", true, 1)]
    [InlineData("true", "!!str 0x2", true, 1)]
    public async Task NativeEdgeSettingsRespectYamlScalarTypes(string numbers, string seconds, bool showsBadge, double duration)
    {
        var result = await RenderAsync($$"""
            ---
            badges: &badges {{numbers}}
            config: { flowchart: { stepDuration: {{seconds}}, stepNumbers: *badges } }
            ---
            flowchart LR
                A e1@--> B
                e1@{ step: 1 }
            """);

        result.Animation.ShouldNotBeNull().ScoreDuration.ShouldBe(TimeSpan.FromSeconds(duration));
        WithClass(XElement.Parse(result.Svg), "edge-step-badge").Any().ShouldBe(showsBadge);
    }

    [Theory]
    [InlineData("1e-8", 1)]
    [InlineData("1e12", 1)]
    [InlineData("5e11", 2)]
    public async Task NativeStepsUseTheDefaultDurationWhenTheTimelineCannotBeRepresented(string seconds, int step)
    {
        var result = await RenderAsync($$"""
            ---
            config: { flowchart: { stepDuration: {{seconds}}, stepNumbers: true } }
            ---
            flowchart LR
                A e1@--> B
                e1@{ step: {{step}} }
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(step));
        animation.Steps.Single().Start.ShouldBe(TimeSpan.FromSeconds(step - 1));
        WithClass(XElement.Parse(result.Svg), "edge-step-badge").ShouldHaveSingleItem();
        var activeFrame = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(step - 0.5)));
        VisibleDots(activeFrame).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("\"step\": 2", 2)]
    [InlineData("step: 0x2", 2)]
    [InlineData("step: !!int \"0o2\"", 2)]
    [InlineData("hint: &phase 2, step: *phase", 2)]
    [InlineData("hint: { first: &phase !!int 0x2, second: 3 }, step: *phase", 2)]
    [InlineData("\n  hint: &phase 0b10\n  step: *phase\n", 2)]
    public async Task NativeEdgeStepsUseYamlScalarRules(string properties, int step)
    {
        var result = await RenderAsync($"flowchart LR\nA e1@--> B\ne1@{{{properties}}}");

        var animation = result.Animation.ShouldNotBeNull();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(step));
        animation.Steps.Single().Start.ShouldBe(TimeSpan.FromSeconds(step - 1));
    }

    [Theory]
    [InlineData("animate: true", 20)]
    [InlineData("animate: !!bool \"true\"", 20)]
    [InlineData("animate: true, animation: slow", 50)]
    [InlineData("speed: &speed \"fast\", animation: *speed", 20)]
    [InlineData("\"animation\": !!str fast", 20)]
    public async Task NativeDashPropertiesUseYamlScalarRules(string properties, int seconds)
    {
        var result = await RenderAsync($"flowchart LR\nA e1@--> B\ne1@{{{properties}}}");

        result.Animation.ShouldNotBeNull().ScoreDuration.ShouldBe(TimeSpan.FromSeconds(seconds));
        Edge(XElement.Parse(result.Svg), "e1").Attribute("stroke-dasharray").ShouldNotBeNull();
    }

    [Theory]
    [InlineData("animation: null")]
    [InlineData("animation: ~")]
    [InlineData("animation: !!null \"null\"")]
    [InlineData("animation:")]
    [InlineData("reset: &reset null, animation: *reset")]
    public async Task YamlNullClearsAnEarlierNativeDashSpeed(string properties)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                A e1@--> B
                e1@{ animation: slow }
                e1@{ {{properties}} }
            """);

        result.Animation.ShouldBeNull();
        WithClass(XElement.Parse(result.Svg), "edge").Single().Attribute("stroke-dashoffset").ShouldBeNull();
    }

    [Theory]
    [InlineData("animation: null", 20)]
    [InlineData("step: null", 50)]
    [InlineData("animation: \"null\"", 50)]
    public async Task NativeDashUpdatesKeepIndependentProperties(string properties, int seconds)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                A e1@--> B
                e1@{ animate: true, animation: slow }
                e1@{ {{properties}} }
            """);

        result.Animation.ShouldNotBeNull().ScoreDuration.ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1.5")]
    [InlineData("\"2\"")]
    [InlineData("!!str 2")]
    public async Task InvalidStepUpdateDoesNotClearAnEarlierStep(string invalidStep)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                A e1@--> B
                e1@{ step: 1 }
                e1@{ step: {{invalidStep}} }
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.Steps.Single().Start.ShouldBe(TimeSpan.Zero);
        VisibleDots(XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(0.5)))).Count().ShouldBe(1);
    }

    [Fact]
    public async Task NamedDashesOnlyAffectTheFirstGroupedLinkAndTheSpecifiedParallelLink()
    {
        var result = await RenderAsync("""
            graph LR
                A ordinary@--> B
                A selected@--> B
                A & B branch@--> C & D
                C branch@--> D
                selected@{ animation: fast }
                branch@{ animation: slow }
                ordinary@{ animate: false }
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.Steps.ShouldBeEmpty();
        var svg = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(0.17)));
        var edges = WithClass(svg, "edge").ToArray();
        edges.Count(edge => edge.Attribute("stroke-dashoffset") is not null).ShouldBe(2);
        Edge(svg, "branch").Attribute("data-from")!.Value.ShouldBe("B");
        Edge(svg, "branch").Attribute("data-to")!.Value.ShouldBe("C");
        var parallel = edges.Where(edge => (string?)edge.Attribute("data-from") == "A" &&
            (string?)edge.Attribute("data-to") == "B").ToArray();
        parallel.Length.ShouldBe(2);
        parallel[0].Attribute("stroke-dashoffset").ShouldBeNull();
        parallel[1].ShouldBeSameAs(Edge(svg, "selected"));
        Edge(svg, "selected").Attribute("stroke-dashoffset")!.Value
            .ShouldNotBe(Edge(svg, "branch").Attribute("stroke-dashoffset")!.Value);
    }

    [Fact]
    public async Task NamedEdgesWithoutAnimationRenderNormally()
    {
        var result = await RenderAsync("flowchart LR\nA edge@--> B\nedge@{ animate: false }");
        result.Animation.ShouldBeNull();
        WithClass(XElement.Parse(result.Svg), "edge").Count().ShouldBe(1);
    }

    [Theory]
    [InlineData("-- Yes ---->", "solid")]
    [InlineData("== Yes ====>", "thick")]
    [InlineData("-. Yes ...->", "dotted")]
    [InlineData("--Yes---->", "solid")]
    [InlineData("--\"Yes\"---->", "solid")]
    [InlineData("==Yes====>", "thick")]
    [InlineData("-.Yes...->", "dotted")]
    public async Task NativeLabeledEdgesAcceptExtendedArrows(string arrow, string style)
    {
        var result = await RenderAsync($"flowchart LR\nA e1@{arrow}B\ne1@{{ animate: true }}");

        result.Animation.ShouldNotBeNull();
        var edge = Edge(XElement.Parse(result.Svg), "e1");
        (edge.Attribute("data-style")?.Value ?? "solid").ShouldBe(style);
        edge.Attribute("data-label")!.Value.ShouldBe("Yes");
        edge.Attribute("data-arrow-end")!.Value.ShouldBe("true");
    }

    [Theory]
    [InlineData("-- Say \"hello\" now -->", "Say \"hello\" now")]
    [InlineData("--\"literal -->; [|\"-->", "literal -->; [|")]
    [InlineData("--Say \"hello\" | again-->", "Say \"hello\" | again")]
    [InlineData("--\"hello\" | again-->", "\"hello\" | again")]
    [InlineData("-- Issue #35;42 -->", "Issue #42")]
    [InlineData("== Quote #quot;one#quot; ===>", "Quote \"one\"")]
    [InlineData("-. Issue #42 .->", "Issue #42")]
    public async Task NamedInlineLabelsPreserveAuthoredPunctuation(string arrow, string label)
    {
        var result = await RenderAsync($"flowchart LR\nA e1@{arrow}B\ne1@{{ animate: true }}");

        result.Animation.ShouldNotBeNull();
        Edge(XElement.Parse(result.Svg), "e1").Attribute("data-label")!.Value.ShouldBe(label);
    }

    [Theory]
    [InlineData("-- first|second -->", "first|second", "\n")]
    [InlineData("== [urgent ===>", "[urgent", "; ")]
    [InlineData("-. {urgent .->", "{urgent", "\r\n")]
    [InlineData("-- Issue #35;42 -->", "Issue #42", "; ")]
    [InlineData("-->|\"[urgent; retry\"|", "[urgent; retry", "; ")]
    public async Task UnnamedInlineLabelsPreserveFollowingAnimatedEdges(string arrow, string label, string separator)
    {
        var source = $"flowchart LR\nA {arrow} B{separator}B e1@--> C\ne1@{{ animate: true }}";
        var result = await RenderAsync(source);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var edges = WithClass(svg, "edge").ToArray();
        edges.Length.ShouldBe(2);
        edges.Single(edge => (string?)edge.Attribute("data-from") == "A")
            .Attribute("data-label")!.Value.ShouldBe(label);
        Edge(svg, "e1").Attribute("data-from")!.Value.ShouldBe("B");
    }

    [Theory]
    [InlineData("-->|Ready|", "Ready")]
    [InlineData("-->", null)]
    public async Task NamedEdgesRenderWhenFollowedByAnotherStatementOnTheSameLine(string arrow, string? label)
    {
        var result = await RenderAsync($"flowchart LR\nA e1@{arrow} B; B --> C\ne1@{{ animate: true }}");

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        WithClass(svg, "edge").Count().ShouldBe(2);
        ((string?)Edge(svg, "e1").Attribute("data-label")).ShouldBe(label);
    }

    [Fact]
    public async Task NativeEdgesRenderAfterSameLineEdgeMetadata()
    {
        var result = await RenderAsync("""
            flowchart LR
                A e1@-->|"Ready; retry"| B; e1@{ animate: true }; B e2@--> C; e2@{ step: 1 }
            """);

        result.Animation.ShouldNotBeNull().Steps.Count.ShouldBe(1);
        var svg = XElement.Parse(result.Svg);
        WithClass(svg, "edge").Count().ShouldBe(2);
        Edge(svg, "e1").Attribute("data-label")!.Value.ShouldBe("Ready; retry");
        Edge(svg, "e2").Attribute("data-to")!.Value.ShouldBe("C");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("; ")]
    public async Task NativeEdgesRenderAfterSameLineNodeAndClassDeclarations(string headerSeparator)
    {
        var result = await RenderAsync($$"""
            flowchart LR{{headerSeparator}}A["Client; priority"]; A e1@--> B; classDef active fill:#f00; B --> C
            e1@{ animate: true }
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        WithClass(svg, "edge").Count().ShouldBe(2);
        Edge(svg, "e1").Attribute("data-from")!.Value.ShouldBe("A");
        svg.Descendants().ShouldContain(element => (string?)element.Attribute("data-label") == "Client; priority");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeFlowchartPreservesUnnamedArrowsAlongsideNamedEdges(bool enableAnimation)
    {
        var result = await RenderAsync("""
            flowchart LR
                A["Literal --No---->C"] e1@--o B--xC
                C -- No ----> D
                D--"Also no"---->E
                E -->|Literal --o| F
                e1@{ animate: true }
            """, enableAnimation);

        (result.Animation is not null).ShouldBe(enableAnimation);
        var svg = XElement.Parse(result.Svg);
        WithClass(svg, "edge-marker-circle").Count().ShouldBe(1);
        WithClass(svg, "edge-marker-cross").Count().ShouldBe(1);
        var edges = WithClass(svg, "edge").ToArray();
        edges.Length.ShouldBe(5);
        edges.Single(edge => (string?)edge.Attribute("data-from") == "B")
            .Attribute("data-arrow-end")!.Value.ShouldBe("false");
        edges.Single(edge => (string?)edge.Attribute("data-from") == "C")
            .Attribute("data-label")!.Value.ShouldBe("No");
        edges.Single(edge => (string?)edge.Attribute("data-from") == "D")
            .Attribute("data-label")!.Value.ShouldBe("Also no");
        edges.Single(edge => (string?)edge.Attribute("data-from") == "E")
            .Attribute("data-label")!.Value.ShouldBe("Literal --o");
        edges.Count(edge => edge.Attribute("stroke-dashoffset") is not null).ShouldBe(enableAnimation ? 1 : 0);
    }

    [Theory]
    [InlineData("--o", 1, 0, true)]
    [InlineData("--x", 0, 1, true)]
    [InlineData("o--x", 1, 1, false)]
    [InlineData("x--o", 1, 1, false)]
    public async Task NamedEdgesPreserveCustomEndingsWithAndWithoutPlayback(
        string arrow, int circles, int crosses, bool enableAnimation)
    {
        var result = await RenderAsync($"flowchart LR\nA e1@{arrow} B\ne1@{{ animate: true }}", enableAnimation);

        (result.Animation is not null).ShouldBe(enableAnimation);
        var svg = XElement.Parse(result.Svg);
        var edge = Edge(svg, "e1");
        edge.Attribute("data-arrow-end")!.Value.ShouldBe("false");
        WithClass(svg, "edge-marker-circle").Count().ShouldBe(circles);
        WithClass(svg, "edge-marker-cross").Count().ShouldBe(crosses);
    }

    [Fact]
    public async Task AllExpandedGroupEdgesKeepCustomEndingsButOnlyTheNamedLinkAnimates()
    {
        var result = await RenderAsync("flowchart LR\nA & B e1@--o C & D\ne1@{ animate: true }");

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        WithClass(svg, "edge-marker-circle").Count().ShouldBe(4);
        WithClass(svg, "edge").Count(edge => edge.Attribute("stroke-dashoffset") is not null).ShouldBe(1);
        var named = Edge(svg, "e1");
        named.Attribute("data-from")!.Value.ShouldBe("B");
        named.Attribute("data-to")!.Value.ShouldBe("C");
    }

    [Fact]
    public async Task NativeEdgeAnimationCanStartAtANodeNamedAnimate()
    {
        var result = await RenderAsync("flowchart LR\nanimate e1@--> B\ne1@{ animate: true }");

        result.Animation.ShouldNotBeNull();
        Edge(XElement.Parse(result.Svg), "e1").Attribute("data-from")!.Value.ShouldBe("animate");
    }

    [Fact]
    public async Task NativeEdgePropertiesAcceptADottedEdgeId()
    {
        var result = await RenderAsync("""
            flowchart LR
                animate api.v1@--> B
                api.v1@{ animation: fast, step: 1 }
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.Steps.Count.ShouldBe(1);
        var svg = XElement.Parse(result.Svg);
        Edge(svg, "api.v1").Attribute("data-from")!.Value.ShouldBe("animate");
    }

    [Theory]
    [InlineData("e1")]
    [InlineData("api.v1")]
    public async Task NativePropertiesBeforeAnEdgeLeaveTheEdgeStatic(string id)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                {{id}}@{ animate: true }
                A {{id}}@--> B
            """);

        result.Animation.ShouldBeNull();
        var svg = XElement.Parse(result.Svg);
        WithClass(svg, "edge").Single().Attribute("stroke-dashoffset").ShouldBeNull();
        svg.Descendants(svg.Name.Namespace + "text").ShouldContain(node => node.Value == id);
    }

    [Fact]
    public async Task NativePropertiesDoNotInheritEarlierNodeMetadata()
    {
        var result = await RenderAsync("""
            flowchart LR
                e1@{ step: 3, animation: slow }
                A e1@--> B
                e1@{ animation: fast }
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.Steps.ShouldBeEmpty();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(20));
        Edge(XElement.Parse(result.Svg), "e1").Attribute("stroke-dashoffset")!.Value.ShouldBe("900");
    }

    [Theory]
    [InlineData("e1", "cyl", "cylinder")]
    [InlineData("api.v1", "diamond", "diamond")]
    public async Task NativePropertiesPreserveTheNodeAppearanceBeforeTheEdgeDeclaration(
        string id, string shape, string svgShape)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                Before-->{{id}}
                {{id}}@{ shape: {{shape}}, label: 'Archive, cold' }
                {{id}} --> Audit
                A {{id}}@--> B
                {{id}}@{ animation: fast }
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-label") == "Archive, cold");
        node.Attribute("data-shape")!.Value.ShouldBe(svgShape);
        var nodeId = node.Attribute("data-id")!.Value;
        WithClass(svg, "edge").ShouldContain(edge => (string?)edge.Attribute("data-to") == nodeId);
        WithClass(svg, "edge").ShouldContain(edge => (string?)edge.Attribute("data-from") == nodeId);
        Edge(svg, id).Attribute("data-from")!.Value.ShouldBe("A");
    }

    [Fact]
    public async Task MultipleEarlyPropertyBlocksCombineTheNodeShapeAndLabel()
    {
        var result = await RenderAsync("""
            flowchart LR
                e1@{ shape: cyl }
                e1@{ label: 'Archive' }
                A e1@--> B
                e1@{ animate: true }
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-shape")!.Value.ShouldBe("cylinder");
        node.Attribute("data-label")!.Value.ShouldBe("Archive");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdaptedNodeWithManyReferencesEmitsTheLongLabelOnce(bool metadataFirst)
    {
        var label = string.Join(' ', Enumerable.Repeat("archive", 180));
        var references = string.Concat(Enumerable.Repeat("Client --> e1\n", 190));
        var metadata = $"e1@{{ label: '{label}' }}\ne1@{{ shape: cyl }}\n";
        var source = "flowchart LR\n" + (metadataFirst ? metadata + references : references + metadata) +
            "A e1@--> B\ne1@{ animate: true }";

        var syntax = MermaidNativeEdgeSyntax.Parse(source, true, TestContext.Current.CancellationToken).ShouldNotBeNull();
        var firstLabel = syntax.Source.IndexOf(label, StringComparison.Ordinal);
        firstLabel.ShouldBeGreaterThanOrEqualTo(0);
        syntax.Source.LastIndexOf(label, StringComparison.Ordinal).ShouldBe(firstLabel);
        var result = await RenderAsync(source);
        result.Animation.ShouldNotBeNull();
        var node = XElement.Parse(result.Svg).Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-label")!.Value.ShouldBe(label);
    }

    [Theory]
    [InlineData("subgraph pipe[Workers]", true)]
    [InlineData("subgraph pipe", true)]
    [InlineData("subgraph pipe[Workers]", false)]
    public async Task AnAlreadyDeclaredSubgraphTakesPrecedenceOverAnEdgeWithTheSameId(string heading, bool subgraphFirst)
    {
        var subgraph = $"{heading}\n  subgraph inner[Batch]\n    A --> B\n  end\nend\n";
        const string namedEdge = "Outside pipe@--> B\npipe@{ animate: true }\n";
        var source = "flowchart LR\n" + (subgraphFirst ? subgraph + namedEdge : namedEdge + subgraph);

        var result = await RenderAsync(source);

        (result.Animation is not null).ShouldBe(!subgraphFirst);
        var svg = XElement.Parse(result.Svg);
        svg.Descendants().ShouldContain(element => (string?)element.Attribute("data-id") == "pipe");
        var outside = WithClass(svg, "edge").Single(edge => (string?)edge.Attribute("data-from") == "Outside");
        (outside.Attribute("stroke-dashoffset") is not null).ShouldBe(!subgraphFirst);
    }

    [Theory]
    [InlineData("null", "Primary")]
    [InlineData("Null", "Primary")]
    [InlineData("NULL", "Primary")]
    [InlineData("~", "Primary")]
    [InlineData("'null'", "null")]
    public async Task MetadataDistinguishesYamlNullFromTheStringNull(string label, string expectedLabel)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                e1[Primary]
                e1@{ shape: cyl, label: {{label}} }
                A e1@--> B
                e1@{ animate: true }
            """);

        var node = XElement.Parse(result.Svg).Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-label")!.Value.ShouldBe(expectedLabel);
        node.Attribute("data-shape")!.Value.ShouldBe("cylinder");
    }

    [Theory]
    [InlineData("e1@{ shape: cyl }\ne1[Primary]\ne1@{ label: 'Archive' }", "Archive", "rectangle")]
    [InlineData("e1[Primary]\ne1@{ shape: cyl }\ne1[Final]", "Final", "rectangle")]
    [InlineData("e1[Primary]\ne1@{ shape: cyl, label: '' }", "Primary", "cylinder")]
    [InlineData("e1@{ label: 'Archive' }\ne1@{ shape: cyl, label: \"\" }", "Archive", "cylinder")]
    public async Task ExistingNodeAppearanceFollowsTheOrderOfClassicDeclarationsAndMetadata(
        string declarations, string expectedLabel, string expectedShape)
    {
        var result = await RenderAsync($"flowchart LR\nBefore --> e1\n{declarations}\nA e1@--> B\ne1@{{ animate: true }}");

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-label")!.Value.ShouldBe(expectedLabel);
        node.Attribute("data-shape")!.Value.ShouldBe(expectedShape);
        WithClass(svg, "edge").ShouldContain(edge => (string?)edge.Attribute("data-to") == "e1");
    }

    [Theory]
    [InlineData("|-", true)]
    [InlineData("|2-", true)]
    [InlineData(">-", false)]
    public async Task MultilineYamlLabelsReadTheIndentedBody(string indicator, bool literal)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                e1@{
                  label: {{indicator}} # label comment
                    Archive }
                    # storage
                  shape: cyl
                }
                A e1@--> B
                e1@{ animate: true }
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-label")!.Value.ReplaceLineEndings(" ").ShouldBe("Archive } # storage");
        node.Attribute("data-shape")!.Value.ShouldBe("cylinder");
        node.Descendants(svg.Name.Namespace + "tspan").Any(element => element.Value == "Archive }").ShouldBe(literal);
    }

    [Theory]
    [InlineData("rect", "rectangle", false)]
    [InlineData("cyl", "cylinder", true)]
    public async Task LongYamlLabelsRenderEvenWhenTheAdaptedDeclarationExceedsTheAuthoredLineLimit(
        string shape, string svgShape, bool includeFrontMatter)
    {
        var labelLine = string.Join(' ', Enumerable.Repeat("archive", 55));
        var labelLines = Enumerable.Repeat(labelLine, 20).ToArray();
        var label = string.Join("\n", labelLines.Select(line => "    " + line));
        var source = $"flowchart LR\ne1@{{\n  shape: {shape}\n  label: |-\n{label}\n}}\n" +
            "A e1@--> B\ne1@{ animate: true }";
        if (includeFrontMatter)
        {
            var ignored = new string('x', 8100);
            source = $"---\nignored: {ignored}\n---\n%% {ignored}\n" + source;
        }

        var result = await RenderAsync(source);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-shape")!.Value.ShouldBe(svgShape);
        node.Attribute("data-label")!.Value.ReplaceLineEndings(" ").ShouldBe(string.Join(' ', labelLines));
    }

    [Fact]
    public async Task NativeEdgeAdaptationEnforcesTheAuthoredLineLengthLimit()
    {
        var property = $"e1@{{ label: '{new string('x', 8100)}' }}";
        var source = $"flowchart LR\n{property}\nA e1@--> B\ne1@{{ animate: true }}";

        var error = await Should.ThrowAsync<Mermaider.MermaidResourceLimitException>(() => RenderAsync(source));

        error.LimitName.ShouldBe("MaxLineLength");
        error.ObservedValue.ShouldBe(property.Length);
    }

    [Fact]
    public async Task NativeEdgeAdaptationAccountsForGeneratedMarkupInTheTotalInputLimit()
    {
        var labelLine = new string('x', 24);
        var label = string.Join('\n', Enumerable.Repeat(" " + labelLine, 9800));
        var source = $"flowchart LR\ne1@{{\nlabel: |-\n{label}\n}}\nA e1@--> B\ne1@{{ animate: true }}";
        source.Length.ShouldBeLessThan(MermaiderSvgRenderer.MaximumSourceCharacters);
        var syntax = MermaidNativeEdgeSyntax.Parse(source, true, TestContext.Current.CancellationToken).ShouldNotBeNull();
        syntax.Source.Length.ShouldBeGreaterThan(MermaiderSvgRenderer.MaximumSourceCharacters);

        var result = await RenderAsync(source);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-label")!.Value.ReplaceLineEndings(" ").ShouldBe(string.Join(' ', Enumerable.Repeat(labelLine, 9800)));
    }

    [Theory]
    [InlineData("\"Archive {cold}\"", "Archive {cold}")]
    [InlineData("'Archive {cold}'", "Archive {cold}")]
    [InlineData("\"Archive \\\"cold\"", "Archive \"cold")]
    public async Task QuotedNodeMetadataKeepsBracesAndEscapedQuotes(string sourceLabel, string expectedLabel)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                e1@{ shape: cyl, label: {{sourceLabel}} }
                A e1@--> B
                e1@{ animate: true }
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-label")!.Value.ShouldBe(expectedLabel);
        node.Attribute("data-shape")!.Value.ShouldBe("cylinder");
    }

    [Theory]
    [InlineData("Archive", "Archive")]
    [InlineData("'Archive # cold'", "Archive # cold")]
    [InlineData("Archive#cold", "Archive#cold")]
    public async Task MetadataIgnoresCommentsAndPreservesLiteralHashes(string sourceLabel, string expectedLabel)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                e1@{
                  # Storage node: comment with a closing brace }
                  shape: cyl # database
                  label: {{sourceLabel}} # storage tier
                }
                A e1@--> B
                e1@{
                  animate: true # request flow
                }
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-label")!.Value.ShouldBe(expectedLabel);
        node.Attribute("data-shape")!.Value.ShouldBe("cylinder");
    }

    [Theory]
    [InlineData("e1[Primary]", "shape: cyl, label: 'Archive'", "Archive")]
    [InlineData("e1[(Primary)]", "label: 'Archive'", "Archive")]
    [InlineData("e1[Primary]", "shape: cyl", "Primary")]
    public async Task MetadataUpdatesExistingNodesAndRetainsUnspecifiedFields(string declaration, string properties, string expectedLabel)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                Before-->e1
                {{declaration}}
                e1@{ {{properties}} }
                A e1@--> B
                e1@{ animation: fast }
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element => (string?)element.Attribute("data-id") == "e1");
        node.Attribute("data-label")!.Value.ShouldBe(expectedLabel);
        node.Attribute("data-shape")!.Value.ShouldBe("cylinder");
        WithClass(svg, "edge").ShouldContain(edge => (string?)edge.Attribute("data-to") == "e1");
    }

    [Theory]
    [InlineData("accTitle: Notes on [\"", "\n")]
    [InlineData("accDescr : Notes on [", "\r\n")]
    [InlineData("accDescr {\nLiteral [\"\n}\n", "\n")]
    public async Task NativeEdgeAnimationCanFollowAccessibilityText(string directive, string newline)
    {
        var source = string.Join(newline, "flowchart LR", directive, "A e1@--> B", "e1@{ animate: true }");

        var result = await RenderAsync(source);

        result.Animation.ShouldNotBeNull();
        Edge(XElement.Parse(result.Svg), "e1").Attribute("data-from")!.Value.ShouldBe("A");
    }

    [Theory]
    [InlineData("A e1@--> B; accTitle: Orders; rush", "title")]
    [InlineData("A e1@--> B; accDescr: Orders; rush", "desc")]
    [InlineData("\u00a0accTitle\u2003: Orders; rush\nA e1@--> B", "title")]
    [InlineData("accDescr\u00a0{Orders; rush}\nA e1@--> B", "desc")]
    [InlineData("accDescr {Orders; rush}\nA e1@--> B", "desc")]
    [InlineData("accDescr {Orders; rush}; A e1@--> B", "desc")]
    [InlineData("accDescr {\nOrders; rush\n}; A e1@--> B", "desc")]
    [InlineData("A e1@--> B; accDescr {Orders; rush}", "desc")]
    public async Task NativeAccessibilityTextPreservesSemicolonsAndAdjacentEdges(string body, string svgElement)
    {
        var result = await RenderAsync($"flowchart LR\n{body}\ne1@{{ animate: true }}");

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        svg.Element(svg.Name.Namespace + svgElement)!.Value.ShouldBe("Orders; rush");
        svg.Descendants().Attributes("data-id").Select(attribute => attribute.Value).ShouldBe(["A", "B"], ignoreOrder: true);
        Edge(svg, "e1").Attribute("data-to")!.Value.ShouldBe("B");
    }

    [Fact]
    public async Task NativeAccessibilityDescriptionsPreserveLiteralCommentMarkersInEveryFrame()
    {
        var result = await RenderAsync("flowchart LR\naccDescr {%% Urgent}\nA e1@--> B\ne1@{ animate: true }");

        var animation = result.Animation.ShouldNotBeNull();
        foreach (var svgText in new[] { result.Svg, animation.RenderFrame(TimeSpan.FromMilliseconds(100)) })
        {
            var svg = XElement.Parse(svgText);
            svg.Element(svg.Name.Namespace + "desc")!.Value.ShouldBe("%% Urgent");
            Edge(svg, "e1").Attribute("data-to")!.Value.ShouldBe("B");
        }
    }

    [Theory]
    [InlineData("A e1@--> B\nC[\"First line\nSecond line\"] / broken", 4, 15, "Second line\"] / broken")]
    [InlineData("accDescr {Orders}\nA e1@--> B\nC[\"First line\nSecond line\"] / broken", 5, 15, "Second line\"] / broken")]
    [InlineData("A e1@--> B / broken", 2, 12, "A e1@--> B / broken")]
    public async Task NativeEdgesPreserveAuthoredDiagramErrorLocation(string body, int line, int column, string sourceLine)
    {
        var error = await Should.ThrowAsync<Mermaider.MermaidParseException>(() =>
            RenderAsync($"flowchart LR\n{body}\ne1@{{ animate: true }}"));

        error.Line.ShouldBe(line);
        error.Column.ShouldBe(column);
        error.SourceLine.ShouldBe(sourceLine);
    }

    [Theory]
    [InlineData("--o", 18, "--o")]
    [InlineData("  -- Ready ---->", 20, "---->")]
    [InlineData("-- Ready --o", 18, "--o")]
    public async Task RewrittenNamedArrowsReportMissingTargetsAtTheAuthoredOperator(string arrow, int column, string token)
    {
        var statement = $"A veryLongEdgeId@{arrow}";
        var error = await Should.ThrowAsync<Mermaider.MermaidParseException>(() =>
            RenderAsync($"flowchart LR\n{statement}\nveryLongEdgeId@{{ animate: true }}"));

        error.Problem.ShouldBe($"link '{token}' has no target node");
        error.Line.ShouldBe(2);
        error.Column.ShouldBe(column);
        error.SourceLine.ShouldBe(statement);
    }

    [Theory]
    [InlineData("A e1@--o / broken", "--o")]
    [InlineData("A e1@--o B --- / broken", "---")]
    public async Task DiagnosticsForInvalidTargetsNameTheAuthoredArrow(string statement, string token)
    {
        var error = await Should.ThrowAsync<Mermaider.MermaidParseException>(() =>
            RenderAsync($"flowchart LR\n{statement}\ne1@{{ animate: true }}"));

        error.Problem.ShouldBe($"link '{token}' is followed by something that is not a node");
        error.Column.ShouldBe(statement.IndexOf('/') + 1);
    }

    [Theory]
    [InlineData("A e1@-->|\"`**First**\nSecond`\"| B", "A")]
    [InlineData("A e1@--\"`**First**\r\nSecond`\"--> B", "A")]
    [InlineData("A -->|\"`**First**\nSecond`\"| B\nB e1@--> C", "B")]
    public async Task NativeFlowchartsPreserveMultilineMarkdownEdgeLabels(string body, string animatedSource)
    {
        var result = await RenderAsync($"flowchart LR\n{body}\ne1@{{ animate: true }}");

        var animation = result.Animation.ShouldNotBeNull();
        foreach (var frame in new[] { result.Svg, animation.RenderFrame(TimeSpan.FromMilliseconds(100)) })
        {
            var svg = XElement.Parse(frame);
            Edge(svg, "e1").Attribute("data-from")!.Value.ShouldBe(animatedSource);
            var spans = svg.Descendants(svg.Name.Namespace + "tspan");
            spans.ShouldContain(span => span.Value == "First" && (string?)span.Attribute("font-weight") == "bold");
            spans.ShouldContain(span => span.Value == "Second" && span.Attribute("dy") != null);
        }
    }

    [Fact]
    public async Task NamedAnimationMatchesTheEdgeWithAMultilineLabel()
    {
        var result = await RenderAsync("""
            flowchart LR
                A plain@-->|Read orders| B
                A selected@-->|Read<br/>orders| B
                selected@{ animate: true }
            """);

        var animation = result.Animation.ShouldNotBeNull();
        var first = XElement.Parse(result.Svg);
        var later = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(0.25)));
        var edges = WithClass(first, "edge").ToArray();
        edges.Length.ShouldBe(2);
        edges[0].Attribute("stroke-dashoffset").ShouldBeNull();
        edges[1].ShouldBeSameAs(Edge(first, "selected"));
        edges[1].Attribute("data-label")!.Value.ShouldBe("Read orders");
        Edge(later, "selected").Attribute("stroke-dashoffset")!.Value
            .ShouldNotBe(edges[1].Attribute("stroke-dashoffset")!.Value);
    }

    [Theory]
    [InlineData(@"9\,5", "pulse", "class active pulse")]
    [InlineData("9,5", "pulse", "class active pulse")]
    [InlineData("9px 5px", "pulse", "class active pulse")]
    [InlineData(@"9\,5", "pulse", "active:::pulse")]
    [InlineData(@"9\,5", "flow-pulse", "active:::flow-pulse")]
    public async Task ClassDefDashUsesTheAuthoredPatternOffsetAndDuration(string dashArray, string className, string assignment)
    {
        var result = await RenderAsync($"""
            flowchart LR
                classClient[Client] active@--> B[Server] idle@--> C[Archive]
                classDef {className} stroke-dasharray: {dashArray},stroke-dashoffset: 900,animation: dash 25s linear infinite;
                {assignment}
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(25));
        var first = XElement.Parse(result.Svg);
        var middle = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(12.5)));
        var nextLoop = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(25)));
        Edge(first, "active").Attribute("stroke-dasharray")!.Value.ShouldBe("9 5");
        Edge(first, "active").Attribute("stroke-dashoffset")!.Value.ShouldBe("900");
        Edge(middle, "active").Attribute("stroke-dashoffset")!.Value.ShouldBe("450");
        Edge(nextLoop, "active").Attribute("stroke-dashoffset")!.Value.ShouldBe("900");
        WithClass(middle, "edge").Count(edge => edge.Attribute("stroke-dashoffset") is not null).ShouldBe(1);
    }

    [Theory]
    [InlineData("class api.v1, B pulse", false)]
    [InlineData("api.v1:::pulse", true)]
    public async Task ClassesAcceptADottedEdgeTarget(string assignment, bool declaresNode)
    {
        var result = await RenderAsync($"""
            flowchart LR
                A api.v1@--> B
                classDef pulse stroke-dasharray: 4 2,stroke-dashoffset: 40,animation: dash 2s linear infinite
                {assignment}
            """);

        var animation = result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(1)));
        Edge(svg, "api.v1").Attribute("stroke-dashoffset")!.Value.ShouldBe("20");
        svg.Descendants(svg.Name.Namespace + "text").Any(node => node.Value == "api.v1").ShouldBe(declaresNode);
    }

    [Theory]
    [InlineData("-->|api.v1|")]
    [InlineData("--\"api.v1\"-->")]
    public async Task DottedNodeDeclaredByAClassShorthandCanBeLinkedInAGroupAndAChain(string arrow)
    {
        var result = await RenderAsync($"""
            flowchart LR
                A api.v1@--> B
                api.v1:::pulse
                Audit{arrow}api.v1 & Other
                api.v1 --> Log --> api.v1
                classDef pulse stroke-dasharray: 4 2,stroke-dashoffset: 40,animation: dash 2s linear infinite
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        var node = svg.Descendants().Single(element =>
            element.Attribute("data-id") is not null && (string?)element.Attribute("data-label") == "api.v1");
        var nodeId = node.Attribute("data-id")!.Value;
        var edges = WithClass(svg, "edge").ToArray();
        edges.Count(edge => (string?)edge.Attribute("data-to") == nodeId).ShouldBe(2);
        edges.Count(edge => (string?)edge.Attribute("data-from") == nodeId).ShouldBe(1);
        edges.Count(edge => (string?)edge.Attribute("data-label") == "api.v1").ShouldBe(2);
        Edge(svg, "api.v1").Attribute("data-to")!.Value.ShouldBe("B");
    }

    [Fact]
    public async Task ClassDefDashKeepsItsPhaseWhenTheStepTimelineWraps()
    {
        var result = await RenderAsync("""
            flowchart LR
                A first@--> B second@--> C
                first@{ step: 1 }
                second@{ step: 2 }
                classDef request-flow stroke-dasharray: 6 4;
                classDef request-flow stroke-dashoffset: -80,animation: 3200ms linear infinite dash;
                class first,second request-flow
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(2));
        var frame = XElement.Parse(animation.CreateFrame(TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(2.4).Ticks));
        VisibleDots(frame).Count().ShouldBe(1);
        Edge(frame, "first").Attribute("stroke-dashoffset")!.Value.ShouldBe("-20");
        Edge(frame, "second").Attribute("stroke-dashoffset")!.Value.ShouldBe("-20");
    }

    [Fact]
    public async Task ClassDefDashUsesTheOrderOfClassAssignments()
    {
        var result = await RenderAsync("""
            flowchart LR
                A e1@--> B
                classDef first stroke-dasharray: 2 2,animation: dash 2s linear infinite
                classDef second stroke-dasharray: 3 3,stroke-dashoffset: 40,animation: dash 3s linear infinite
                class e1 second
                class e1 first
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(2));
        var frame = XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(1)));
        var edge = Edge(frame, "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("2 2");
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe("20");
    }

    [Theory]
    [InlineData("pulse", "e1:::pulse")]
    [InlineData("pulse", "class e1 pulse")]
    [InlineData("flow-pulse", "e1:::flow-pulse")]
    [InlineData("flow-pulse", "class e1 flow-pulse")]
    public async Task ClassAssignmentsDoNotAnimateEdgesDeclaredLater(string className, string assignment)
    {
        var result = await RenderAsync($"""
            flowchart LR
                {assignment}
                A e1@--> B
                classDef {className} stroke-dasharray: 4 2,stroke-dashoffset: 40,animation: dash 2s linear infinite
            """);

        result.Animation.ShouldBeNull();
        WithClass(XElement.Parse(result.Svg), "edge").Single().Attribute("stroke-dashoffset").ShouldBeNull();
    }

    [Fact]
    public async Task AClassAssignmentAnimatesOnlyTheTargetsAlreadyDeclared()
    {
        var result = await RenderAsync("""
            flowchart LR
                A first@--> B
                class first, second pulse
                A second@--> C
                classDef pulse stroke-dasharray: 4 2,stroke-dashoffset: 40,animation: dash 2s linear infinite
            """);

        result.Animation.ShouldNotBeNull();
        var svg = XElement.Parse(result.Svg);
        Edge(svg, "first").Attribute("stroke-dashoffset")!.Value.ShouldBe("40");
        WithClass(svg, "edge").Single(edge => (string?)edge.Attribute("data-to") == "C")
            .Attribute("stroke-dashoffset").ShouldBeNull();
    }

    [Fact]
    public async Task ClassDefDashRemainsStaticWhenAnimationIsDisabled()
    {
        var result = await RenderAsync("""
            flowchart LR
                A e1@--> B
                classDef pulse stroke-dasharray: 4 2,stroke-dashoffset: 40,animation: dash 2s linear infinite;
                class e1 pulse
            """, enableAnimation: false);

        result.Animation.ShouldBeNull();
        var edge = Edge(XElement.Parse(result.Svg), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("4 2");
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe("40");
    }

    [Theory]
    [InlineData("classDef pulse stroke-dasharray: 0 0\nclass e1 pulse")]
    [InlineData("class e1 pulse\nclassDef reset stroke-dasharray: 0 0\nclass e1 reset")]
    [InlineData("class e1 pulse\nclassDef reset stroke-dasharray: none\nclass e1 reset")]
    public async Task LaterClassDashResetsClearTheInheritedPattern(string overrides)
    {
        var result = await RenderAsync($"""
            flowchart LR
                A e1@-.-> B
                classDef pulse stroke-dasharray: 4 2,stroke-dashoffset: 40,animation: dash 2s linear infinite
                {overrides}
            """);

        result.Animation.ShouldBeNull();
        var edge = Edge(XElement.Parse(result.Svg), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("none");
    }

    [Theory]
    [InlineData("stroke-dashoffset: 0", "0")]
    [InlineData("animation: none", "40")]
    public async Task LaterClassStopsMotionWithoutClearingTheDashPattern(string style, string offset)
    {
        var result = await RenderAsync($"""
            flowchart LR
                A e1@--> B
                classDef pulse stroke-dasharray: 4 2,stroke-dashoffset: 40,animation: dash 2s linear infinite
                classDef still {style}
                class e1 pulse
                class e1 still
            """);

        result.Animation.ShouldBeNull();
        var edge = Edge(XElement.Parse(result.Svg), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("4 2");
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe(offset);
    }

    [Fact]
    public async Task ClassDashPatternUsesNativeMotionWhenTheClassDoesNotSetMotion()
    {
        var result = await RenderAsync("""
            flowchart LR
                A e1@--> B
                e1@{ animation: fast }
                classDef pattern stroke-dasharray: 4 2
                class e1 pattern
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(20));
        var edge = Edge(XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(10))), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("4 2");
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe("450");
    }

    [Theory]
    [InlineData("animation: none", 0, "900")]
    [InlineData("stroke-dashoffset: 0", 0, "0")]
    [InlineData("STROKE-DASHOFFSET: 0", 0, "0")]
    [InlineData("animation: dash 2s linear infinite", 2, "900")]
    [InlineData("stroke-dashoffset: 80", 20, "80")]
    public async Task PartialClassStylesOverrideNativeDashMotion(string style, int durationSeconds, string offset)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                A e1@--> B
                e1@{ animation: fast }
                classDef custom {{style}}
                class e1 custom
            """);

        (result.Animation?.ScoreDuration).ShouldBe(durationSeconds == 0 ? (TimeSpan?)null : TimeSpan.FromSeconds(durationSeconds));
        var edge = Edge(XElement.Parse(result.Svg), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("9 5");
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe(offset);
    }

    [Theory]
    [InlineData("stroke-dashoffset: 80", "9 5", "80")]
    [InlineData("animation: dash 2s linear infinite", "9 5", "900")]
    [InlineData("stroke-dasharray: 4 2", "4 2", "900")]
    public async Task PartialClassStylesInheritNativeDashesWithPlaybackDisabled(string style, string pattern, string offset)
    {
        var result = await RenderAsync($$"""
            flowchart LR
                A e1@--> B
                e1@{ animation: fast }
                classDef custom {{style}}
                class e1 custom
            """, enableAnimation: false);

        result.Animation.ShouldBeNull();
        var edge = Edge(XElement.Parse(result.Svg), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe(pattern);
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe(offset);
    }

    [Theory]
    [InlineData("stroke-dasharray: 4 2,stroke-dashoffset: 80,animation: dash 2s LINEAR infinite")]
    [InlineData("stroke-dasharray: 4 2,stroke-dashoffset: 80,animation: dash 2s linear INFINITE")]
    [InlineData("STROKE-DASHARRAY: 4 2,stroke-dashoffset: 80,animation: dash 2s linear infinite")]
    [InlineData("stroke-dasharray: 4 2,STROKE-DASHOFFSET: 80,animation: dash 2s linear infinite")]
    [InlineData("stroke-dasharray: 4 2,stroke-dashoffset: 80,ANIMATION: dash 2s linear infinite")]
    public async Task CssDashPropertyNamesAndKeywordsIgnoreCase(string style)
    {
        var result = await RenderAsync($"""
            flowchart LR
                A e1@--> B
                classDef custom {style}
                class e1 custom
            """);

        var animation = result.Animation.ShouldNotBeNull();
        animation.ScoreDuration.ShouldBe(TimeSpan.FromSeconds(2));
        var edge = Edge(XElement.Parse(animation.RenderFrame(TimeSpan.FromSeconds(1))), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("4 2");
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe("40");
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("0ms")]
    public async Task ZeroDurationClassAnimationRetainsTheStaticDashStyle(string duration)
    {
        var result = await RenderAsync($"""
            flowchart LR
                A e1@--> B
                classDef custom stroke-dasharray: 4 2,stroke-dashoffset: 40,animation: dash {duration} linear infinite
                class e1 custom
            """);

        result.Animation.ShouldBeNull();
        var edge = Edge(XElement.Parse(result.Svg), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("4 2");
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe("40");
    }

    [Fact]
    public async Task ClassOffsetPreservesTheDottedEdgePattern()
    {
        var result = await RenderAsync("""
            flowchart LR
                A e1@-.-> B
                classDef custom stroke-dashoffset: 3
                class e1 custom
            """);

        result.Animation.ShouldBeNull();
        var edge = Edge(XElement.Parse(result.Svg), "e1");
        edge.Attribute("stroke-dasharray")!.Value.ShouldBe("5 4");
        edge.Attribute("stroke-dashoffset")!.Value.ShouldBe("3");
    }

    [Theory]
    [InlineData("wash 2s linear infinite", "7 3")]
    [InlineData("DASH 2s LINEAR INFINITE", "7 3")]
    [InlineData("dash 2s linear infinite", "url(https://example.invalid/pattern)")]
    public async Task UnsupportedClassDefCssDoesNotAnimateOrEnterTheSvg(string animation, string dashArray)
    {
        var result = await RenderAsync($"""
            flowchart LR
                A e1@--> B
                classDef pulse stroke-dasharray: {dashArray},stroke-dashoffset: 40,animation: {animation};
                class e1 pulse
            """);

        result.Animation.ShouldBeNull();
        result.Svg.ShouldNotContain("example.invalid");
        result.Svg.ShouldNotContain("wash 2s");
        WithClass(XElement.Parse(result.Svg), "edge").Single().Attribute("stroke-dashoffset").ShouldBeNull();
    }

    private static Task<MermaidDiagramRenderResult> RenderAsync(string source, bool enableAnimation = true) =>
        new MermaiderSvgRenderer().RenderDiagramAsync(
            new MermaidSvgRenderRequest(source, MarkdownThemePalette.Light, "Inter", 14),
            enableAnimation, TestContext.Current.CancellationToken);

    private static IEnumerable<XElement> WithClass(XElement svg, string className) =>
        svg.Descendants().Where(element => (string?)element.Attribute("class") == className);

    private static IEnumerable<XElement> VisibleDots(XElement svg) =>
        WithClass(svg, "edge-step-dot").Where(dot => (string?)dot.Attribute("opacity") == "1");

    private static XElement Edge(XElement svg, string id) =>
        WithClass(svg, "edge").Single(edge => (string?)edge.Attribute("data-mermaid-edge-id") == id);
}
