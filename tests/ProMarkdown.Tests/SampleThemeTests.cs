using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using ProMarkdown.Controls;
using ProMarkdown.Sample.ViewModels;
using ProMarkdown.Services;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class SampleThemeTests
{
    [Fact]
    public void ThemeStateNotifiesBindingsOnlyWhenTheModeChanges()
    {
        var viewModel = new MainWindowViewModel();
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        viewModel.IsDarkTheme = false;
        notifications.ShouldBeEmpty();
        viewModel.IsDarkTheme = true;
        notifications.ShouldBe(new[] { "IsDarkTheme", "RequestedThemeVariant", "Palette", "ThemeLabel" });
        viewModel.RequestedThemeVariant.ShouldBe(ThemeVariant.Dark);
        viewModel.Palette.ShouldBeSameAs(MarkdownThemePalette.Dark);
    }

    [AvaloniaFact]
    public async Task ThemeBindingsUpdateTheWindowAndMarkdownPaletteInBothDirections()
    {
        var viewModel = new MainWindowViewModel();
        var preview = new MarkdownTextBlock { Markdown = "# Theme preview\n\n**Content** stays intact." };
        var toggle = new ToggleButton();
        var window = new Window
        {
            DataContext = viewModel,
            Content = new StackPanel { Children = { toggle, preview } }
        };
        var theme = new FluentTheme();
        Application.Current!.Styles.Add(theme);
        window.Bind(Window.RequestedThemeVariantProperty, new Binding(nameof(viewModel.RequestedThemeVariant)));
        toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(viewModel.IsDarkTheme)) { Mode = BindingMode.TwoWay });
        preview.Bind(MarkdownTextBlock.ThemePaletteProperty, new Binding(nameof(viewModel.Palette)));
        window.Show();
        try
        {
            foreach (var dark in new[] { true, false, true })
            {
                toggle.IsChecked = dark;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                window.ActualThemeVariant.ShouldBe(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                preview.ThemePalette.ShouldBeSameAs(dark ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light);
                preview.Markdown.ShouldBe("# Theme preview\n\n**Content** stays intact.");
                viewModel.IsDarkTheme.ShouldBe(dark);
            }
        }
        finally
        {
            window.Close();
            Application.Current!.Styles.Remove(theme);
        }
    }
}
