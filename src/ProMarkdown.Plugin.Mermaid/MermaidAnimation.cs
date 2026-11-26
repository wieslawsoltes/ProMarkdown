using Mermaider.Animate;

namespace ProMarkdown.Plugin.Mermaid;

internal sealed record MermaidAnimation(
    Func<TimeSpan, Int128, string> CreateFrame,
    TimeSpan Duration,
    TimeSpan ScoreDuration,
    IReadOnlyList<AnimationStep> Steps,
    bool Repeats,
    TimeSpan Intro = default,
    bool ScrubsOnScroll = false)
{
    public IReadOnlyList<MermaidAnimationScenario> Scenarios { get; init; } = [];

    public string RenderFrame(TimeSpan time) => CreateFrame(time, time.Ticks);

    public static MermaidAnimation FromScore(AnimatedDiagram score, string? descriptionPercentMarker) =>
        FromTimeline(score, descriptionPercentMarker) with
    {
        Scenarios = score.Scenarios.Select(scenario =>
            new MermaidAnimationScenario(scenario.Name, FromTimeline(scenario.Animation, descriptionPercentMarker))).ToArray()
    };

    private static MermaidAnimation FromTimeline(AnimatedDiagram score, string? descriptionPercentMarker) => new(
        (time, _) => MermaidAccessibilitySyntax.RestoreDescription(score.RenderFrame(time), descriptionPercentMarker),
        score.Duration, score.ScoreDuration, score.Steps,
        score.Mode == PlaybackMode.Loop &&
        MermaiderSvgRenderer.GetGeneratedStyle(score.Svg).Contains(" infinite", StringComparison.Ordinal),
        score.Intro, score.Mode == PlaybackMode.Scrub);
}

internal sealed record MermaidAnimationScenario(string Name, MermaidAnimation Animation);
