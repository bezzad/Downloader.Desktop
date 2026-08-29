using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The per-row state that drives what a download looks like and which buttons it offers.
///
/// These getters are one-liners, but they are the row's whole contract with the view: Can*/IsActive
/// decide which action buttons appear, so a wrong one either hides the only way to recover a download
/// or offers "Pause" on a finished file. They are checked across the full status range rather than the
/// happy path, because the bugs in this area have always been about an unusual state (a completed row
/// accepting Stop, a queued row that could not be started).
/// </summary>
public class DownloadRowStateTests
{
    private static DownloadItemViewModel Row(DownloadStatus status, string name = "movie.mkv")
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        // An unreachable IP, never a hostname: a .invalid host makes the engine sit in DNS resolution
        // and hangs the suite (that is what the repo's 10.255.255.1 convention is for).
        var vm = manager.Add(new DownloadItem { Url = "https://10.255.255.1/" + name, FileName = name },
            autoStart: false);
        vm.Status = status;
        return vm;
    }

    // ---- which buttons a row offers ---------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Running, true)]
    [InlineData(DownloadStatus.Paused, false)]
    [InlineData(DownloadStatus.Stopped, false)]
    [InlineData(DownloadStatus.Created, false)]
    [InlineData(DownloadStatus.None, false)]
    [InlineData(DownloadStatus.Completed, false)]
    [InlineData(DownloadStatus.Failed, false)]
    public void Only_a_running_download_can_be_paused(DownloadStatus status, bool expected) =>
        Assert.Equal(expected, Row(status).CanPause);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Paused, true)]
    [InlineData(DownloadStatus.Stopped, true)]
    [InlineData(DownloadStatus.Created, true)]
    [InlineData(DownloadStatus.None, true)]
    [InlineData(DownloadStatus.Running, false)]
    [InlineData(DownloadStatus.Completed, false)]  // never restart a finished download from zero
    [InlineData(DownloadStatus.Failed, false)]     // a failure offers Retry, not Resume
    public void Resume_is_offered_for_anything_paused_or_waiting(DownloadStatus status, bool expected) =>
        Assert.Equal(expected, Row(status).CanResume);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Failed, true)]
    [InlineData(DownloadStatus.Completed, false)]
    [InlineData(DownloadStatus.Running, false)]
    [InlineData(DownloadStatus.Stopped, false)]
    public void Only_a_failed_download_offers_retry(DownloadStatus status, bool expected) =>
        Assert.Equal(expected, Row(status).CanRetry);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Running, true)]
    [InlineData(DownloadStatus.Paused, true)]
    [InlineData(DownloadStatus.Created, false)]
    [InlineData(DownloadStatus.Stopped, false)]
    [InlineData(DownloadStatus.Completed, false)]
    [InlineData(DownloadStatus.Failed, false)]
    public void A_download_is_active_while_running_or_paused(DownloadStatus status, bool expected) =>
        Assert.Equal(expected, Row(status).IsActive);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Completed, true)]
    [InlineData(DownloadStatus.Running, false)]
    [InlineData(DownloadStatus.Failed, false)]
    public void Completed_is_reported_for_finished_downloads_only(DownloadStatus status, bool expected) =>
        Assert.Equal(expected, Row(status).IsCompleted);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Running, false)]   // a running row shows live %, not a badge
    [InlineData(DownloadStatus.Paused, true)]
    [InlineData(DownloadStatus.Created, true)]
    [InlineData(DownloadStatus.Completed, true)]
    [InlineData(DownloadStatus.Failed, true)]
    public void The_status_badge_shows_for_every_state_except_running(DownloadStatus status, bool expected) =>
        Assert.Equal(expected, Row(status).ShowStatusBadge);

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Failed, true)]
    [InlineData(DownloadStatus.Completed, false)]
    [InlineData(DownloadStatus.Stopped, false)]
    public void Only_a_failed_row_reports_an_error(DownloadStatus status, bool expected) =>
        Assert.Equal(expected, Row(status).HasError);

    // ---- formatted text ----------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Unknown_size_and_idle_speed_render_as_a_dash()
    {
        var row = Row(DownloadStatus.Created);

        // An em dash, not "0 B" — a queued row has no size yet and must not claim to be empty.
        Assert.Equal("—", row.SizeText);
        Assert.Equal("—", row.SpeedText);
        Assert.Equal("—", row.TimeLeftText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Known_size_and_speed_are_formatted()
    {
        var row = Row(DownloadStatus.Running);
        row.Size = 5 * 1024 * 1024;
        row.Speed = 1024 * 1024;

        Assert.Equal(DownloadItemViewModel.FormatBytes(5 * 1024 * 1024), row.SizeText);
        Assert.EndsWith("/s", row.SpeedText);
        Assert.Contains(DownloadItemViewModel.FormatBytes(1024 * 1024), row.SpeedText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Time_left_is_only_estimated_while_running()
    {
        var row = Row(DownloadStatus.Running);
        row.Size = 10 * 1024 * 1024;
        row.Downloaded = 2 * 1024 * 1024;
        row.Speed = 1024 * 1024; // 8 MB left at 1 MB/s

        var running = row.TimeLeftText;
        Assert.NotEqual("—", running);

        // Pausing stops the estimate — a frozen "8s remaining" on a paused row is a lie.
        row.Status = DownloadStatus.Paused;
        Assert.Equal("—", row.TimeLeftText);
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(45, "45s")]
    [InlineData(83, "1m 23s")]
    [InlineData(7500, "2h 5m")]
    [InlineData(0, "0s")]   // zero is a real estimate ("done in a moment"), not "unknown"
    [InlineData(-1, "—")]
    [InlineData(double.PositiveInfinity, "—")]
    [InlineData(double.NaN, "—")]
    public void Durations_are_formatted_compactly(double seconds, string expected) =>
        Assert.Equal(expected, DownloadItemViewModel.FormatDuration(seconds));

    // ---- name, tooltip, grouping ------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_tooltip_is_the_name_until_something_goes_wrong()
    {
        var row = Row(DownloadStatus.Completed);
        Assert.Equal(row.DisplayName, row.NameTooltip);

        row.Status = DownloadStatus.Failed;
        row.ErrorMessage = "server said no";

        // The grid trims the name, so the tooltip is where the failure reason is actually readable.
        Assert.Contains(row.DisplayName, row.NameTooltip);
        Assert.Contains("server said no", row.NameTooltip);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_queued_row_shows_its_resolved_preview_name()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var row = manager.Add(new DownloadItem { Url = "https://10.255.255.1/x" }, autoStart: false);

        row.PreviewName = "resolved.iso";

        Assert.Equal("resolved.iso", row.PreviewName);
        // The preview must not be written onto the model, or it gets forced on the engine.
        Assert.True(string.IsNullOrEmpty(row.GetItem().FileName));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_ungrouped_download_falls_back_to_the_default_group()
    {
        var row = Row(DownloadStatus.Created);

        Assert.False(string.IsNullOrWhiteSpace(row.Group));
        Assert.DoesNotContain("Group_", row.Group); // localized, not a raw key
    }

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData("movie.mkv")]
    [InlineData("song.mp3")]
    [InlineData("photo.jpg")]
    [InlineData("archive.zip")]
    [InlineData("notes.pdf")]
    [InlineData("installer.exe")]
    [InlineData("noextension")]
    [InlineData("")]
    public void Every_file_name_maps_to_some_row_icon(string name)
    {
        // The converter looks the kind up to pick an icon; an unmapped/blank name must still resolve
        // to a usable kind rather than null, or the row renders with no icon at all.
        var kind = DownloadItemViewModel.GetFileKind(name);

        Assert.False(string.IsNullOrWhiteSpace(kind));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_row_reports_the_file_kind_of_its_name()
    {
        var row = Row(DownloadStatus.Completed, "movie.mkv");

        Assert.Equal(DownloadItemViewModel.GetFileKind("movie.mkv"), row.FileKind);
    }

    // ---- flags carried on the model ---------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_completed_row_always_shows_a_full_bar()
    {
        var row = Row(DownloadStatus.Created);
        row.Status = DownloadStatus.Completed;

        // A file that already existed on disk completes with Downloaded=0; computing the bar from
        // bytes would show 0% on a finished download (especially after a restart).
        Assert.Equal(100, row.Progress);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Already_existed_and_refresh_flags_default_to_off()
    {
        var row = Row(DownloadStatus.Created);

        Assert.False(row.AlreadyExisted);
        Assert.False(row.IsRefreshingLink);
        Assert.False(row.HasCustomSpeedLimit);
        Assert.Equal(0, row.CustomSpeedLimitBytesPerSecond);
        Assert.Null(row.PlanStage);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_speed_cap_writes_through_to_the_persisted_model()
    {
        var row = Row(DownloadStatus.Stopped);

        row.HasCustomSpeedLimit = true;
        row.CustomSpeedLimitBytesPerSecond = 128 * 1024;

        Assert.True(row.GetItem().HasCustomSpeedLimit);
        Assert.Equal(128 * 1024, row.GetItem().CustomSpeedLimitBytesPerSecond);
    }
}
