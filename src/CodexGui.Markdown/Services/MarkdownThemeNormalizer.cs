using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using CodexGui.Markdown.Controls;

namespace CodexGui.Markdown.Services;

internal static class MarkdownThemeNormalizer
{
    public static void Apply(InlineCollection inlines, MarkdownThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        ArgumentNullException.ThrowIfNull(palette);
        foreach (var inline in inlines)
            NormalizeInline(inline, palette);
    }

    private static void NormalizeInline(Inline inline, MarkdownThemePalette palette)
    {
        if (inline is TextElement text)
        {
            text.SetCurrentValue(TextElement.ForegroundProperty, MapForeground(text.Foreground, palette));
            text.SetCurrentValue(TextElement.BackgroundProperty, MapBackground(text.Background, palette));
        }

        if (inline is Span span)
        {
            foreach (var nestedInline in span.Inlines)
                NormalizeInline(nestedInline, palette);
        }

        if (inline is InlineUIContainer { Child: { } child })
            NormalizeControl(child, palette);
    }

    private static void NormalizeControl(Control control, MarkdownThemePalette palette)
    {
        if (control is MarkdownTextBlock markdown)
        {
            if (markdown.ThemePalette is null)
                markdown.SetCurrentValue(MarkdownTextBlock.ThemePaletteProperty, palette);

            return;
        }

        if (control is TextBlock text)
        {
            text.SetCurrentValue(TextBlock.ForegroundProperty, MapForeground(text.Foreground, palette));
            text.SetCurrentValue(TextBlock.BackgroundProperty, MapBackground(text.Background, palette));
            if (text.Inlines is { } inlines)
            {
                foreach (var inline in inlines)
                    NormalizeInline(inline, palette);
            }
        }

        if (control is Border border)
        {
            border.SetCurrentValue(Border.BackgroundProperty, MapBackground(border.Background, palette));
            border.SetCurrentValue(Border.BorderBrushProperty, MapBorder(border.BorderBrush, palette));
        }
        else if (control is Panel panel)
        {
            panel.SetCurrentValue(Panel.BackgroundProperty, MapBackground(panel.Background, palette));
        }
        else if (control is TemplatedControl templated)
        {
            templated.SetCurrentValue(TemplatedControl.BackgroundProperty, MapBackground(templated.Background, palette));
            templated.SetCurrentValue(TemplatedControl.BorderBrushProperty, MapBorder(templated.BorderBrush, palette));
            templated.SetCurrentValue(TemplatedControl.ForegroundProperty, MapForeground(templated.Foreground, palette));
        }

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    NormalizeControl(child, palette);
                break;
            case Decorator { Child: { } child }:
                NormalizeControl(child, palette);
                break;
            case ContentControl { Content: Control child }:
                NormalizeControl(child, palette);
                break;
        }
    }

    private static IBrush? MapForeground(IBrush? brush, MarkdownThemePalette palette)
    {
        if (brush is not ISolidColorBrush solid)
            return brush;

        return solid.Color.ToString().ToUpperInvariant() switch
        {
            "#FF0A56C2" => palette.HyperlinkForeground,
            "#FF6E6E6E" or "#FF6B7280" or "#FF64748B" => palette.MutedForeground,
            "#FF1F2328" or "#FF333333" or "#FF000000" => palette.Foreground,
            "#FF116329" => palette.InsertedTextForeground,
            "#FFCF222E" => palette.CodeKeywordForeground,
            "#FF8250DF" => palette.CodeTypeForeground,
            "#FF0A3069" => palette.CodeStringForeground,
            "#FF0550AE" => palette.CodeNumberForeground,
            "#FF953800" => palette.CodePropertyForeground,
            "#FF1A7F37" => palette.CodeTagForeground,
            "#FF9A6700" => palette.CodeAttributeForeground,
            "#FF57606A" => palette.CodePunctuationForeground,
            "#FF6E7781" => palette.CodeCommentForeground,
            "#FF2563EB" => palette.NoteAccent,
            "#FF059669" => palette.TipAccent,
            "#FF7C3AED" or "#FF4338CA" => palette.ImportantAccent,
            "#FFD97706" => palette.WarningAccent,
            "#FFDC2626" => palette.CautionAccent,
            _ => brush
        };
    }

    private static IBrush? MapBackground(IBrush? brush, MarkdownThemePalette palette)
    {
        if (brush is not ISolidColorBrush solid)
            return brush;

        return solid.Color.ToString().ToUpperInvariant() switch
        {
            "#FFFFFFFF" => palette.Surface,
            "#FFF6F8FA" or "#FFF8FAFC" => palette.SurfaceRaised,
            "#FFEEF1F5" => palette.InlineCodeBackground,
            "#FFEAEEF2" => palette.CodeHeaderBackground,
            "#FFF3F4F6" => palette.TableHeaderBackground,
            "#FFFBFCFD" => palette.TableAlternateRowBackground,
            "#FFFFF1B8" => palette.MarkedTextBackground,
            "#FFEFF6FF" => palette.NoteBackground,
            "#FFECFDF5" => palette.TipBackground,
            "#FFF5F3FF" => palette.ImportantBackground,
            "#FFFFFBEB" => palette.WarningBackground,
            "#FFFEF2F2" => palette.CautionBackground,
            "#FF2563EB" => palette.NoteAccent,
            "#FF059669" => palette.TipAccent,
            "#FF7C3AED" or "#FF4338CA" => palette.ImportantAccent,
            "#FFD97706" => palette.WarningAccent,
            "#FFDC2626" => palette.CautionAccent,
            _ => brush
        };
    }

    private static IBrush? MapBorder(IBrush? brush, MarkdownThemePalette palette)
    {
        if (brush is not ISolidColorBrush solid)
            return brush;

        return solid.Color.ToString().ToUpperInvariant() switch
        {
            "#FFD0D7DE" => palette.Border,
            "#FFD8DEE4" => palette.QuoteBorder,
            _ => brush
        };
    }

}
