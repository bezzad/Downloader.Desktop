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

// Build the Avalonia application ONCE for the whole assembly. The default is
// AvaloniaTestIsolationLevel.PerTest, which tears the application down and rebuilds it for EVERY test —
// and that rebuild is what caused the recurring "test host hung / Total tests: Unknown" CI abort.
//
// Root cause, from the hang dump of run 34016675882 (see the project skill for the full chain):
// HeadlessUnitTestSession's per-test path calls EnsureIsolatedApplication() -> AppBuilder.SetupUnsafe()
// -> AvaloniaHeadlessPlatform.Initialize -> new Compositor(...) -> RenderLoop.Add, which occasionally
// fails its own Dispatcher.VerifyAccess with "The calling thread cannot access this object because a
// different thread owns it". That call sits BEFORE the try that completes the test's
// TaskCompletionSource, so the throw both orphaned the test and faulted the session's dispatch loop
// task: the dispatcher was gone and every later test parked for ever, with the abort blaming whichever
// innocent test happened to be next in line.
//
// PerAssembly uses EnsureSharedApplication instead, which runs SetupUnsafe() once per process, so the
// failing call is no longer on the per-test path at all. It also stops rebuilding the whole application
// ~1700 times a run.
[assembly: Avalonia.Headless.AvaloniaTestIsolation(Avalonia.Headless.AvaloniaTestIsolationLevel.PerAssembly)]

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
