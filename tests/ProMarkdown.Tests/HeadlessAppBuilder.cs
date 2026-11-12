using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: AvaloniaTestApplication(typeof(ProMarkdown.Tests.HeadlessAppBuilder))]
[assembly: AvaloniaTestFramework]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly)]
[assembly: Parallelization(Mode = ParallelMode.Collections, MaxThreads = 1)]

namespace ProMarkdown.Tests;

public sealed class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true
            });
}
