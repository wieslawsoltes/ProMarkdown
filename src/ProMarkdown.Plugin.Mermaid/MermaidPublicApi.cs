using Avalonia.Media;
using Avalonia.Media.Immutable;
using ProMarkdown.Services;

namespace ProMarkdown.Plugin.Mermaid;

/// <summary>Describes one themed Mermaid SVG render request.</summary>
public sealed record MermaidSvgRenderRequest
{
    /// <summary>Initializes a themed Mermaid SVG render request.</summary>
    public MermaidSvgRenderRequest(
        string source,
        MarkdownThemePalette palette,
        string fontFamily,
        double fontSize)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Palette = MermaidThemePaletteSnapshot.Create(
            palette ?? throw new ArgumentNullException(nameof(palette)));
        FontFamily = fontFamily ?? throw new ArgumentNullException(nameof(fontFamily));
        FontSize = fontSize;
    }

    /// <summary>Gets the Mermaid source.</summary>
    public string Source { get; }

    /// <summary>Gets an immutable, background-thread-safe snapshot of the render palette.</summary>
    public MarkdownThemePalette Palette { get; }

    /// <summary>Gets the requested diagram font family.</summary>
    public string FontFamily { get; }

    /// <summary>Gets the requested diagram font size.</summary>
    public double FontSize { get; }
}

/// <summary>Renders Mermaid source into a sanitized-ready SVG document.</summary>
public interface IMermaidSvgRenderer
{
    Task<string> RenderAsync(MermaidSvgRenderRequest request, CancellationToken cancellationToken);
}

/// <summary>Configures Mermaid UI text and safe-link activation.</summary>
public sealed record MermaidMarkdownPluginOptions
{
    /// <summary>Gets the accessible name announced for a rendered Mermaid diagram.</summary>
    public string AccessibleName { get; init; } = "Mermaid diagram";

    public string ErrorText { get; init; } = "Mermaid diagram could not be rendered.";

    public string RetryText { get; init; } = "Retry";

    public Func<Uri, CancellationToken, Task>? ActivateLinkAsync { get; init; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AccessibleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ErrorText);
        ArgumentException.ThrowIfNullOrWhiteSpace(RetryText);
    }
}

internal static class MermaidThemePaletteSnapshot
{
    public static MarkdownThemePalette Create(MarkdownThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var fallback = palette.IsDark ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light;
        return new MarkdownThemePalette
        {
            IsDark = palette.IsDark,
            Foreground = SnapshotBrush(palette.Foreground, fallback.Foreground),
            MutedForeground = SnapshotBrush(palette.MutedForeground, fallback.MutedForeground),
            Accent = SnapshotBrush(palette.Accent, fallback.Accent),
            HyperlinkForeground = SnapshotBrush(palette.HyperlinkForeground, fallback.HyperlinkForeground),
            Surface = SnapshotBrush(palette.Surface, fallback.Surface),
            SurfaceRaised = SnapshotBrush(palette.SurfaceRaised, fallback.SurfaceRaised),
            Border = SnapshotBrush(palette.Border, fallback.Border),
            QuoteBorder = SnapshotBrush(palette.QuoteBorder, fallback.QuoteBorder),
            InlineCodeBackground = SnapshotBrush(palette.InlineCodeBackground, fallback.InlineCodeBackground),
            CodeHeaderBackground = SnapshotBrush(palette.CodeHeaderBackground, fallback.CodeHeaderBackground),
            TableHeaderBackground = SnapshotBrush(palette.TableHeaderBackground, fallback.TableHeaderBackground),
            TableAlternateRowBackground = SnapshotBrush(
                palette.TableAlternateRowBackground,
                fallback.TableAlternateRowBackground),
            MarkedTextBackground = SnapshotBrush(palette.MarkedTextBackground, fallback.MarkedTextBackground),
            InsertedTextForeground = SnapshotBrush(palette.InsertedTextForeground, fallback.InsertedTextForeground),
            NoteAccent = SnapshotBrush(palette.NoteAccent, fallback.NoteAccent),
            NoteBackground = SnapshotBrush(palette.NoteBackground, fallback.NoteBackground),
            TipAccent = SnapshotBrush(palette.TipAccent, fallback.TipAccent),
            TipBackground = SnapshotBrush(palette.TipBackground, fallback.TipBackground),
            ImportantAccent = SnapshotBrush(palette.ImportantAccent, fallback.ImportantAccent),
            ImportantBackground = SnapshotBrush(palette.ImportantBackground, fallback.ImportantBackground),
            WarningAccent = SnapshotBrush(palette.WarningAccent, fallback.WarningAccent),
            WarningBackground = SnapshotBrush(palette.WarningBackground, fallback.WarningBackground),
            CautionAccent = SnapshotBrush(palette.CautionAccent, fallback.CautionAccent),
            CautionBackground = SnapshotBrush(palette.CautionBackground, fallback.CautionBackground),
            CodeKeywordForeground = SnapshotBrush(palette.CodeKeywordForeground, fallback.CodeKeywordForeground),
            CodeTypeForeground = SnapshotBrush(palette.CodeTypeForeground, fallback.CodeTypeForeground),
            CodeStringForeground = SnapshotBrush(palette.CodeStringForeground, fallback.CodeStringForeground),
            CodeCommentForeground = SnapshotBrush(palette.CodeCommentForeground, fallback.CodeCommentForeground),
            CodeNumberForeground = SnapshotBrush(palette.CodeNumberForeground, fallback.CodeNumberForeground),
            CodePropertyForeground = SnapshotBrush(palette.CodePropertyForeground, fallback.CodePropertyForeground),
            CodeTagForeground = SnapshotBrush(palette.CodeTagForeground, fallback.CodeTagForeground),
            CodeAttributeForeground = SnapshotBrush(palette.CodeAttributeForeground, fallback.CodeAttributeForeground),
            CodePunctuationForeground = SnapshotBrush(
                palette.CodePunctuationForeground,
                fallback.CodePunctuationForeground)
        };
    }

    private static IBrush SnapshotBrush(IBrush? brush, IBrush fallback)
    {
        if (brush is ImmutableSolidColorBrush immutableBrush)
            return immutableBrush;
        if (brush is ISolidColorBrush solidBrush)
            return new ImmutableSolidColorBrush(solidBrush.Color);
        if (fallback is ImmutableSolidColorBrush immutableFallback)
            return immutableFallback;
        if (fallback is ISolidColorBrush solidFallback)
            return new ImmutableSolidColorBrush(solidFallback.Color);

        throw new InvalidOperationException("Markdown theme palette brushes must resolve to solid colors.");
    }
}
