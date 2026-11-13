using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ProMarkdown.Controls;
using ProMarkdown.Services;
using Shouldly;
using Xunit;

namespace ProMarkdown.Tests;

public sealed class MarkdownLayoutTests
{
    [AvaloniaFact]
    public void EmbeddedCodeRespectsAnArrangeSlotNarrowerThanItsMeasureConstraint()
    {
        var control = new MarkdownTextBlock
        {
            Markdown = "```shell\ndotnet run\n```",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var renderResult = control.LastRenderResult;
        control.Measure(new Size(600, 300));
        control.Arrange(new Rect(0, 0, 200, 300));
        MarkdownDocumentLayout.Flush(control);
        control.Measure(new Size(600, 300));
        control.Arrange(new Rect(0, 0, 200, 300));
        var block = control.Inlines!.OfType<InlineUIContainer>().Single().Child!;
        block.Bounds.Width.ShouldBeLessThanOrEqualTo(control.Bounds.Width,
            $"owner={control.Bounds.Width}, embedded block={block.Bounds.Width}");
        control.IsMeasureValid.ShouldBeTrue();

        control.Arrange(new Rect(0, 0, 600, 300));
        control.Measure(new Size(600, 300));
        control.Arrange(new Rect(0, 0, 600, 300));

        block.Bounds.Width.ShouldBe(600);
        control.IsMeasureValid.ShouldBeTrue();
        control.LastRenderResult.ShouldBeSameAs(renderResult);
    }

    [AvaloniaFact]
    public void SwitchingToNoWrapDoesNotRetainThePreviousMeasureWidth()
    {
        var control = new MarkdownTextBlock
        {
            Width = 600,
            Markdown = "```shell\ndotnet run\n```",
            TextWrapping = TextWrapping.Wrap
        };
        var window = new Window { Width = 800, Height = 300, Content = control };
        window.Show();
        try
        {
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();
            control.TextWrapping = TextWrapping.NoWrap;
            control.Width = 200;
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();
            var scroll = control.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Bounds.Width.ShouldBeLessThanOrEqualTo(control.Bounds.Width,
                $"owner={control.Bounds.Width}, code scroll viewer={scroll.Bounds.Width}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(HorizontalAlignment.Stretch, 200d)]
    [InlineData(HorizontalAlignment.Left, 200d)]
    [InlineData(HorizontalAlignment.Stretch, 150d)]
    public void ArrangeWidthAccountsForMarginPaddingAndMaximumWidth(
        HorizontalAlignment alignment, double maxWidth)
    {
        var control = new MarkdownTextBlock
        {
            Markdown = "```shell\ndotnet run\n```",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = alignment,
            Margin = new Thickness(10, 0, 20, 0),
            Padding = new Thickness(5, 0, 15, 0),
            MaxWidth = maxWidth
        };

        control.Measure(new Size(600, 300));
        control.Arrange(new Rect(0, 0, 200, 300));
        control.Measure(new Size(600, 300));
        control.Arrange(new Rect(0, 0, 200, 300));

        var block = control.Inlines!.OfType<InlineUIContainer>().Single().Child!;
        block.Bounds.Width.ShouldBe(Math.Min(170, maxWidth) - 20);
        control.IsMeasureValid.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void UnboundedNoWrapMeasureClearsThePreviousFiniteConstraint()
    {
        var control = new MarkdownTextBlock
        {
            Markdown = "```shell\ndotnet run\n```",
            TextWrapping = TextWrapping.Wrap
        };
        control.Measure(new Size(200, 300));
        control.Arrange(new Rect(0, 0, 200, 300));

        control.TextWrapping = TextWrapping.NoWrap;
        control.Measure(new Size(double.PositiveInfinity, 300));
        control.Arrange(new Rect(0, 0, 600, 300));
        control.Measure(new Size(double.PositiveInfinity, 300));
        control.Arrange(new Rect(0, 0, 600, 300));

        var block = control.Inlines!.OfType<InlineUIContainer>().Single().Child!;
        block.Bounds.Width.ShouldBe(600);
        control.IsMeasureValid.ShouldBeTrue();
    }
}
