using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The Linux tray menu must be served at a path the snap's AppArmor profile lets the shell read
/// (<c>/MenuBar</c>), or a single click on the icon shows an empty menu — i.e. nothing at all.
/// </summary>
public class TrayDbusMenuTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_Avalonia_internal_it_relies_on_still_exists()
    {
        // Fails on the Avalonia upgrade that renames one, instead of on a user's tray.
        Assert.Empty(TrayDbusMenu.MissingMembers());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Anything_that_is_not_a_DBus_tray_is_left_alone()
    {
        Assert.Null(TrayDbusMenu.TryExport(new object(), new NativeMenu()));
        Assert.Null(TrayDbusMenu.TryExport((object)null, new NativeMenu()));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_real_DBus_tray_advertises_its_menu_at_a_path_the_snap_allows()
    {
        var hasBus = OperatingSystem.IsLinux()
                     && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));
        Assert.SkipUnless(hasBus, "needs a Linux session bus");

        var type = Type.GetType("Avalonia.FreeDesktop.DBusTrayIconImpl, Avalonia.FreeDesktop", throwOnError: true)!;
        var impl = Activator.CreateInstance(type)!;
        // Never put an icon in the developer's top bar (the interface member is not public, the method is).
        type.GetMethod("SetIsVisible")!.Invoke(impl, new object[] { false });
        try
        {
            Assert.StartsWith("/net/avaloniaui/dbusmenu/", TrayDbusMenu.AdvertisedPath(impl));

            using var exporter = TrayDbusMenu.TryExport(impl, new NativeMenu());

            Assert.NotNull(exporter);
            Assert.Equal("/MenuBar", TrayDbusMenu.AdvertisedPath(impl));
        }
        finally
        {
            ((IDisposable)impl).Dispose();
        }
    }
}
