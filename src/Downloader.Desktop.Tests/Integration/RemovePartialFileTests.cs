using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.Integration;

/// <summary>
/// Whether Remove also tidies the file off the disk. The line that matters is that it only ever deletes the
/// HALF-FINISHED file: Remove is one click on a grid row, so a misclick must not be able to destroy a
/// download the user waited for.
/// </summary>
public class RemovePartialFileTests
{
    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async System.Threading.Tasks.Task With_the_option_off_the_partial_file_is_left_alone()
    {
        var (manager, _, vm, partial, final) = Scenario(deleteOnRemove: false, completed: false);

        await manager.Remove(vm);

        Assert.True(File.Exists(partial), "today's behaviour: Remove drops the record only");
        Assert.False(File.Exists(final));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async System.Threading.Tasks.Task With_the_option_on_the_partial_file_goes_too()
    {
        var (manager, _, vm, partial, _) = Scenario(deleteOnRemove: true, completed: false);

        await manager.Remove(vm);

        Assert.False(File.Exists(partial));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public async System.Threading.Tasks.Task A_finished_file_is_never_deleted_however_the_option_is_set()
    {
        var (manager, _, vm, _, final) = Scenario(deleteOnRemove: true, completed: true);

        await manager.Remove(vm);

        Assert.True(File.Exists(final), "a completed download's file is the user's, not the app's to delete");
    }

    private static (DownloadManager Manager, Config Config, DownloadItemViewModel Vm, string Partial, string Final)
        Scenario(bool deleteOnRemove, bool completed)
    {
        Localizer.Instance.Load("en");
        var folder = TempDir();
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.DefaultSavePath = folder;
        config.Settings.DeletePartialFileOnRemove = deleteOnRemove;
        config.DefaultQueue.IsRunning = false;
        manager.Initialize(config);

        var vm = manager.Add(new DownloadItem
        {
            Urls = new List<string> { "https://10.255.255.1/movie.mkv" },
            SaveFolder = folder,
            FileName = "movie.mkv",
        }, autoStart: false);

        var final = Path.Combine(folder, "movie.mkv");
        var partial = final + ".download";
        if (completed)
        {
            vm.Status = DownloadStatus.Completed;
            File.WriteAllText(final, "the whole film");
        }
        else
        {
            File.WriteAllText(partial, "half a film");
        }

        return (manager, config, vm, partial, final);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-rmpartial-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
