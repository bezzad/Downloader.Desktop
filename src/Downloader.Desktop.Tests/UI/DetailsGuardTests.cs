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
/// The details window with nothing behind it, and the transient states its buttons pass through.
///
/// Nearly every member of this view model is written <c>Item?.Something</c>, because the XAML
/// designer instantiates it with no download at all and because the dialog can outlive the row it was
/// opened for. None of those null sides had ever run: the existing tests always build it around a
/// real item, so a regression that dereferenced <c>Item</c> would only show up as a designer crash or
/// a NullReferenceException when a user removes a download while its details are open.
/// </summary>
public class DetailsGuardTests
{
    private static DownloadDetailsViewModel Empty()
    {
        Localizer.Instance.Load("en");
        return new DownloadDetailsViewModel();   // the design-time constructor: no item
    }

    private static DownloadDetailsViewModel WithItem(DownloadStatus status = DownloadStatus.Stopped)
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;
        var item = manager.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/movie.mkv",
            FileName = "movie.mkv",
            SaveFolder = "/tmp",
        }, autoStart: false);
        item.Status = status;
        return new DownloadDetailsViewModel(item);
    }

    // ---- every read with no download behind it -----------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_displayed_value_is_readable_with_no_download()
    {
        var vm = Empty();

        // The designer builds exactly this. Any of these throwing shows up as a broken preview and,
        // worse, as a crash if the dialog outlives its row.
        Assert.Null(vm.Item);
        Assert.Equal(0, vm.Connections);
        Assert.False(vm.HasConfig);
        Assert.False(vm.HasQueue);
        Assert.False(vm.HasParts);
        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.FilePath);
        Assert.Null(vm.EditableUrl);
        Assert.False(vm.CanEdit);
        Assert.Equal(string.Empty, vm.PartsSummary);
        Assert.Equal(0, vm.SpeedLimitKb);
        Assert.True(vm.UsesGlobalSpeedLimit);
        Assert.False(string.IsNullOrWhiteSpace(vm.WindowTitle));
        Assert.Empty(vm.Mirrors);
        Assert.Null(vm.OpenFolderCommand);   // there is no row to reveal
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_command_is_inert_with_no_download()
    {
        var vm = Empty();

        vm.EditableUrl = "https://10.255.255.1/other.bin"; // nothing to write through to
        vm.SpeedLimitKb = 128;
        vm.UseGlobalSpeedLimit();
        vm.Cleanup();

        Assert.Null(vm.EditableUrl);
        Assert.True(vm.UsesGlobalSpeedLimit);

        // The design-time constructor builds no commands: XAML bound to a null ICommand simply shows
        // a disabled button, which is the right preview for a dialog with no download behind it.
        Assert.Null(vm.AddMirrorCommand);
        Assert.Null(vm.RefreshLinkCommand);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Copying_with_nothing_to_copy_does_nothing()
    {
        var vm = Empty();

        await vm.CopyUrlAsync();
        await vm.CopyPathAsync();
        await vm.CopyErrorAsync();

        Assert.False(vm.UrlCopied);
        Assert.False(vm.PathCopied);
        Assert.False(vm.ErrorCopied);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async Task Refreshing_a_link_with_no_download_is_inert()
    {
        var vm = Empty();

        await vm.RefreshLinkAsync();

        Assert.False(vm.IsRefreshing);
        Assert.False(vm.HasRefreshMessage);
    }

    // ---- the copy confirmation while it is showing -------------------------

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_tooltip_says_copied_while_the_confirmation_is_showing()
    {
        var vm = WithItem();

        // Start the copy but do not await it: the "Copied!" state is transient and reverts itself,
        // so this is the only way to observe the label the user actually sees.
        var pending = vm.CopyUrlAsync();
        var whileCopied = vm.CopyUrlTooltip;
        var flagged = vm.UrlCopied;
        await pending;

        Assert.True(flagged);
        Assert.NotEqual(whileCopied, vm.CopyUrlTooltip); // reverted
        Assert.DoesNotContain("Action_", whileCopied);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task A_second_copy_takes_over_the_confirmation_from_the_first()
    {
        var vm = WithItem();

        // Two copies in flight: the first must NOT revert the label out from under the second, or the
        // checkmark would vanish while the user is still looking at it.
        var first = vm.CopyPathAsync();
        await Task.Delay(50);
        var second = vm.CopyPathAsync();

        await Task.WhenAll(first, second);

        Assert.False(vm.PathCopied);
    }

    [AvaloniaFact(Timeout = TestTimeouts.SlowMs)]
    public async Task The_error_copy_confirmation_behaves_the_same()
    {
        var vm = WithItem(DownloadStatus.Failed);
        vm.Item.ErrorMessage = "server said no";

        var pending = vm.CopyErrorAsync();
        var flagged = vm.ErrorCopied;
        var tooltip = vm.CopyErrorTooltip;
        await pending;

        Assert.True(flagged);
        Assert.DoesNotContain("Action_", tooltip);
        Assert.False(vm.ErrorCopied);
    }

    // ---- the speed cap read-back -------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void An_unlimited_cap_reads_as_zero_however_the_engine_spells_it()
    {
        var vm = WithItem();
        vm.Item.Configuration = new DownloadConfiguration();

        // The engine normalises 0 to long.MaxValue — both mean "no limit", and showing
        // 9007199254740991 KB/s in the box would be absurd.
        vm.Item.Configuration.MaximumBytesPerSecond = long.MaxValue;
        Assert.Equal(0, vm.SpeedLimitKb);

        vm.Item.Configuration.MaximumBytesPerSecond = 0;
        Assert.Equal(0, vm.SpeedLimitKb);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clearing_the_cap_falls_back_to_the_global_limit()
    {
        var vm = WithItem();
        vm.Item.Configuration = new DownloadConfiguration();
        vm.Item.Manager.Config.Settings.MaximumBytesPerSecond = 512 * 1024;

        vm.SpeedLimitKb = 64;
        Assert.False(vm.UsesGlobalSpeedLimit);

        vm.UseGlobalSpeedLimit();

        Assert.True(vm.UsesGlobalSpeedLimit);
        Assert.Equal(512 * 1024, vm.Item.Configuration.MaximumBytesPerSecond);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Clearing_the_cap_with_no_global_limit_means_unlimited()
    {
        var vm = WithItem();
        vm.Item.Configuration = new DownloadConfiguration();
        vm.Item.Manager.Config.Settings.MaximumBytesPerSecond = 0;

        vm.SpeedLimitKb = 64;
        vm.UseGlobalSpeedLimit();

        Assert.Equal(0, vm.SpeedLimitKb);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Setting_a_negative_cap_means_unlimited()
    {
        var vm = WithItem();
        vm.Item.Configuration = new DownloadConfiguration();

        vm.SpeedLimitKb = -5;

        Assert.Equal(0, vm.SpeedLimitKb);
        Assert.Equal(0, vm.Item.CustomSpeedLimitBytesPerSecond);
    }

    // ---- the window title ---------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_download_with_no_name_yet_falls_back_to_the_generic_title()
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        manager.Config.DefaultQueue.IsRunning = false;
        var item = manager.Add(new DownloadItem { Url = "https://10.255.255.1/x" }, autoStart: false);

        var vm = new DownloadDetailsViewModel(item);

        // Before the engine resolves a name there is nothing to put in the title bar; "0% " alone
        // would look broken.
        Assert.False(string.IsNullOrWhiteSpace(vm.WindowTitle));
        Assert.DoesNotContain("Det_", vm.WindowTitle);
    }

    // ---- opening on a download that is already in a terminal state ---------

    [Theory(Timeout = TestTimeouts.DefaultMs)]
    [InlineData(DownloadStatus.Failed)]
    [InlineData(DownloadStatus.Stopped)]
    [InlineData(DownloadStatus.Paused)]
    [InlineData(DownloadStatus.Completed)]
    public void Opening_the_dialog_on_a_finished_download_does_not_throw(DownloadStatus status)
    {
        var vm = WithItem(status);

        // The constructor reflects the terminal state onto every segment; with no engine attached
        // there are no segments, and that has to be fine too.
        Assert.NotNull(vm);
        Assert.Empty(vm.Parts);
        vm.Cleanup();
    }
}
