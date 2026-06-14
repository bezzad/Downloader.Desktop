using Avalonia;
using Avalonia.Headless;
using Downloader.Desktop;
using Downloader.Desktop.Tests;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Downloader.Desktop.Tests;

public static class TestAppBuilder
{
    // Use the real App (so all styles/themes load) with Skia drawing enabled, which lets
    // the screenshot capture render actual pixels.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
