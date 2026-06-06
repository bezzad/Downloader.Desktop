using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using Downloader.Desktop.Models;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// Default <see cref="IDownloadManager"/>. Builds <see cref="IDownload"/> instances via
/// <see cref="DownloadBuilder"/>, marshals engine events onto the UI thread, and updates the
/// matching <see cref="DownloadItemViewModel"/>. Queue concurrency / scheduling are layered on later.
/// </summary>
public class DownloadManager : IDownloadManager
{
    private Config _config;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    public void Initialize(Config config)
    {
        _config = config ?? Config.New();
        Items.Clear();
        foreach (var item in _config.Downloads ?? new List<DownloadItem>())
        {
            Items.Add(new DownloadItemViewModel(item, this));
        }
    }

    public DownloadItemViewModel Add(DownloadItem item, bool autoStart)
    {
        if (string.IsNullOrWhiteSpace(item.QueueId) && _config != null)
            item.QueueId = _config.DefaultQueue.Id;

        var vm = new DownloadItemViewModel(item, this);
        Items.Add(vm);
        if (autoStart)
            Start(vm);

        return vm;
    }

    public async void Start(DownloadItemViewModel vm)
    {
        var item = vm.GetItem();
        if (string.IsNullOrWhiteSpace(item.Url))
            return;

        try
        {
            var folder = string.IsNullOrWhiteSpace(item.SaveFolder)
                ? _config?.Settings?.DefaultSavePath
                : item.SaveFolder;
            item.SaveFolder = folder;

            var builder = DownloadBuilder.New()
                .WithUrl(item.Url)
                .WithDirectory(folder ?? string.Empty);

            // Only force a name when the user supplied one; otherwise let the engine
            // resolve the real file name from the URL / Content-Disposition headers.
            if (!string.IsNullOrWhiteSpace(item.FileName))
                builder = builder.WithFileName(item.FileName);

            var download = builder
                .WithConfiguration(_config?.Settings?.ToConfiguration() ?? new DownloadConfiguration())
                .Build();

            Attach(vm, download);
            item.LastTry = DateTime.Now;
            vm.Status = DownloadStatus.Running;
            await download.StartAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            OnUi(() => vm.Status = DownloadStatus.Failed);
        }
    }

    public void Pause(DownloadItemViewModel vm)
    {
        vm.Download?.Pause();
        vm.Status = DownloadStatus.Paused;
        vm.Speed = 0;
    }

    public void Resume(DownloadItemViewModel vm)
    {
        if (vm.Download != null && vm.Status == DownloadStatus.Paused)
        {
            vm.Download.Resume();
            vm.Status = DownloadStatus.Running;
        }
        else
        {
            // No live handle (stopped or freshly loaded) — (re)build and start.
            Start(vm);
        }
    }

    public void Cancel(DownloadItemViewModel vm)
    {
        vm.Download?.Stop();
        vm.Status = DownloadStatus.Stopped;
        vm.Speed = 0;
    }

    public void Retry(DownloadItemViewModel vm) => Start(vm);

    public Task Remove(DownloadItemViewModel vm)
    {
        try
        {
            vm.Download?.Stop();
        }
        catch
        {
            // best-effort stop before removal
        }

        Items.Remove(vm);
        return Task.CompletedTask;
    }

    public void StartAll()
    {
        foreach (var vm in Items.Where(v => v.CanResume).ToList())
            Resume(vm);
    }

    public void StopAll()
    {
        foreach (var vm in Items.Where(v => v.Status == DownloadStatus.Running).ToList())
            Pause(vm);
    }

    public void ClearCompleted()
    {
        foreach (var vm in Items.Where(v => v.IsCompleted).ToList())
            Items.Remove(vm);
    }

    private void Attach(DownloadItemViewModel vm, IDownload download)
    {
        vm.Download = download;

        download.DownloadStarted += (_, e) => OnUi(() =>
        {
            // The engine has now resolved the real file name (from URL / Content-Disposition).
            if (string.IsNullOrWhiteSpace(vm.FileName) && !string.IsNullOrWhiteSpace(download.Filename))
                vm.FileName = download.Filename;
            if (!string.IsNullOrWhiteSpace(download.Folder))
                vm.GetItem().SaveFolder = download.Folder;
            if (e.TotalBytesToReceive > 0)
                vm.Size = e.TotalBytesToReceive;
            vm.Status = DownloadStatus.Running;
        });

        download.DownloadProgressChanged += (_, e) => OnUi(() =>
        {
            vm.Progress = e.ProgressPercentage;
            vm.Speed = e.BytesPerSecondSpeed;
            vm.Downloaded = e.ReceivedBytesSize;
            if (vm.Size is null or 0 && e.TotalBytesToReceive > 0)
                vm.Size = e.TotalBytesToReceive;
        });

        download.DownloadFileCompleted += (_, e) => OnUi(() =>
        {
            vm.Speed = 0;
            if (e.Cancelled)
            {
                // Distinguish a user pause (live handle kept) from a hard stop.
                if (vm.Status != DownloadStatus.Paused)
                    vm.Status = DownloadStatus.Stopped;
            }
            else if (e.Error != null)
            {
                vm.Status = DownloadStatus.Failed;
            }
            else
            {
                vm.Progress = 100;
                vm.Status = DownloadStatus.Completed;
            }
        });
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
