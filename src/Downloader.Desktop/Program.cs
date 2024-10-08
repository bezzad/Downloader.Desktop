﻿using Avalonia;
using Avalonia.ReactiveUI;
using System;
using Avalonia.Controls;

namespace Downloader.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
               .UsePlatformDetect()
               .With(new X11PlatformOptions { EnableMultiTouch = false })
               .With(new Win32PlatformOptions { DpiAwareness = Win32DpiAwareness.PerMonitorDpiAware })
               .With(new AvaloniaNativePlatformOptions())
               .With(new MacOSPlatformOptions { ShowInDock = true })
               .WithInterFont()
               .LogToTrace()
               .UseReactiveUI()
               .UseSkia();
    }
}
