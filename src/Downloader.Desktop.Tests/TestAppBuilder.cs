using Avalonia;
using Avalonia.Headless;
using Downloader.Desktop.Tests;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Downloader.Desktop.Tests;

/// <summary>Minimal headless Avalonia application used to host the UI/logic tests.</summary>
public class TestApp : Application
{
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
