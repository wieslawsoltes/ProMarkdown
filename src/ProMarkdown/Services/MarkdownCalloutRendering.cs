using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ProMarkdown.Services;

public readonly record struct MarkdownCalloutPresentation(string Title, IBrush AccentBrush, IBrush Background);

public static class MarkdownCalloutRendering
{
    public static Control CreateCalloutSurface(
        string title,
        string? subtitle,
        Control body,
        IBrush accentBrush,
        IBrush background) =>
        CreateCalloutSurface(title, subtitle, body, accentBrush, background, MarkdownThemePalette.Light);

    public static Control CreateCalloutSurface(
        string title,
        string? subtitle,
        Control body,
        IBrush accentBrush,
        IBrush background,
        MarkdownThemePalette palette)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(accentBrush);
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(palette);

        var contentPanel = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12)
        };

        contentPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Foreground = accentBrush,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            contentPanel.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                Foreground = palette.MutedForeground,
                TextWrapping = TextWrapping.Wrap
            });
        }

        contentPanel.Children.Add(body);

        var layout = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };

        layout.Children.Add(new Border
        {
            Width = 4,
            Background = accentBrush
        });

        Grid.SetColumn(contentPanel, 1);
        layout.Children.Add(contentPanel);

        return new Border
        {
            Background = background,
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = layout
        };
    }

    public static MarkdownCalloutPresentation ResolvePresentation(string? kind, string fallbackTitle) =>
        ResolvePresentation(kind, fallbackTitle, MarkdownThemePalette.Light);

    public static MarkdownCalloutPresentation ResolvePresentation(
        string? kind,
        string fallbackTitle,
        MarkdownThemePalette palette)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackTitle);
        ArgumentNullException.ThrowIfNull(palette);

        var normalizedKind = kind?.Trim().ToLowerInvariant();
        var title = string.IsNullOrWhiteSpace(kind) ? fallbackTitle : FormatLabel(kind);

        return normalizedKind switch
        {
            "caution" => new MarkdownCalloutPresentation(title, palette.CautionAccent, palette.CautionBackground),
            "warning" => new MarkdownCalloutPresentation(title, palette.WarningAccent, palette.WarningBackground),
            "danger" or "error" => new MarkdownCalloutPresentation(title, palette.CautionAccent, palette.CautionBackground),
            "important" => new MarkdownCalloutPresentation(title, palette.ImportantAccent, palette.ImportantBackground),
            "success" or "tip" => new MarkdownCalloutPresentation(title, palette.TipAccent, palette.TipBackground),
            "info" or "note" => new MarkdownCalloutPresentation(title, palette.NoteAccent, palette.NoteBackground),
            _ => new MarkdownCalloutPresentation(title, palette.MutedForeground, palette.SurfaceRaised)
        };
    }

    public static string FormatLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Trim()
            .Replace('-', ' ')
            .Replace('_', ' ');

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant());
    }
}
