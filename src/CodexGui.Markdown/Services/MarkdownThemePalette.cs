using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CodexGui.Markdown.Services;

/// <summary>
/// Defines the semantic brushes used while rendering a Markdown document.
/// </summary>
public sealed class MarkdownThemePalette
{
    /// <summary>Gets the built-in light Markdown palette.</summary>
    public static MarkdownThemePalette Light { get; } = CreateLight();

    /// <summary>Gets the built-in dark Markdown palette.</summary>
    public static MarkdownThemePalette Dark { get; } = CreateDark();

    /// <summary>Gets or initializes whether this palette represents a dark theme.</summary>
    public bool IsDark { get; init; }

    /// <summary>Gets or initializes the primary content foreground.</summary>
    public IBrush Foreground { get; init; } = Brush("#1F2328");

    /// <summary>Gets or initializes the secondary content foreground.</summary>
    public IBrush MutedForeground { get; init; } = Brush("#57606A");

    /// <summary>Gets or initializes the application accent used for semantic emphasis.</summary>
    public IBrush Accent { get; init; } = Brush("#0A56C2");

    /// <summary>Gets or initializes the hyperlink foreground.</summary>
    public IBrush HyperlinkForeground { get; init; } = Brush("#0A56C2");

    /// <summary>Gets or initializes the document surface.</summary>
    public IBrush Surface { get; init; } = Brush("#FFFFFF");

    /// <summary>Gets or initializes raised container surfaces.</summary>
    public IBrush SurfaceRaised { get; init; } = Brush("#F6F8FA");

    /// <summary>Gets or initializes the common border brush.</summary>
    public IBrush Border { get; init; } = Brush("#D0D7DE");

    /// <summary>Gets or initializes the block quote rail brush.</summary>
    public IBrush QuoteBorder { get; init; } = Brush("#D8DEE4");

    /// <summary>Gets or initializes inline-code and marked-text backgrounds.</summary>
    public IBrush InlineCodeBackground { get; init; } = Brush("#EEF1F5");

    /// <summary>Gets or initializes code block header surfaces.</summary>
    public IBrush CodeHeaderBackground { get; init; } = Brush("#EAEEF2");

    /// <summary>Gets or initializes table header surfaces.</summary>
    public IBrush TableHeaderBackground { get; init; } = Brush("#F3F4F6");

    /// <summary>Gets or initializes alternating table row surfaces.</summary>
    public IBrush TableAlternateRowBackground { get; init; } = Brush("#FBFCFD");

    /// <summary>Gets or initializes marked-text backgrounds.</summary>
    public IBrush MarkedTextBackground { get; init; } = Brush("#FFF1B8");

    /// <summary>Gets or initializes inserted-text foreground.</summary>
    public IBrush InsertedTextForeground { get; init; } = Brush("#116329");

    /// <summary>Gets or initializes the Note alert accent.</summary>
    public IBrush NoteAccent { get; init; } = Brush("#2563EB");

    /// <summary>Gets or initializes the Note alert background.</summary>
    public IBrush NoteBackground { get; init; } = Brush("#EFF6FF");

    /// <summary>Gets or initializes the Tip alert accent.</summary>
    public IBrush TipAccent { get; init; } = Brush("#059669");

    /// <summary>Gets or initializes the Tip alert background.</summary>
    public IBrush TipBackground { get; init; } = Brush("#ECFDF5");

    /// <summary>Gets or initializes the Important alert accent.</summary>
    public IBrush ImportantAccent { get; init; } = Brush("#7C3AED");

    /// <summary>Gets or initializes the Important alert background.</summary>
    public IBrush ImportantBackground { get; init; } = Brush("#F5F3FF");

    /// <summary>Gets or initializes the Warning alert accent.</summary>
    public IBrush WarningAccent { get; init; } = Brush("#D97706");

    /// <summary>Gets or initializes the Warning alert background.</summary>
    public IBrush WarningBackground { get; init; } = Brush("#FFFBEB");

    /// <summary>Gets or initializes the Caution alert accent.</summary>
    public IBrush CautionAccent { get; init; } = Brush("#DC2626");

