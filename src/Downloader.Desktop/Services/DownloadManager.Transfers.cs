using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

// The plugin TRANSFER path: when an enabled plugin's ITransferProvider claims an item's URL (e.g. the
// website plugin's "websitezip:" scheme, a future torrent plugin's "magnet:"), the plugin's ITransfer
// owns the whole download instead of the core HTTP engine. The manager stays the single choke point:
// progress flows through the normal staging pump, Pause/Resume/Cancel route to the transfer, and the
// item obeys its queue's concurrency cap exactly like an engine download (Start is the only entry).
public partial class DownloadManager
{
    /// <summary>Runs a plugin-owned transfer to completion and owns the row's terminal state
    /// (Completed/Failed; a user cancel keeps the Stopped status set by <see cref="Cancel"/>).</summary>
    private async Task RunTransferAsync(DownloadItemViewModel vm, ITransferProvider provider,
        string url, string folder)
    {
        var item = vm.GetItem();
        var cts = new CancellationTokenSource();
        var transfer = provider.Create(url, folder ?? ".");
        OnUi(() =>
        {
            vm.ActiveTransfer = transfer;
            vm.TransferCancellation = cts;
        });
        transfer.ProgressChanged += (_, p) =>
        {
            // Same rule as engine events: staged values are dropped unless Running, so a paused row
            // keeps its last fill. Percentage is 0..100 like the engine's.
            if (vm.Status == DownloadStatus.Running)
                vm.StageProgress(p.Percentage, p.BytesPerSecond, p.BytesReceived, p.TotalBytes);
        };

        try
        {
            var finalPath = await transfer.StartAsync(cts.Token).ConfigureAwait(false);
            var size = SafeLength(finalPath);
            OnUi(() =>
            {
                vm.FileName = Path.GetFileName(finalPath);
                if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(finalPath)))
                    item.SaveFolder = Path.GetDirectoryName(finalPath);
                if (size > 0) { vm.Size = size; vm.Downloaded = size; }
                vm.Progress = 100;
                vm.Status = DownloadStatus.Completed;
                AppLog.Info($"Completed (transfer): {vm.FileName}");
                if (NotifyCompleteEnabled)
                    NotificationService.NotifyCompleted(vm.FileName);
                OfferPostDownloadAction(vm);
                FinishTerminal(vm);
            });
        }
        catch (OperationCanceledException)
        {
            // User cancel — Cancel() already marked the row Stopped (and stopped its queued siblings);
            // just free the slot for whatever is still queued.
            OnUi(() =>
            {
                vm.Speed = 0;
                TryStartNextInQueue(item.QueueId);
                NotifyList();
            });
        }
        catch (Exception ex)
        {
            OnUi(() =>
            {
                vm.ErrorMessage = Describe(ex);
                vm.Status = DownloadStatus.Failed;
                AppLog.Error($"Transfer failed: {url}", ex);
                if (NotifyFailedEnabled)
                    NotificationService.NotifyFailed(vm.FileName ?? url, vm.ErrorMessage);
                FinishTerminal(vm);
            });
        }
        finally
        {
            // Cleared on the UI thread so it can't race Cancel()/Pause() (which run there too).
            OnUi(() =>
            {
                vm.ActiveTransfer = null;
                vm.TransferCancellation = null;
            });
        }
    }
}
