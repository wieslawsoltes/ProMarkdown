using System.Runtime.ExceptionServices;
using Avalonia.Threading;

namespace ProMarkdown.Services;

internal static class MarkdownAsyncExceptionBoundary
{
    public static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not AccessViolationException;

    public static void ReportNonRecoverable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var dispatchInfo = ExceptionDispatchInfo.Capture(exception);
        Dispatcher.UIThread.Post(dispatchInfo.Throw, DispatcherPriority.Send);
    }
}
