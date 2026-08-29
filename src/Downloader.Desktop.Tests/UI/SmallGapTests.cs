using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Behaviors;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The last few defensive branches in the small behaviors and overlay services. Each of these is the
/// path that only runs when something has already gone wrong (a cleared numeric box, a platform that
/// cannot create the overlay window, an OS with no handler for a link), which is exactly why none of
/// them had ever executed in the suite.
/// </summary>
public class SmallGapTests
{
    // ---- NumericCoerce -----------------------------------------------------

    /// <summary>
    /// The whole point of the behavior: clearing the box sets <c>Value = null</c>, which a binding to a
    /// non-nullable setting cannot convert. It must snap back to Minimum instead of surfacing a
    /// "value cannot be null" validation error in the view.
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clearing_a_numeric_box_snaps_it_back_to_the_minimum()
    {
        var nud = new NumericUpDown { Minimum = 4, Maximum = 64, Value = 16 };
        NumericCoerce.SetEmptyToMinimum(nud, true);
        Assert.True(NumericCoerce.GetEmptyToMinimum(nud));

        nud.Value = null; // what the control does when the user deletes the text

        Assert.Equal(4m, nud.Value);
    }

    /// <summary>Turning the behavior off unsubscribes it — a cleared box is then left as null.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Turning_the_coercion_off_leaves_a_cleared_box_alone()
    {
        var nud = new NumericUpDown { Minimum = 4, Value = 16 };
        NumericCoerce.SetEmptyToMinimum(nud, true);
        NumericCoerce.SetEmptyToMinimum(nud, false);
        Assert.False(NumericCoerce.GetEmptyToMinimum(nud));

        nud.Value = null;

        Assert.Null(nud.Value);
    }

    // ---- NotchService ------------------------------------------------------

    /// <summary>
    /// The overlay is fail-soft on purpose: a platform that cannot create a borderless topmost window
    /// must log and stay inactive rather than take the app down. Driven here by starting it off the UI
    /// thread, which is the same shape of failure (the window refuses to be created).
    /// </summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_overlay_that_cannot_be_created_leaves_the_service_inactive()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        try
        {
            await Task.Run(() => NotchService.Start(manager));

            Assert.False(NotchService.IsActive);
        }
        finally
        {
            NotchService.Stop();
        }
    }

    /// <summary>Stopping an overlay that was never started is a no-op, not a null dereference.</summary>
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Stopping_an_overlay_that_never_started_is_harmless()
    {
        NotchService.Stop();
        NotchService.Stop();

        Assert.False(NotchService.IsActive);
    }

    // ---- ShellLauncher -----------------------------------------------------

    /// <summary>
    /// With no override installed this really does hand the target to the OS. The contract is
    /// best-effort — a machine with nothing registered for the target must come back with an answer,
    /// never an exception, because every call site swallows the result. The target is a path that
    /// cannot exist, so nothing opens either way.
    /// </summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Handing_an_unopenable_target_to_the_OS_reports_back_instead_of_throwing()
    {
        var previous = ShellLauncher.OpenOverride;
        ShellLauncher.OpenOverride = null;
        try
        {
            var ex = Record.Exception(() => ShellLauncher.TryOpen("/nonexistent/downloader-tests/no-such-file"));

            Assert.Null(ex);
        }
        finally
        {
            ShellLauncher.OpenOverride = previous;
        }
    }

    /// <summary>A blank target is rejected before anything is handed to the OS.</summary>
    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void A_blank_target_is_never_handed_to_the_OS()
    {
        Assert.False(ShellLauncher.TryOpen("   "));
        Assert.False(ShellLauncher.TryOpen(null));
        Assert.False(ShellLauncher.Run(""));
    }
}
