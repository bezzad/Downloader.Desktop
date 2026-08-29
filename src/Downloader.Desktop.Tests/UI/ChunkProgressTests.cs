using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// One segment of the details window's per-connection strip, plus the copy-to-clipboard buttons
/// beside it.
///
/// The segment's job is to keep telling the truth after the events stop. Progress arrives only while
/// a connection is transferring, so a stopped or paused download leaves every unfinished segment
/// frozen on whatever it last said — which is why <c>Freeze</c> exists and why a finished segment
/// must be left alone by it. The colour is index-derived rather than assigned on arrival so the strip
/// does not reshuffle as bars update.
/// </summary>
public class ChunkProgressTests
{
    private static ChunkProgressViewModel Part(int index = 1, long total = 0)
    {
        Localizer.Instance.Load("en");
        return new ChunkProgressViewModel(index, total);
    }

    // ---- a segment's own state --------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_fresh_segment_is_pending_with_no_speed_and_an_unknown_total()
    {
        var part = Part(index: 1);

        Assert.Equal(0, part.Progress);
        Assert.Equal("Part 1", part.Title);
        Assert.Equal(string.Empty, part.SpeedText); // no "0 B/s" noise before it starts
        Assert.Equal("—", part.TotalText);
        Assert.False(string.IsNullOrWhiteSpace(part.StatusText));
        Assert.DoesNotContain("State_", part.StatusText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Progress_updates_the_fill_the_speed_and_the_byte_counts()
    {
        var part = Part();

        part.Update(progress: 42, speed: 1024 * 1024, received: 420, total: 1000);

        Assert.Equal(42, part.Progress);
        Assert.EndsWith("/s", part.SpeedText);
        Assert.Equal(DownloadItemViewModel.FormatBytes(420), part.DownloadedText);
        Assert.Equal(DownloadItemViewModel.FormatBytes(1000), part.TotalText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_seeded_total_shows_before_any_progress_arrives()
    {
        var part = Part(total: 2048);
        Assert.Equal(DownloadItemViewModel.FormatBytes(2048), part.TotalText);

        part.SetTotal(4096);
        Assert.Equal(DownloadItemViewModel.FormatBytes(4096), part.TotalText);

        // A zero or unchanged total is ignored rather than blanking the display.
        part.SetTotal(0);
        Assert.Equal(DownloadItemViewModel.FormatBytes(4096), part.TotalText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Completing_a_segment_fills_it_and_clears_the_speed()
    {
        var part = Part(total: 1000);
        part.Update(50, 999, 500, 1000);

        part.Complete();

        Assert.Equal(100, part.Progress);
        Assert.Equal(string.Empty, part.SpeedText);
        Assert.Equal(DownloadItemViewModel.FormatBytes(1000), part.DownloadedText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Freezing_an_unfinished_segment_stops_it_reading_as_downloading()
    {
        var part = Part(total: 1000);
        part.Update(50, 4096, 500, 1000);
        var whileRunning = part.StatusText;

        part.Freeze("State_Paused");

        // With no further events its status would otherwise stick on "Downloading" forever.
        Assert.NotEqual(whileRunning, part.StatusText);
        Assert.Equal(string.Empty, part.SpeedText);
        Assert.DoesNotContain("State_", part.StatusText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Freezing_leaves_an_already_finished_segment_alone()
    {
        var part = Part(total: 1000);
        part.Complete();
        var completed = part.StatusText;

        part.Freeze("State_Stopped");

        // A connection that genuinely finished must keep saying so even though the download as a
        // whole was stopped.
        Assert.Equal(completed, part.StatusText);
        Assert.Equal(100, part.Progress);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Fresh_progress_thaws_a_frozen_segment()
    {
        var part = Part(total: 1000);
        part.Update(50, 4096, 500, 1000);
        part.Freeze("State_Paused");
        var frozen = part.StatusText;

        part.Update(60, 4096, 600, 1000); // resumed

        Assert.NotEqual(frozen, part.StatusText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Syncing_from_the_package_advances_but_never_regresses_a_segment()
    {
        var part = Part();
        part.Update(60, 0, 600, 1000);

        part.SyncFromPackage(700, 1000);
        Assert.Equal(70, part.Progress, 3);

        // A late package snapshot must not rewind a fill that a per-chunk event already advanced.
        part.SyncFromPackage(300, 1000);
        Assert.Equal(70, part.Progress, 3);

        // An unknown total is ignored outright.
        part.SyncFromPackage(900, 0);
        Assert.Equal(70, part.Progress, 3);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Syncing_from_the_package_thaws_a_frozen_segment()
    {
        var part = Part();
        part.Update(50, 0, 500, 1000);
        part.Freeze("State_Stopped");
        var frozen = part.StatusText;

        part.SyncFromPackage(600, 1000);

        Assert.NotEqual(frozen, part.StatusText);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Each_connection_keeps_a_stable_distinct_colour()
    {
        // Assigned by index so the strip never reshuffles as bars update.
        var first = Part(1).Brush;
        Assert.NotNull(first);
        Assert.Equal(first, Part(1).Brush);
        Assert.NotEqual(first, Part(2).Brush);

        // More connections than palette entries must still resolve to a brush, not throw.
        Assert.NotNull(Part(99).Brush);
    }

    // ---- the copy buttons --------------------------------------------------

    private static DownloadDetailsViewModel Details(DownloadStatus status = DownloadStatus.Failed)
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var item = manager.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/movie.mkv",
            FileName = "movie.mkv",
            SaveFolder = "/tmp",
        }, autoStart: false);
        item.Status = status;
        item.ErrorMessage = "server said no";
        return new DownloadDetailsViewModel(item);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Copying_shows_a_confirmation_that_reverts_on_its_own()
    {
        var details = Details();
        Assert.False(details.UrlCopied);

        await details.CopyUrlAsync();

        // The button flips to a checkmark and back; without the revert it would claim "copied"
        // forever, so both halves matter.
        Assert.False(details.UrlCopied);
        Assert.DoesNotContain("Action_", details.CopyUrlTooltip);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task Copying_the_path_and_the_error_behave_the_same_way()
    {
        var details = Details();

        await details.CopyPathAsync();
        Assert.False(details.PathCopied);

        await details.CopyErrorAsync();
        Assert.False(details.ErrorCopied);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Copying_an_error_is_a_no_op_when_there_is_none()
    {
        var details = Details(DownloadStatus.Completed);
        details.Item.ErrorMessage = null;

        await details.CopyErrorAsync();

        Assert.False(details.ErrorCopied);
    }
}
