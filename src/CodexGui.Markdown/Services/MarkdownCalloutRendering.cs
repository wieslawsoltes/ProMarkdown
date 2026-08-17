using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexGui.Markdown.Services;

public readonly record struct MarkdownCalloutPresentation(string Title, IBrush AccentBrush, IBrush Background);

public static class MarkdownCalloutRendering
{
    private static readonly IBrush SurfaceBorderBrush = new SolidColorBrush(Color.Parse("#D0D7DE"));
    private static readonly IBrush SubtitleBrush = new SolidColorBrush(Color.Parse("#6E6E6E"));
    private static readonly IBrush NeutralCalloutAccentBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush NeutralCalloutBackground = new SolidColorBrush(Color.Parse("#F8FAFC"));
    private static readonly IBrush NoteAccentBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush NoteBackground = new SolidColorBrush(Color.Parse("#EFF6FF"));
    private static readonly IBrush SuccessAccentBrush = new SolidColorBrush(Color.Parse("#059669"));
    private static readonly IBrush SuccessBackground = new SolidColorBrush(Color.Parse("#ECFDF5"));
    private static readonly IBrush WarningAccentBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush WarningBackground = new SolidColorBrush(Color.Parse("#FFFBEB"));
    private static readonly IBrush DangerAccentBrush = new SolidColorBrush(Color.Parse("#DC2626"));
    private static readonly IBrush DangerBackground = new SolidColorBrush(Color.Parse("#FEF2F2"));
    private static readonly IBrush ImportantAccentBrush = new SolidColorBrush(Color.Parse("#7C3AED"));
    private static readonly IBrush ImportantBackground = new SolidColorBrush(Color.Parse("#F5F3FF"));

    public static Control CreateCalloutSurface(
        string title,
        string? subtitle,
        Control body,
        IBrush accentBrush,
        IBrush background)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(accentBrush);
        ArgumentNullException.ThrowIfNull(background);

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
                Foreground = SubtitleBrush,
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
            BorderBrush = SurfaceBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = layout
        };
    }

    public static MarkdownCalloutPresentation ResolvePresentation(string? kind, string fallbackTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackTitle);

        var normalizedKind = kind?.Trim().ToLowerInvariant();
        var title = string.IsNullOrWhiteSpace(kind) ? fallbackTitle : FormatLabel(kind);

        return normalizedKind switch
        {
            "caution" => new MarkdownCalloutPresentation(title, DangerAccentBrush, DangerBackground),
            "warning" => new MarkdownCalloutPresentation(title, WarningAccentBrush, WarningBackground),
            "danger" or "error" => new MarkdownCalloutPresentation(title, DangerAccentBrush, DangerBackground),
            "important" => new MarkdownCalloutPresentation(title, ImportantAccentBrush, ImportantBackground),
            "success" or "tip" => new MarkdownCalloutPresentation(title, SuccessAccentBrush, SuccessBackground),
            "info" or "note" => new MarkdownCalloutPresentation(title, NoteAccentBrush, NoteBackground),
            _ => new MarkdownCalloutPresentation(title, NeutralCalloutAccentBrush, NeutralCalloutBackground)
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
