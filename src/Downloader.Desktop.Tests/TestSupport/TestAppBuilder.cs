using Avalonia;
using Avalonia.Headless;
using Downloader.Desktop;
using Downloader.Desktop.Tests;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(TestAppBuilder))]

// Run test collections SEQUENTIALLY. Parallel collections intermittently killed the shared Avalonia
// headless dispatcher thread mid-run (hang dump 2026-07-17: 8 workers from 8 classes all blocked in
// AvaloniaTestCase.Run with NO dispatcher thread left → every later [AvaloniaFact] waited forever).
// The suite's classes race on shared statics (ShutdownService, LocalApiService, DialogHelper.MainWindow,
// Localizer, NotificationService) and every AvaloniaFact serializes through the one dispatcher anyway,
// so parallelism bought nothing (suite runs in seconds) while causing the intermittent freeze.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

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
