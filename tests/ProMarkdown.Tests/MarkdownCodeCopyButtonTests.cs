using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using ProMarkdown.Controls;
using Shouldly;

namespace ProMarkdown.Tests;

public sealed class MarkdownCodeCopyButtonTests
{
    [AvaloniaFact]
    public async Task CodeBlockCopiesItsCodeAndExposesAccessibleFeedback()
    {
        const string code = "dotnet run\n--project sample";
        var markdown = new MarkdownTextBlock { Markdown = $"```shell\n{code}\n```" };
        var window = new Window { Width = 600, Height = 240, Content = markdown };
        window.Show();
        try
        {
            var button = markdown.GetVisualDescendants().OfType<MarkdownCodeCopyButton>().Single();
            var peer = new ButtonAutomationPeer(button);
            peer.GetName().ShouldBe("Copy code");
            peer.GetLiveSetting().ShouldBe(AutomationLiveSetting.Polite);
            button.Focusable.ShouldBeTrue();

            await button.CopyAsync();

            var clipboard = window.Clipboard.ShouldNotBeNull();
            (await clipboard.TryGetTextAsync()).ShouldBe(code);
            peer.GetName().ShouldBe("Code copied");
            ToolTip.GetTip(button).ShouldBe("Code copied");

            window.Content = null;
            peer.GetName().ShouldBe("Copy code");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RecoverableClipboardFailureDoesNotEscapeOrShowSuccess()
    {
        var attempts = 0;
        var button = new MarkdownCodeCopyButton(
            "code",
            _ =>
            {
                attempts++;
                return Task.FromException<bool>(new ExternalException("Clipboard is busy."));
            });
        var window = new Window { Content = button };
        window.Show();
        try
        {
            await button.CopyAsync();
            await button.CopyAsync();

            attempts.ShouldBe(2);
            new ButtonAutomationPeer(button).GetName().ShouldBe("Copy code");
            ToolTip.GetTip(button).ShouldBe("Copy code");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SuccessfulCopyFeedbackReturnsToTheCopyAction()
    {
        var button = new MarkdownCodeCopyButton("code", _ => Task.FromResult(true));
        var window = new Window { Content = button };
        window.Show();
        try
        {
            await button.CopyAsync();
            new ButtonAutomationPeer(button).GetName().ShouldBe("Code copied");
            var reset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == AutomationProperties.NameProperty &&
                    AutomationProperties.GetName(button) == "Copy code")
                    reset.TrySetResult();
            }

            button.PropertyChanged += OnPropertyChanged;
            try
            {
                await reset.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
                ToolTip.GetTip(button).ShouldBe("Copy code");
            }
            finally
            {
                button.PropertyChanged -= OnPropertyChanged;
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task FatalClipboardFailureIsNotSwallowed()
    {
        var button = new MarkdownCodeCopyButton(
            "code",
            _ => Task.FromException<bool>(new OutOfMemoryException("Fatal clipboard failure.")));

        await Should.ThrowAsync<OutOfMemoryException>(() => button.CopyAsync());
    }

    [AvaloniaFact]
    public async Task PendingCopyDoesNotOverlapOrUpdateADetachedButton()
    {
        var completion = new TaskCompletionSource<bool>();
        var copyCount = 0;
        var button = new MarkdownCodeCopyButton("code", _ =>
        {
            copyCount++;
            return completion.Task;
        });
        var window = new Window { Content = button };
        window.Show();
        try
        {
            var pendingCopy = button.CopyAsync();
            await button.CopyAsync();
            copyCount.ShouldBe(1);

            window.Content = null;
            completion.SetResult(true);
            await pendingCopy;

            new ButtonAutomationPeer(button).GetName().ShouldBe("Copy code");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MissingClipboardDoesNotShowSuccess()
    {
        var button = new MarkdownCodeCopyButton("code");

        await button.CopyAsync();

        new ButtonAutomationPeer(button).GetName().ShouldBe("Copy code");
    }
}
