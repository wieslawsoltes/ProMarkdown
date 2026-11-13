using System.Xml.Linq;
using System.Text;
using ProMarkdown.Plugin.Mermaid;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class MermaidSvgColorResolverTests
{
    [Theory]
    [InlineData("color-mix(in srgb, #000000 14%, #FFFFFF)", "#DBDBDB")]
    [InlineData("color-mix(in srgb, #145DC5 74%, #000000)", "#0F4592")]
    [InlineData("color-mix(in srgb, #000 2.1%, #fff)", "#FAFAFA")]
    [InlineData("color-mix(in srgb, #000000 0%, #FFFFFF)", "#FFFFFF")]
    [InlineData("color-mix(in srgb, #000000 100%, #FFFFFF)", "#000000")]
    [InlineData("color-mix(in srgb, #145DC5 12%, transparent)", "rgba(20,93,197,0.12)")]
    [InlineData("color-mix(in srgb, color-mix(in srgb, #FFFFFF 50%, black) 50%, transparent)", "rgba(128,128,128,0.5)")]
    public void SanitizerComputesSrgbColorsAndPremultipliedAlpha(string expression, string expected)
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\"><rect fill=\"{expression}\" /></svg>";
        var sanitized = MermaidSvgSanitizer.SanitizeMermaiderSvg(svg);
        var document = XDocument.Parse(sanitized);

        document.Root!.Elements().Single().Attribute("fill")!.Value.ShouldBe(expected);
        sanitized.ShouldNotContain("color-mix(");
    }

    [Fact]
    public void RootPaletteOverridesStylesheetAndNestedVariableFallbacksResolve()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" style="--bg:#FFFFFF;--fg:#000000">
              <style>svg { --bg:#000000; --fg:#FFFFFF; --line:var(--missing, var(--fg)); --soft:color-mix(in srgb, var(--line) 14%, var(--bg)); }</style>
              <rect style="fill:color-mix(in srgb, var(--line) 14%, var(--bg))" stroke="var(--soft)" />
              <text>color-mix(in srgb, red 50%, blue)</text>
            </svg>
            """;
        var document = XDocument.Parse(MermaidSvgSanitizer.SanitizeMermaiderSvg(svg));
        var ns = document.Root!.Name.Namespace;

        document.Root.Element(ns + "style")!.Value.ShouldContain("--soft:#DBDBDB");
        document.Root.Element(ns + "rect")!.Attribute("style")!.Value.ShouldBe("fill:#DBDBDB");
        document.Root.Element(ns + "text")!.Value.ShouldBe("color-mix(in srgb, red 50%, blue)");
    }

    [Theory]
    [InlineData("--a:var(--a)", "color-mix(in srgb, var(--a) 50%, white)")]
    [InlineData("", "color-mix(in srgb, var(--missing) 50%, white)")]
    [InlineData("", "color-mix(in lab, black 50%, white)")]
    [InlineData("", "color-mix(in srgb, black 101%, white)")]
    [InlineData("", "color-mix(in srgb, black 50%, white")]
    public void UnsupportedOrCyclicColorExpressionsFailClearly(string declarations, string expression)
    {
        var svg = $"<svg style=\"{declarations}\"><rect fill=\"{expression}\" /></svg>";

        Should.Throw<InvalidOperationException>(() => MermaidSvgSanitizer.SanitizeMermaiderSvg(svg));
    }

    [Fact]
    public void CustomRendererCssScopesAndFallbacksArePreserved()
    {
        const string svg = """
            <svg><style>svg { --tone:#000000; } .other { --tone:#FFFFFF; }</style>
              <rect fill="var(--tone, color-mix(in srgb, var(--missing) 50%, #FFFFFF))" />
            </svg>
            """;

        XNode.DeepEquals(XDocument.Parse(MermaidSvgSanitizer.Sanitize(svg)), XDocument.Parse(svg)).ShouldBeTrue();
    }

    [Fact]
    public void MermaiderTokensDoNotReadUnrelatedSelectors()
    {
        const string svg = """
            <svg><style>svg { --tone:#000000; } .other { --tone:#FFFFFF; }</style>
              <rect fill="color-mix(in srgb, var(--tone) 50%, #000000)" />
            </svg>
            """;
        var document = XDocument.Parse(MermaidSvgSanitizer.SanitizeMermaiderSvg(svg));

        document.Root!.Element("rect")!.Attribute("fill")!.Value.ShouldBe("#000000");
    }

    [Theory]
    [InlineData("#145DC5", "#145DC5")]
    [InlineData("var(--actual)", "#145DC5")]
    public void ActiveVariableDoesNotEvaluateAnUnusedColorMixFallback(string tone, string expected)
    {
        var svg = $"""
            <svg style="--actual:#145DC5;--tone:{tone}">
              <rect fill="var(--tone, color-mix(in srgb, var(--missing) 50%, white))" />
            </svg>
            """;
        var document = XDocument.Parse(MermaidSvgSanitizer.SanitizeMermaiderSvg(svg));

        document.Root!.Element("rect")!.Attribute("fill")!.Value.ShouldBe(expected);
    }

    [Fact]
    public void MissingVariableEvaluatesItsColorMixFallback()
    {
        const string svg = "<svg><rect fill=\"var(--missing, color-mix(in srgb, #000000 50%, white))\" /></svg>";
        var document = XDocument.Parse(MermaidSvgSanitizer.SanitizeMermaiderSvg(svg));

        document.Root!.Element("rect")!.Attribute("fill")!.Value.ShouldBe("#808080");
    }

    [Fact]
    public void MemoizedVariablesKeepFractionalColorPrecision()
    {
        const string svg = """
            <svg style="--half:color-mix(in srgb, #FFFFFF 50%, black)">
              <rect fill="color-mix(in srgb, var(--half) 50%, white)" />
              <rect fill="color-mix(in srgb, var(--half) 50%, white)" />
            </svg>
            """;
        var document = XDocument.Parse(MermaidSvgSanitizer.SanitizeMermaiderSvg(svg));

        foreach (var rectangle in document.Root!.Elements("rect"))
            rectangle.Attribute("fill")!.Value.ShouldBe("#BFBFBF");
    }

    [Fact]
    public void SharedVariableExpressionsResolveWithinTheWorkBudget()
    {
        var svg = new StringBuilder("<svg><style>svg { --c0:#000000;");
        for (var index = 1; index <= 14; index++)
            svg.Append($"--c{index}:color-mix(in srgb, var(--c{index - 1}) 50%, var(--c{index - 1}));");
        svg.Append("}</style>");
        for (var index = 0; index < 5000; index++)
            svg.Append("<rect fill=\"color-mix(in srgb, var(--c14) 50%, white)\" />");
        svg.Append("</svg>");

        var sanitized = MermaidSvgSanitizer.SanitizeMermaiderSvg(svg.ToString());

        sanitized.ShouldNotContain("color-mix(");
        sanitized.ShouldContain("fill=\"#808080\"");
    }

    [Fact]
    public void ExcessiveColorEvaluationWorkFailsClearly()
    {
        var svg = new StringBuilder("<svg>");
        for (var index = 0; index < MermaidSvgColorResolver.MaximumExpressionEvaluations; index++)
            svg.Append("<rect fill=\"color-mix(in srgb, black 50%, white)\" />");
        svg.Append("</svg>");

        Should.Throw<InvalidOperationException>(() => MermaidSvgSanitizer.SanitizeMermaiderSvg(svg.ToString()))
            .Message.ShouldContain("work limit");
    }
}
