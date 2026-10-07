using System.ComponentModel;
using Avalonia.Styling;
using ProMarkdown.Services;

namespace ProMarkdown.Sample.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private bool _isDarkTheme;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (_isDarkTheme == value)
                return;

            _isDarkTheme = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDarkTheme)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RequestedThemeVariant)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Palette)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeLabel)));
        }
    }

    public ThemeVariant RequestedThemeVariant => IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;

    public MarkdownThemePalette Palette => IsDarkTheme ? MarkdownThemePalette.Dark : MarkdownThemePalette.Light;

    public string ThemeLabel => IsDarkTheme ? "Dark mode" : "Light mode";
}
