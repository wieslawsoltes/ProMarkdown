using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using ProMarkdown.Sample.Controls;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class SampleScrollingTests
{
    [AvaloniaFact]
    public async Task PreviewWheelEasesWholeNotchesAndPassesThroughPreciseInput()
    {
        var scroll = new PreviewScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Height = 1200, Background = Brushes.Transparent },
            Template = new FuncControlTemplate<ScrollViewer>((viewer, scope) =>
                new ScrollContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    Content = viewer.Content,
                    CanVerticallyScroll = true,
                    ClipToBounds = true
                }.RegisterInNameScope(scope))
        };
        var window = new Window { Width = 400, Height = 300, Content = scroll };
        window.Show();
        try
        {
            window.UpdateLayout();
            scroll.ScrollBarMaximum.Y.ShouldBeGreaterThan(200);
            var point = new Point(100, 100);

            window.MouseWheel(point, new Vector(0, -1));
            scroll.Offset.Y.ShouldBeLessThan(50);
            var timeout = Stopwatch.StartNew();
            while (scroll.Offset.Y < 50 && timeout.Elapsed < TimeSpan.FromSeconds(3))
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            scroll.Offset.Y.ShouldBe(50);

            window.MouseWheel(point, new Vector(0, -0.5));
            scroll.Offset.Y.ShouldBe(75);

            scroll.IsScrollInertiaEnabled = false;
            window.MouseWheel(point, new Vector(0, -1));
            scroll.Offset.Y.ShouldBe(125);
        }
        finally
        {
            window.Close();
        }
    }
}
