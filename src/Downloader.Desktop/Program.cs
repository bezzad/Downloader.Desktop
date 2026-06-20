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
        // Single instance: if one is already running, forward our args (e.g. a URL) to it and exit so
        // clicking the taskbar/tray icon — or `downloader <url>` from the extension — surfaces the
        // existing window instead of launching a second copy.
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
