using Avalonia.Headless.XUnit;
using Markdig.Extensions.Figures;
using Markdig.Syntax;
using ProMarkdown.Plugin.Figures;
using ProMarkdown.Services;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class FigureParsingTests
{
    private const string LeadingCaption = "Preview **surface**";
    private const string TrailingCaption = "Supporting notes with `^^^` inside inline code.";
    private const string Body = "Supporting markdown inside the figure.\n\n- First item\n- Second item\n\n```json\n{\"feature\": \"figures\"}\n```";
    private static readonly string FigureSource = $"^^^ {LeadingCaption}\n{Body}\n^^^ {TrailingCaption}";

    [AvaloniaTheory]
    [InlineData("", "\n")]
    [InlineData("", "\r\n")]
    [InlineData("Introductory content.\n\n", "\n")]
    [InlineData("Introductory content.\n\n", "\r\n")]
    [InlineData("# Before the figure\n\nA longer introduction with Unicode: café.\n\n", "\n")]
    [InlineData("# Before the figure\n\nA longer introduction with Unicode: café.\n\n", "\r\n")]
    public void DocumentFigureKeepsCaptionsBodyAndSpansInTheirSourceCoordinates(string prefix, string lineEnding)
    {
        var source = (prefix + FigureSource + "\n\n## Following section\n\nFollowing text.")
            .Replace("\n", lineEnding, StringComparison.Ordinal);
        var parseResult = new MarkdownParsingService().Parse(source);
        var figure = parseResult.Document.OfType<Figure>().Single();

        var document = MarkdownFigureParser.Parse(figure, parseResult);

        AssertFigure(document);
        document.SourceMarkdown.ShouldBe(MarkdownSourceSpan.FromMarkdig(figure.Span).Slice(source));
        MarkdownFigureSyntax.NormalizeBlockText(document.BodySpan.Slice(document.SourceMarkdown)).Trim().ShouldBe(Body);
        document.LeadingCaption!.Span.Slice(document.SourceMarkdown).ShouldBe(LeadingCaption);
        document.TrailingCaption!.Span.Slice(document.SourceMarkdown).ShouldBe(TrailingCaption);
    }

    [AvaloniaTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void StandaloneFigureUsedByEditorPreviewKeepsCaptionsAndBody(string lineEnding)
    {
        var document = MarkdownFigureParser.Parse(FigureSource.Replace("\n", lineEnding, StringComparison.Ordinal));

        AssertFigure(document);
    }

    [AvaloniaFact]
    public void TransformedMarkdownUsesTheParsedSourceForAstSpans()
    {
        var parseResult = new MarkdownParsingService([new PrefixParserPlugin()]).Parse(FigureSource);
        parseResult.UsesOriginalSourceSpans.ShouldBeFalse();
        var figure = parseResult.Document.OfType<Figure>().Single();

        var document = MarkdownFigureParser.Parse(figure, parseResult);

        AssertFigure(document);
        document.SourceMarkdown.ShouldBe(FigureSource);
    }

    [AvaloniaFact]
    public void MissingContainerSpanRebasesRecoveredSourceBeforeParsingChildren()
    {
        var parseResult = new MarkdownParsingService().Parse("Introductory content.\n\n" + FigureSource);
        var figure = parseResult.Document.OfType<Figure>().Single();
        figure.Span = SourceSpan.Empty;

        var document = MarkdownFigureParser.Parse(figure, parseResult);

        AssertFigure(document);
    }

    private static void AssertFigure(MarkdownFigureDocument document)
    {
        document.LeadingCaption.ShouldNotBeNull().Markdown.ShouldBe(LeadingCaption);
        document.TrailingCaption.ShouldNotBeNull().Markdown.ShouldBe(TrailingCaption);
        document.BodyMarkdown.ShouldBe(Body);
        document.Diagnostics.ShouldBeEmpty();
    }

    private sealed class PrefixParserPlugin : IMarkdownParserPlugin
    {
        public string TransformMarkdown(string markdown) => "Injected introduction.\n\n" + markdown;
    }
}

