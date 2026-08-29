using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The details window: the mirror editor, the editable source URL, the per-item speed cap and the
/// "refresh an expired link" flow (issue #6).
///
/// The refresh flow is the part worth guarding. A signed link often dies before a long download
/// finishes; the user pastes a fresh one here. The engine only resumes onto the existing partial file
/// when the new link reports the SAME total size — a link to a different file makes it discard the
/// partial and start from zero. So a mismatch must ask first, and an abandoned or unreachable refresh
/// must leave the download exactly as it was (the URL box writes through to the item as the user
/// types, so "leave it as it was" means actively restoring the committed URL).
///
/// Both network and dialogs are replaced through the view model's own internal seams
/// (<c>ProbeAsync</c> / <c>ConfirmAsync</c>), so nothing here needs a window or a connection.
/// </summary>
public class DownloadDetailsViewModelTests
{
    private const string OriginalUrl = "https://10.255.255.1/signed/movie.mkv?token=old";
    private const string FreshUrl = "https://10.255.255.1/signed/movie.mkv?token=new";

    private static (DownloadDetailsViewModel details, DownloadItemViewModel item, DownloadManager manager) Build(
        DownloadStatus status = DownloadStatus.Stopped, long knownSize = 1000)
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var item = manager.Add(new DownloadItem { Url = OriginalUrl, FileName = "movie.mkv", SaveFolder = "/tmp" },
            autoStart: false);
        item.Status = status;
        item.Size = knownSize;
        return (new DownloadDetailsViewModel(item), item, manager);
    }

    // ---- basics ------------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_design_time_constructor_is_inert()
    {
        Localizer.Instance.Load("en");
        var vm = new DownloadDetailsViewModel();

        Assert.Null(vm.Item);
        Assert.False(vm.HasParts);
        Assert.False(vm.HasQueue);
        Assert.False(vm.HasConfig);
        Assert.False(vm.HasError);
        Assert.Equal(0, vm.Connections);
        Assert.Equal(string.Empty, vm.PartsSummary);
        Assert.False(string.IsNullOrWhiteSpace(vm.WindowTitle));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_window_title_shows_progress_and_file_name()
    {
        var (details, item, _) = Build();
        item.Progress = 21;

        Assert.Equal($"21% {item.DisplayName}", details.WindowTitle);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_file_path_and_queue_come_from_the_item()
    {
        var (details, item, _) = Build();

        Assert.Equal(item.GetItem().FilePath, details.FilePath);
        Assert.True(details.HasQueue); // items always land on a queue
        Assert.Equal(item.QueueName, item.QueueName);
    }

    // ---- editing the source URL -------------------------------------------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Stopped, true)]
    [InlineData(DownloadStatus.Failed, true)]
    [InlineData(DownloadStatus.Paused, true)]
    [InlineData(DownloadStatus.Created, true)]
    [InlineData(DownloadStatus.None, true)]
    [InlineData(DownloadStatus.Running, false)]
    [InlineData(DownloadStatus.Completed, false)]
    public void The_url_is_editable_only_while_the_download_is_not_active(DownloadStatus status, bool editable)
    {
        var (details, _, _) = Build(status);

        Assert.Equal(editable, details.CanEdit);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Typing_in_the_url_box_writes_through_to_the_item()
    {
        var (details, item, _) = Build();

        details.EditableUrl = FreshUrl;

        Assert.Equal(FreshUrl, item.Url);
        Assert.Equal(FreshUrl, details.EditableUrl);
    }

    // ---- mirrors -----------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Mirrors_start_empty_and_can_be_added_and_removed()
    {
        var (details, item, _) = Build();

        Assert.Empty(details.Mirrors);

        details.AddMirrorCommand.Execute(null);
        Assert.Single(details.Mirrors);

        details.Mirrors[0].Url = "https://10.255.255.2/movie.mkv";

        // The primary URL is kept; mirrors are everything after it.
        Assert.Equal(OriginalUrl, item.GetItem().Urls[0]);
        Assert.Equal(new[] { "https://10.255.255.2/movie.mkv" }, item.GetItem().Mirrors.ToArray());

        details.Mirrors[0].RemoveCommand.Execute(null);

        Assert.Empty(details.Mirrors);
        Assert.Empty(item.GetItem().Mirrors);
        Assert.Equal(OriginalUrl, item.GetItem().Urls[0]);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Blank_mirror_rows_are_not_pushed_onto_the_item()
    {
        var (details, item, _) = Build();

        details.AddMirrorCommand.Execute(null);
        details.AddMirrorCommand.Execute(null);
        details.Mirrors[1].Url = "https://10.255.255.2/movie.mkv";

        // An empty editor row is a UI affordance, not a mirror — sending it to the engine would make
        // it try to download from "".
        Assert.Equal(new[] { "https://10.255.255.2/movie.mkv" }, item.GetItem().Mirrors.ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Existing_mirrors_seed_the_editor_when_the_dialog_opens()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        var item = manager.Add(new DownloadItem
        {
            Urls = new() { OriginalUrl, "https://10.255.255.3/f", "https://10.255.255.4/f" },
            FileName = "movie.mkv"
        }, autoStart: false);

        var details = new DownloadDetailsViewModel(item);

        Assert.Equal(2, details.Mirrors.Count);
        Assert.Equal(new[] { "https://10.255.255.3/f", "https://10.255.255.4/f" },
            details.Mirrors.Select(m => m.Url).ToArray());
        Assert.Contains("2", details.MirrorsHeader);
    }

    // ---- per-item speed limit ---------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Setting_a_speed_cap_opts_the_item_out_of_the_global_limit_and_persists_it()
    {
        var (details, item, _) = Build();

        Assert.True(details.UsesGlobalSpeedLimit);

        details.SpeedLimitKb = 256;

        Assert.False(details.UsesGlobalSpeedLimit);
        Assert.True(item.HasCustomSpeedLimit);
        Assert.Equal(256L * 1024, item.CustomSpeedLimitBytesPerSecond);

        // Persisted on the model, so the cap survives stop -> resume and a restart.
        Assert.True(item.GetItem().HasCustomSpeedLimit);
        Assert.Equal(256L * 1024, item.GetItem().CustomSpeedLimitBytesPerSecond);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Use_global_limit_clears_the_per_item_override()
    {
        var (details, item, _) = Build();
        details.SpeedLimitKb = 256;

        details.UseGlobalLimitCommand.Execute(null);

        Assert.True(details.UsesGlobalSpeedLimit);
        Assert.False(item.HasCustomSpeedLimit);
        Assert.Equal(0, details.SpeedLimitKb);
    }

    // ---- refreshing an expired link ---------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Blanking_the_url_box_cannot_erase_the_download_s_link()
    {
        var (details, item, _) = Build(knownSize: 1000);
        string probedUrl = null;
        details.ProbeAsync = (url, _) =>
        {
            probedUrl = url;
            return Task.FromResult(new RemoteFileInfo { FileName = "movie.mkv", FileSize = 1000 });
        };

        details.EditableUrl = "   ";

        // DownloadItem.Url deliberately ignores a blank value, so clearing the box does NOT leave the
        // download with no source — it keeps the link it had. A refresh therefore re-validates the
        // existing link rather than failing on an empty one.
        Assert.Equal(OriginalUrl, item.Url);

        await details.RefreshLinkAsync();

        Assert.Equal(OriginalUrl, probedUrl);
        Assert.False(details.RefreshFailed);
        Assert.False(details.IsRefreshing);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_link_reporting_the_same_size_is_applied_and_the_download_resumes()
    {
        var (details, item, _) = Build(knownSize: 1000);
        details.ProbeAsync = (url, _) => Task.FromResult(new RemoteFileInfo { FileName = "movie.mkv", FileSize = 1000 });
        details.ConfirmAsync = (_, _) => throw new InvalidOperationException("must not prompt on a size match");

        details.EditableUrl = FreshUrl;
        await details.RefreshLinkAsync();

        Assert.Equal(FreshUrl, item.Url);
        Assert.False(details.RefreshFailed);
        Assert.False(details.IsRefreshing);

        // A stored plan holds the OLD expired segment URLs — it must be dropped so the next start
        // resolves the refreshed link from scratch.
        Assert.Null(item.GetItem().PlanJson);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_unknown_size_is_applied_without_a_prompt()
    {
        var (details, item, _) = Build(knownSize: 1000);
        details.ProbeAsync = (_, _) => Task.FromResult(new RemoteFileInfo { FileName = "movie.mkv", FileSize = 0 });
        details.ConfirmAsync = (_, _) => throw new InvalidOperationException("must not prompt on an unknown size");

        details.EditableUrl = FreshUrl;
        await details.RefreshLinkAsync();

        Assert.Equal(FreshUrl, item.Url);
        Assert.False(details.RefreshFailed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task A_different_size_asks_first_and_applies_the_link_when_confirmed()
    {
        var (details, item, _) = Build(knownSize: 1000);
        var asked = false;
        details.ProbeAsync = (_, _) => Task.FromResult(new RemoteFileInfo { FileName = "other.mkv", FileSize = 4242 });
        details.ConfirmAsync = (_, _) => { asked = true; return Task.FromResult(true); };

        details.EditableUrl = FreshUrl;
        await details.RefreshLinkAsync();

        Assert.True(asked, "a size mismatch discards the partial file, so it must be confirmed");
        Assert.Equal(FreshUrl, item.Url);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Declining_the_mismatch_prompt_leaves_the_original_link_in_place()
    {
        var (details, item, _) = Build(knownSize: 1000);
        details.ProbeAsync = (_, _) => Task.FromResult(new RemoteFileInfo { FileName = "other.mkv", FileSize = 4242 });
        details.ConfirmAsync = (_, _) => Task.FromResult(false);

        details.EditableUrl = FreshUrl; // writes through to the item as the user types
        await details.RefreshLinkAsync();

        // Saying "no" must undo that write-through, or the download is left pointing at a link the
        // user just rejected.
        Assert.Equal(OriginalUrl, item.Url);
        Assert.False(details.RefreshFailed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task An_unreachable_link_is_reported_and_changes_nothing()
    {
        var (details, item, _) = Build();
        details.ProbeAsync = (_, _) => Task.FromResult<RemoteFileInfo>(null);

        details.EditableUrl = FreshUrl;
        await details.RefreshLinkAsync();

        Assert.Equal(OriginalUrl, item.Url);
        Assert.True(details.RefreshFailed);
        Assert.True(details.HasRefreshMessage);
        Assert.NotNull(details.RefreshMessageBrush);
        Assert.False(details.IsRefreshing);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Refreshing_is_inert_without_a_manager()
    {
        Localizer.Instance.Load("en");
        var details = new DownloadDetailsViewModel();

        await details.RefreshLinkAsync(); // no item, no manager — must not throw

        Assert.False(details.IsRefreshing);
    }

    // ---- copy actions ------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Copy_tooltips_start_in_their_uncopied_state()
    {
        var (details, _, _) = Build();

        Assert.False(details.UrlCopied);
        Assert.False(details.PathCopied);
        Assert.False(details.ErrorCopied);

        foreach (var tip in new[] { details.CopyUrlTooltip, details.CopyPathTooltip, details.CopyErrorTooltip })
        {
            Assert.False(string.IsNullOrWhiteSpace(tip));
            Assert.DoesNotContain("Action_", tip); // localized, not a raw key
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_error_on_the_item_surfaces_in_the_dialog()
    {
        var (details, item, _) = Build(DownloadStatus.Failed);
        item.ErrorMessage = "server said no";

        Assert.True(details.HasError);
        Assert.Equal("server said no", details.ErrorMessage);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Cleanup_detaches_without_throwing()
    {
        var (details, _, _) = Build();

        details.Cleanup();
        details.Cleanup(); // idempotent — the window can close more than once
    }
}
