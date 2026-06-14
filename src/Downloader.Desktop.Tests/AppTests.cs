using System.Globalization;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Downloader;
using Downloader.Desktop.Converters;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests;

/// <summary>Tests that need the (headless) Avalonia runtime: geometry parsing, dispatcher, view models.</summary>
public class AppTests
{
    [AvaloniaTheory]
    [InlineData("video")]
    [InlineData("audio")]
    [InlineData("image")]
    [InlineData("archive")]
    [InlineData("document")]
    [InlineData("app")]
    [InlineData("disc")]
    [InlineData("file")]
    [InlineData("unknown-kind")]
    public void FileKind_icons_parse(string kind)
    {
        var geometry = FileKindToIconConverter.Instance.Convert(kind, typeof(Geometry), null, CultureInfo.InvariantCulture);
        Assert.IsAssignableFrom<Geometry>(geometry);
    }

    [AvaloniaFact]
    public void Manager_add_and_remove_updates_stats()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var vm = manager.Add(new DownloadItem { Url = "https://host/a.zip", SaveFolder = "/tmp" }, autoStart: false);
        Assert.Single(manager.Items);
        Assert.Equal(1, manager.QueuedCount);

        vm.Status = DownloadStatus.Completed;
        Assert.Equal(1, manager.CompletedCount);
        Assert.Equal(0, manager.QueuedCount);

        manager.Remove(vm);
        Assert.Empty(manager.Items);
    }

    [AvaloniaFact]
    public void DownloadsViewModel_filters_by_status_and_search()
    {
        var manager = new DownloadManager();
        manager.Initialize(Config.New());

        var a = manager.Add(new DownloadItem { Url = "https://host/movie.mp4", FileName = "movie.mp4" }, autoStart: false);
        manager.Add(new DownloadItem { Url = "https://host/song.mp3", FileName = "song.mp3" }, autoStart: false);
        a.Status = DownloadStatus.Completed;

        var view = new DownloadsViewModel(manager) { Filter = StatusFilter.Completed };
        Assert.Single(view.ItemsView);

        view.Filter = StatusFilter.All;
        view.Search = "song";
        Assert.Single(view.ItemsView);
    }

    [AvaloniaFact]
    public void Removing_a_queue_reassigns_its_items()
    {
        var manager = new DownloadManager();
        var config = Config.New();
        manager.Initialize(config);

        var queue = manager.AddQueue("Second");
        var vm = manager.Add(new DownloadItem { Url = "https://host/c.zip", QueueId = queue.Id }, autoStart: false);
        Assert.Equal(queue.Id, vm.GetItem().QueueId);

        manager.RemoveQueue(queue);
        Assert.Equal(config.DefaultQueue.Id, vm.GetItem().QueueId);
    }

    [AvaloniaFact]
    public void Add_dialog_parses_multiple_urls()
    {
        var config = Config.New();
        var vm = new AddDownloadItemViewModel(config, "https://host/a.zip\nhttps://host/b.zip");
        Assert.True(vm.CanDownload);
        Assert.True(vm.IsMultiple);
    }

    [AvaloniaFact]
    public void Pending_name_shows_placeholder()
    {
        var item = new DownloadItem { Url = "https://host/x", Status = DownloadStatus.Running };
        var vm = new DownloadItemViewModel(item, null);
        Assert.True(vm.IsNamePending);
        Assert.Equal("Fetching name…", vm.DisplayName);
    }
}
