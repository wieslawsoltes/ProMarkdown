using Avalonia.Controls.Documents;

namespace ProMarkdown.Services;

public sealed class MarkdownRenderController(IMarkdownParsingService parsingService, IMarkdownInlineRenderingService inlineRenderingService) : IMarkdownRenderController
{
    private readonly IMarkdownParsingService _parsingService = parsingService ?? throw new ArgumentNullException(nameof(parsingService));
    private readonly IMarkdownInlineRenderingService _inlineRenderingService = inlineRenderingService ?? throw new ArgumentNullException(nameof(inlineRenderingService));

    public MarkdownRenderResult Render(MarkdownRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);

        if (string.IsNullOrWhiteSpace(request.Markdown))
        {
            return MarkdownRenderResult.Empty(request.Context.ResourceTracker);
        }

        var parseResult = _parsingService.Parse(request.Markdown);
        var result = _inlineRenderingService.Render(parseResult, request.Context);
        MarkdownRichBlockBorderNormalizer.PreserveRoundedBorders(result.Inlines);
        MarkdownTaskListNormalizer.Normalize(result.Inlines, parseResult, request.Context);
        MarkdownBlockQuoteNormalizer.Normalize(result.Inlines, parseResult, request.Context);
        MarkdownSelectionNormalizer.IsolateTextSegments(result.Inlines, request.Context, parseResult);
        return result;
    }
}
