using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Markdig.Syntax;

namespace CodexGui.Markdown.Services;

public static class MarkdownCodeBlockRendering
{
    private static readonly FontFamily MonospaceFamily = new("Cascadia Mono, Consolas, Courier New");
    private static readonly IBrush SurfaceBackground = new SolidColorBrush(Color.Parse("#F6F8FA"));
    private static readonly IBrush SurfaceBorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush HeaderBackground = new SolidColorBrush(Color.Parse("#EAEEF2"));
    private static readonly IBrush DefaultMetaForeground = new SolidColorBrush(Color.Parse("#6E7781"));

    public static IBrush DefaultCodeTextForeground { get; } = new SolidColorBrush(Color.Parse("#1F2328"));

    public static MarkdownCodeBlockSurface CreateSurface(
        CodeBlock block,
        string code,
        InlineCollection inlines,
        string? languageHint,
        string metaText,
        MarkdownRenderContext renderContext,
        IBrush? textForeground = null,
        IBrush? metaForeground = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(inlines);
        ArgumentNullException.ThrowIfNull(renderContext);

        var codeText = new SelectableTextBlock
        {
            FontFamily = MonospaceFamily,
            FontSize = Math.Max(renderContext.FontSize - 1, 12),
            Foreground = textForeground ?? renderContext.Foreground ?? DefaultCodeTextForeground,
            TextWrapping = renderContext.TextWrapping == TextWrapping.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Inlines = inlines
        };

        Control codeContent = renderContext.TextWrapping == TextWrapping.NoWrap
            ? new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = codeText
            }
            : codeText;

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12
        };
        headerGrid.Children.Add(new TextBlock
        {
            Text = FormatLanguageLabel(languageHint),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        var metaTextBlock = new TextBlock
        {
            Text = metaText,
            Foreground = metaForeground ?? DefaultMetaForeground,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(metaTextBlock, 1);
        headerGrid.Children.Add(metaTextBlock);

        var bodyBorder = new Border
        {
            Padding = new Thickness(12, 10),
            Child = codeContent
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var headerBorder = new Border
        {
            Background = HeaderBackground,
            Padding = new Thickness(12, 8),
            Child = headerGrid
        };
        layout.Children.Add(headerBorder);

        Grid.SetRow(bodyBorder, 1);
        layout.Children.Add(bodyBorder);

        var root = new MarkdownRichBlockBorder
        {
            Background = SurfaceBackground,
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = layout
        };

        var lineHitTargets = BuildLineHitTargets(code, block);
        return new MarkdownCodeBlockSurface(
            root,
            request => CreateHitTestResult(request, block, headerBorder, bodyBorder, codeText, lineHitTargets));
    }

    public static string NormalizeCode(string? code)
    {
        return (code ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n');
    }

    public static string NormalizeLanguageHint(string? languageHint)
    {
        if (string.IsNullOrWhiteSpace(languageHint))
        {
            return string.Empty;
        }

        var trimmed = languageHint.Trim();
        var separatorIndex = trimmed.IndexOfAny([' ', '\t', ',', ';', '{', '(']);
        var normalized = separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
        return normalized.Trim().Trim('.').ToLowerInvariant();
    }

    public static string FormatLanguageLabel(string? languageHint)
    {
        return NormalizeLanguageHint(languageHint) switch
        {
            "axaml" => "AXAML",
            "c#" or "cs" or "csharp" => "C#",
            "cpp" or "c++" => "C++",
            "html" or "htm" => "HTML",
            "javascript" or "js" => "JavaScript",
            "json" or "jsonc" => "JSON",
            "markdown" or "md" => "Markdown",
            "powershell" or "ps1" or "pwsh" => "PowerShell",
            "python" or "py" => "Python",
            "rs" or "rust" => "Rust",
            "shell" or "sh" or "bash" => "Shell",
            "sql" => "SQL",
            "ts" or "typescript" => "TypeScript",
            "tsx" => "TSX",
            "xaml" => "XAML",
            "xml" => "XML",
            "yaml" or "yml" => "YAML",
            "text" or "plain" or "plaintext" => "Plain text",
            var other when !string.IsNullOrWhiteSpace(other) => other.ToUpperInvariant(),
            _ => "Plain text"
        };
    }

    private static MarkdownVisualHitTestResult CreateHitTestResult(
        MarkdownVisualHitTestRequest request,
        CodeBlock block,
        Control headerBorder,
        Control bodyBorder,
        SelectableTextBlock codeText,
        IReadOnlyList<CodeLineHitTarget> lineHitTargets)
    {
        if (TryGetLocalPoint(request.Control, request.LocalPoint, headerBorder, out var headerPoint) &&
            new Rect(headerBorder.Bounds.Size).Contains(headerPoint))
        {
            return new MarkdownVisualHitTestResult
            {
                LocalHighlightRects = ResolveLocalControlBounds(headerBorder, request.Control)
            };
        }

        if (TryGetLocalPoint(request.Control, request.LocalPoint, codeText, out var codePoint) &&
            new Rect(codeText.Bounds.Size).Contains(codePoint) &&
            codeText.TextLayout is { } textLayout)
        {
            foreach (var lineHitTarget in lineHitTargets)
            {
                var lineRects = ResolveTextHighlightRects(textLayout, lineHitTarget.RenderedStart, lineHitTarget.RenderedLength);
                if (!ContainsPoint(lineRects, codePoint))
                {
                    continue;
                }

                return new MarkdownVisualHitTestResult
                {
                    SourceSpan = lineHitTarget.SourceSpan ?? MarkdownSourceSpan.FromMarkdig(block.Span),
                    LocalHighlightRects = TranslateLocalRects(codeText, request.Control, lineRects)
                };
            }
        }

        if (TryGetLocalPoint(request.Control, request.LocalPoint, bodyBorder, out var bodyPoint) &&
            new Rect(bodyBorder.Bounds.Size).Contains(bodyPoint))
        {
            return new MarkdownVisualHitTestResult
            {
                LocalHighlightRects = ResolveLocalControlBounds(bodyBorder, request.Control)
            };
        }

        return new MarkdownVisualHitTestResult
        {
            SourceSpan = MarkdownSourceSpan.FromMarkdig(block.Span),
            LocalHighlightRects = ResolveLocalControlBounds(request.Control, request.Control)
        };
    }

    private static List<CodeLineHitTarget> BuildLineHitTargets(string code, CodeBlock block)
    {
        var normalizedLines = code.Split('\n', StringSplitOptions.None);
        var lineHitTargets = new List<CodeLineHitTarget>(normalizedLines.Length);
        var renderedStart = 0;

        for (var index = 0; index < normalizedLines.Length; index++)
        {
            MarkdownSourceSpan? sourceSpan = null;
            if (index < block.Lines.Count)
            {
                var line = block.Lines.Lines[index];
                if (line.Position >= 0 && line.Slice.Length > 0)
                {
                    sourceSpan = new MarkdownSourceSpan(line.Position, line.Slice.Length);
                }
            }

            var renderedLength = normalizedLines[index].Length;
            lineHitTargets.Add(new CodeLineHitTarget(renderedStart, renderedLength, sourceSpan));
            renderedStart += renderedLength;
            if (index < normalizedLines.Length - 1)
            {
                renderedStart++;
            }
        }

        return lineHitTargets;
    }

    private static IReadOnlyList<Rect> ResolveTextHighlightRects(TextLayout textLayout, int start, int length)
    {
        if (length > 0)
        {
            return textLayout.HitTestTextRange(start, length).ToArray();
        }

        return start < 0
            ? Array.Empty<Rect>()
            : [textLayout.HitTestTextPosition(start)];
    }

    private static IReadOnlyList<Rect> ResolveLocalControlBounds(Control control, Control root)
    {
        if (control.TranslatePoint(default, root) is not { } topLeft)
        {
            return Array.Empty<Rect>();
        }

        return [new Rect(topLeft, control.Bounds.Size)];
    }

    private static IReadOnlyList<Rect> TranslateLocalRects(Control source, Control root, IReadOnlyList<Rect> rects)
    {
        var translatedRects = new List<Rect>(rects.Count);
        foreach (var rect in rects)
        {
            if (source.TranslatePoint(rect.TopLeft, root) is not { } topLeft)
            {
                continue;
            }

            translatedRects.Add(new Rect(topLeft, rect.Size));
        }

        return translatedRects;
    }

    private static bool TryGetLocalPoint(Control source, Point sourcePoint, Control target, out Point targetPoint)
    {
        if (source.TranslatePoint(sourcePoint, target) is { } translatedPoint)
        {
            targetPoint = translatedPoint;
            return true;
        }

        targetPoint = default;
        return false;
    }

    private static bool ContainsPoint(IReadOnlyList<Rect> rects, Point point)
    {
        for (var index = 0; index < rects.Count; index++)
        {
            if (rects[index].Inflate(1).Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record CodeLineHitTarget(int RenderedStart, int RenderedLength, MarkdownSourceSpan? SourceSpan);
}

public sealed record MarkdownCodeBlockSurface(Control Control, MarkdownVisualHitTestHandler? HitTestHandler);
