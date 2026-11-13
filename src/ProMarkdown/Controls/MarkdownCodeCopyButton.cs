using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Threading;
using ProMarkdown.Services;

namespace ProMarkdown.Controls;

internal sealed class MarkdownCodeCopyButton : Button
{
    private const string CopyLabel = "Copy code";
    private const string CopiedLabel = "Code copied";
    private readonly string _code;
    private readonly Func<string, Task<bool>> _copyTextAsync;
    private readonly PathIcon _icon;
    private readonly DispatcherTimer _feedbackTimer;
    private bool _isCopying;
    private int _attachmentGeneration;

    internal MarkdownCodeCopyButton(string code, Func<string, Task<bool>>? copyTextAsync = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        _code = code;
        _copyTextAsync = copyTextAsync ?? CopyToClipboardAsync;
        _icon = new PathIcon
        {
            Width = MarkdownCodeCopyIcons.Size,
            Height = MarkdownCodeCopyIcons.Size,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _icon.Bind(ForegroundProperty, this.GetObservable(ForegroundProperty));
        Content = _icon;
        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _feedbackTimer.Tick += OnFeedbackTimerTick;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        SetFeedback(false);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override async void OnClick()
    {
        base.OnClick();
        await CopyAsync();
    }

    internal async Task CopyAsync()
    {
        if (_isCopying)
            return;

        _isCopying = true;
        var attachmentGeneration = _attachmentGeneration;
        _feedbackTimer.Stop();
        SetFeedback(false);
        try
        {
            if (!await _copyTextAsync(_code) || attachmentGeneration != _attachmentGeneration)
                return;

            SetFeedback(true);
            _feedbackTimer.Start();
        }
        catch (Exception exception) when (MarkdownAsyncExceptionBoundary.IsRecoverable(exception))
        {
            Trace.TraceWarning("The Markdown code block could not be copied: {0}", exception);
        }
        finally
        {
            _isCopying = false;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attachmentGeneration = unchecked(_attachmentGeneration + 1);
        _feedbackTimer.Stop();
        SetFeedback(false);
        base.OnDetachedFromVisualTree(e);
    }

    private async Task<bool> CopyToClipboardAsync(string code)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return false;

        await clipboard.SetTextAsync(code);
        return true;
    }

    private void OnFeedbackTimerTick(object? sender, EventArgs args)
    {
        _feedbackTimer.Stop();
        SetFeedback(false);
    }

    private void SetFeedback(bool copied)
    {
        var label = copied ? CopiedLabel : CopyLabel;
        _icon.Data = copied ? MarkdownCodeCopyIcons.Checkmark : MarkdownCodeCopyIcons.Copy;
        AutomationProperties.SetName(this, label);
        ToolTip.SetTip(this, label);
    }
}
