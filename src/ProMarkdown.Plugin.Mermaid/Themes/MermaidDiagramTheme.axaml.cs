using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace ProMarkdown.Plugin.Mermaid.Themes;

internal sealed partial class MermaidDiagramTheme : ControlTheme
{
    public MermaidDiagramTheme()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
