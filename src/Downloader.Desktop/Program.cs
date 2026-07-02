using Avalonia;
using Avalonia.ReactiveUI;
using System;
using Avalonia.Controls;
using Downloader.Desktop.Services;

namespace Downloader.Desktop;

static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // CLI verbs (add/list/pause/…) run headless and exit — no Avalonia, no single-instance
        // claim (a `list` must not steal the lock or surface the window). Anything that isn't a
        // recognized verb (bare URL, --minimized, --cli-add) is a normal GUI launch.
        if (CliParser.TryParse(args, out var cli))
        {
            Environment.Exit(CliRunner.Run(cli));
            return;
        }

        // Single instance: if one is already running, forward our args (a URL or a --cli-add
        // payload) to it and exit so clicking the taskbar/tray icon — or `downloader <url>` from
        // the extension — surfaces the existing window instead of launching a second copy.
        if (!SingleInstanceService.TryClaim(args))
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
               .UsePlatformDetect()
               // WmClass must match the installed .desktop file's StartupWMClass so Linux desktops show
               // our app icon (and group the taskbar entry) instead of a generic/host icon (#1).
               .With(new X11PlatformOptions { EnableMultiTouch = false, WmClass = "Downloader" })
               .With(new Win32PlatformOptions { DpiAwareness = Win32DpiAwareness.PerMonitorDpiAware })
               .With(new AvaloniaNativePlatformOptions())
               .With(new MacOSPlatformOptions { ShowInDock = true })
               .WithInterFont()
               .LogToTrace()
               .UseReactiveUI()
               .UseSkia();
    }
}