    /// <summary>Gets or initializes the Caution alert background.</summary>
    public IBrush CautionBackground { get; init; } = Brush("#FEF2F2");

    /// <summary>Gets or initializes the syntax keyword foreground.</summary>
    public IBrush CodeKeywordForeground { get; init; } = Brush("#CF222E");

    /// <summary>Gets or initializes the syntax type foreground.</summary>
    public IBrush CodeTypeForeground { get; init; } = Brush("#8250DF");

    /// <summary>Gets or initializes the syntax string foreground.</summary>
    public IBrush CodeStringForeground { get; init; } = Brush("#0A3069");

    /// <summary>Gets or initializes the syntax comment foreground.</summary>
    public IBrush CodeCommentForeground { get; init; } = Brush("#6E7781");

    /// <summary>Gets or initializes the syntax number foreground.</summary>
    public IBrush CodeNumberForeground { get; init; } = Brush("#0550AE");

    /// <summary>Gets or initializes the syntax property foreground.</summary>
    public IBrush CodePropertyForeground { get; init; } = Brush("#953800");

    /// <summary>Gets or initializes the syntax tag foreground.</summary>
    public IBrush CodeTagForeground { get; init; } = Brush("#1A7F37");

    /// <summary>Gets or initializes the syntax attribute foreground.</summary>
    public IBrush CodeAttributeForeground { get; init; } = Brush("#9A6700");

    /// <summary>Gets or initializes the syntax punctuation foreground.</summary>
    public IBrush CodePunctuationForeground { get; init; } = Brush("#57606A");

    /// <summary>Returns the built-in palette appropriate for the supplied foreground.</summary>
    public static MarkdownThemePalette Resolve(IBrush? foreground) =>
        IsLightForeground(foreground) ? Dark : Light;

    private static MarkdownThemePalette CreateLight() => new();

    private static MarkdownThemePalette CreateDark() => new()
    {
        IsDark = true,
        Foreground = Brush("#E6EDF3"),
        MutedForeground = Brush("#AAB1BB"),
        Accent = Brush("#58A6FF"),
        HyperlinkForeground = Brush("#58A6FF"),
        Surface = Brush("#202327"),
        SurfaceRaised = Brush("#292D33"),
        Border = Brush("#454A52"),
        QuoteBorder = Brush("#56606B"),
        InlineCodeBackground = Brush("#30343B"),
        CodeHeaderBackground = Brush("#292D33"),
        TableHeaderBackground = Brush("#292D33"),
        TableAlternateRowBackground = Brush("#25282D"),
        MarkedTextBackground = Brush("#5A4B00"),
        InsertedTextForeground = Brush("#7EE787"),
        NoteAccent = Brush("#60A5FA"),
        NoteBackground = Brush("#1E2A3A"),
        TipAccent = Brush("#34D399"),
        TipBackground = Brush("#1D302A"),
        ImportantAccent = Brush("#A78BFA"),
        ImportantBackground = Brush("#27243A"),
        WarningAccent = Brush("#FBBF24"),
        WarningBackground = Brush("#342B18"),
        CautionAccent = Brush("#FF5A5F"),
        CautionBackground = Brush("#3A2024"),
        CodeKeywordForeground = Brush("#FF7B72"),
        CodeTypeForeground = Brush("#D2A8FF"),
        CodeStringForeground = Brush("#A5D6FF"),
        CodeCommentForeground = Brush("#8B949E"),
        CodeNumberForeground = Brush("#79C0FF"),
        CodePropertyForeground = Brush("#FFA657"),
        CodeTagForeground = Brush("#7EE787"),
        CodeAttributeForeground = Brush("#E3B341"),
        CodePunctuationForeground = Brush("#C9D1D9")
    };

    private static bool IsLightForeground(IBrush? brush)
    {
        if (brush is not ISolidColorBrush solid)
            return false;

        var color = solid.Color;
        var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / byte.MaxValue;
        return luminance >= 0.55;
    }

    private static IBrush Brush(string color) => new ImmutableSolidColorBrush(Color.Parse(color));
}
