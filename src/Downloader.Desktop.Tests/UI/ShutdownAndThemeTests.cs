using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// <see cref="ShutdownService"/> and <see cref="ThemeService"/>.
///
/// ShutdownService was reported at 0% even though ShutdownVerificationTests appears to cover it: that
/// suite is gated behind <c>DLDESKTOP_VERIFY=1</c> and silently returns in a normal run, so it passed
/// without executing anything. These tests are ungated — they drive the service directly instead of
/// through a real download, so they need no network and no timers.
///
/// The one thing never exercised is the actual OS power-off: <see cref="ShutdownService.PowerOffOverride"/>
/// stands in for it, which is the whole point of that seam.
/// </summary>
public class ShutdownAndThemeTests : IDisposable
{
    private readonly bool _notificationsWereEnabled = NotificationService.Enabled;

    public ShutdownAndThemeTests()
    {
        // Keep tests from shelling out to notify-send on this box.
        NotificationService.Enabled = false;
    }

    public void Dispose()
    {
        NotificationService.Enabled = _notificationsWereEnabled;
        ShutdownService.PowerOffOverride = null;
        ShutdownService.Cancel();
    }

    // ---- ShutdownService ---------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_is_scheduled_until_shutdown_is_armed()
    {
        ShutdownService.Cancel(); // ensure a clean slate
        Assert.False(ShutdownService.IsScheduled);

        // Cancelling when nothing is armed must be a silent no-op.
        ShutdownService.Cancel();
        Assert.False(ShutdownService.IsScheduled);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Scheduling_shows_a_cancelable_countdown_and_never_powers_off_on_its_own()
    {
        Localizer.Instance.Load("en");
        var poweredOff = false;
        ShutdownService.PowerOffOverride = () => poweredOff = true;

        ShutdownService.Schedule(notify: false);

        Assert.True(ShutdownService.IsScheduled, "arming a shutdown must show the countdown dialog");

        // Arming twice must not stack a second dialog.
        ShutdownService.Schedule(notify: false);
        Assert.True(ShutdownService.IsScheduled);

        ShutdownService.Cancel();

        Assert.False(ShutdownService.IsScheduled, "Cancel must dismiss the countdown dialog");
        Assert.False(poweredOff, "cancelling must never power the machine off");
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Arming_and_cancelling_repeatedly_leaves_no_dialog_behind()
    {
        Localizer.Instance.Load("en");
        var poweredOff = false;
        ShutdownService.PowerOffOverride = () => poweredOff = true;

        for (var i = 0; i < 3; i++)
        {
            ShutdownService.Schedule(notify: false);
            Assert.True(ShutdownService.IsScheduled);
            ShutdownService.Cancel();
            Assert.False(ShutdownService.IsScheduled);
        }

        Assert.False(poweredOff);
    }

    // ---- ThemeService ------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_accent_has_a_distinct_key_name_and_swatch()
    {
        var accents = ThemeService.Accents;

        Assert.NotEmpty(accents);
        Assert.Equal(accents.Count, accents.Select(a => a.Key).Distinct().Count());
        Assert.Equal(accents.Count, accents.Select(a => a.Color).Distinct().Count());

        foreach (var a in accents)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Key));
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            Assert.NotNull(a.Brush);
        }
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("Teal")]
    [InlineData("Blue")]
    [InlineData("Purple")]
    [InlineData("Green")]
    [InlineData("Amber")]
    public void Find_returns_the_requested_accent(string key)
    {
        Assert.Equal(key, ThemeService.Find(key).Key);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Chartreuse")]
    public void Find_falls_back_to_the_first_accent_for_an_unknown_key(string? key)
    {
        // A config carrying an accent from a newer build (or junk) must not crash the theme.
        Assert.Equal(ThemeService.Accents[0].Key, ThemeService.Find(key).Key);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Applying_an_accent_overrides_the_fluent_accent_resources()
    {
        var app = Application.Current;
        Assert.NotNull(app);

        ThemeService.ApplyAccent("Purple");

        var expected = ThemeService.Find("Purple").Color;
        Assert.Equal(expected, Assert.IsType<Color>(app!.Resources["SystemAccentColor"]));

        // Fluent derives its accent surfaces from the light/dark shades; all six must be present or
        // parts of the UI silently keep the previous accent.
        foreach (var name in new[]
                 {
                     "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                     "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3"
                 })
        {
            Assert.True(app.Resources.ContainsKey(name), name + " must be defined");
            Assert.IsType<Color>(app.Resources[name]);
        }

        // The selected-row tint follows the accent and must stay translucent, or dark text on a
        // selected row becomes unreadable (the #row-select regression).
        var row = Assert.IsType<SolidColorBrush>(app.Resources["RowSelectionBrush"]);
        Assert.Equal(expected, row.Color);
        Assert.InRange(row.Opacity, 0.05, 0.6);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Accent_shades_move_towards_white_and_black()
    {
        ThemeService.ApplyAccent("Blue");
        var res = Application.Current!.Resources;

        var baseColor = ThemeService.Find("Blue").Color;
        var light1 = (Color)res["SystemAccentColorLight1"]!;
        var light3 = (Color)res["SystemAccentColorLight3"]!;
        var dark1 = (Color)res["SystemAccentColorDark1"]!;
        var dark3 = (Color)res["SystemAccentColorDark3"]!;

        static int Sum(Color c) => c.R + c.G + c.B;

        // Light shades get progressively lighter, dark shades progressively darker.
        Assert.True(Sum(light1) > Sum(baseColor));
        Assert.True(Sum(light3) > Sum(light1));
        Assert.True(Sum(dark1) < Sum(baseColor));
        Assert.True(Sum(dark3) < Sum(dark1));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Apply_sets_the_theme_variant_and_the_accent_together()
    {
        var config = Config.New();
        config.Settings.AccentColor = "Green";
        config.IsThemeDarkMode = true;

        ThemeService.Apply(config);

        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.Equal(ThemeService.Find("Green").Color, (Color)Application.Current.Resources["SystemAccentColor"]!);

        config.IsThemeDarkMode = false;
        ThemeService.Apply(config);
        Assert.Equal(ThemeVariant.Light, Application.Current.RequestedThemeVariant);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Apply_tolerates_a_config_with_no_settings()
    {
        // Older/partial configs deserialize with a null Settings; the theme must still apply.
        var config = Config.New();
        config.Settings = null;

        ThemeService.Apply(config);

        Assert.Equal(ThemeService.Accents[0].Color, (Color)Application.Current!.Resources["SystemAccentColor"]!);
    }

    // ---- StartupService (read-only) ---------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Startup_state_can_be_queried_without_throwing()
    {
        // Deliberately read-only. Apply(true/false) writes a real autostart entry for the user
        // running the suite (~/.config/autostart on Linux, the HKCU Run key on Windows, a LaunchAgent
        // on macOS) — a test must not turn a developer's "launch at login" setting on or off behind
        // their back, so only the query path is covered here.
        var enabled = StartupService.IsEnabled();

        Assert.True(enabled || !enabled); // the contract is "never throws", not a particular value
    }
}
