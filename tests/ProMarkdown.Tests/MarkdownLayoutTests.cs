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
    [AvaloniaTheory]
    [InlineData(HorizontalAlignment.Left)]
    [InlineData(HorizontalAlignment.Center)]
    [InlineData(HorizontalAlignment.Right)]
    public void StreamingMarkdownCanGrowInsideAnAutoSizedBubbleBorder(HorizontalAlignment alignment)
    {
        var control = new MarkdownTextBlock { Markdown = "A", TextWrapping = TextWrapping.Wrap };
        var bubble = new Border { HorizontalAlignment = alignment, Child = control };
        var window = new Window { Width = 600, Height = 300, Content = bubble };
        window.Show();
        try
        {
            window.UpdateLayout();
            var initialWidth = control.Bounds.Width;
            initialWidth.ShouldBeLessThan(100);

            control.Markdown = "This paragraph must grow when streamed into an autosized chat bubble " +
                "whose Border sizes itself to the content. It must use the parent's measurement offer.";
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            control.Bounds.Width.ShouldBeGreaterThan(initialWidth + 200);
            control.IsMeasureValid.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AutoSizedBubbleCanRecoverAfterReplacingLongContentWithShortContent()
    {
        const string paragraph = "This paragraph must fill the available space inside a left aligned " +
            "bubble border and recover its width after the previous message was much shorter.";
        var control = new MarkdownTextBlock { Markdown = paragraph, TextWrapping = TextWrapping.Wrap };
        var bubble = new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = control };
        var window = new Window { Width = 600, Height = 300, Content = bubble };
        window.Show();
        try
        {
            window.UpdateLayout();
            var originalWidth = control.Bounds.Width;
            control.Markdown = "A";
            window.UpdateLayout();
            control.Bounds.Width.ShouldBeLessThan(100);

            control.Markdown = paragraph;
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            control.Bounds.Width.ShouldBe(originalWidth, 1);
            control.IsMeasureValid.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AutoSizedBubbleGrowsWithANewMeasureOfferWithoutRebuildingTheDocument()
    {
        var control = new MarkdownTextBlock
        {
            Markdown = "This paragraph must grow when its autosized Border is offered more space " +
                "even when the document itself has not changed.",
            TextWrapping = TextWrapping.Wrap
        };
        var bubble = new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = control };
        var host = new Decorator { Width = 200, Child = bubble };
        var window = new Window { Width = 800, Height = 300, Content = host };
        window.Show();
        try
        {
            window.UpdateLayout();
            var originalWidth = control.Bounds.Width;
            var renderResult = control.LastRenderResult;

            host.Width = 600;
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            control.Bounds.Width.ShouldBeGreaterThan(originalWidth + 200);
            control.IsMeasureValid.ShouldBeTrue();
            control.LastRenderResult.ShouldBeSameAs(renderResult);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StreamingCodeAfterShortTextUsesTheAvailableWidthInsideAnAutoSizedBubble()
    {
        var control = new MarkdownTextBlock { Markdown = "A", TextWrapping = TextWrapping.Wrap };
        var bubble = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12),
            Child = control
        };
        var window = new Window { Width = 600, Height = 300, Content = bubble };
        window.Show();
        try
        {
            window.UpdateLayout();
            control.Markdown = "```shell\ndotnet run\n```";
            window.UpdateLayout();
            MarkdownDocumentLayout.Flush(control);
            window.UpdateLayout();

            var block = control.Inlines!.OfType<InlineUIContainer>().Single().Child!;
            block.Bounds.Width.ShouldBeGreaterThan(500);
            block.Bounds.Width.ShouldBeLessThanOrEqualTo(control.Bounds.Width);
            control.IsMeasureValid.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

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
    public void RebuiltCodeKeepsHonoringATighterArrangeSlotWithoutALayoutLoop()
    {
        var control = new MarkdownTextBlock { TextWrapping = TextWrapping.Wrap };
        for (var message = 0; message < 4; message++)
        {
            control.Markdown = $"```shell\ndotnet run --project sample{message}\n```";
            control.Measure(new Size(600, 300));
            control.Arrange(new Rect(0, 0, 200, 300));
            control.Measure(new Size(600, 300));
            control.Arrange(new Rect(0, 0, 200, 300));

            var block = control.Inlines!.OfType<InlineUIContainer>().Single().Child!;
            block.Bounds.Width.ShouldBe(200);
            control.IsMeasureValid.ShouldBeTrue();
            control.IsArrangeValid.ShouldBeTrue();
        }
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
