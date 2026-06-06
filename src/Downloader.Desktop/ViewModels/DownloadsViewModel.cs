using System;
using Avalonia.Collections;
using Avalonia.Controls;
using Downloader.Desktop.Services;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The downloads table page. Wraps the manager's master collection in a filterable
/// <see cref="DataGridCollectionView"/> driven by the selected status filter + search text.
/// </summary>
public class DownloadsViewModel : ViewModelBase
{
    private readonly IDownloadManager _manager;
    private StatusFilter _filter = StatusFilter.All;
    private string _search;

    /// <summary>Design-time constructor with sample rows.</summary>
    public DownloadsViewModel()
    {
        var sample = new ObservableCollection<DownloadItemViewModel>(new[]
        {
            new DownloadItemViewModel { FileName = "ubuntu-24.04.iso" },
            new DownloadItemViewModel { FileName = "podcast-ep12.mp3" }
        });
        ItemsView = new DataGridCollectionView(sample);
        RemoveItemCommand = ReactiveCommand.CreateFromTask<DownloadItemViewModel>(RemoveDownloadItem);
    }

    public DownloadsViewModel(IDownloadManager manager)
    {
        _manager = manager;
        ItemsView = new DataGridCollectionView(manager.Items) { Filter = Matches };
        RemoveItemCommand = ReactiveCommand.CreateFromTask<DownloadItemViewModel>(RemoveDownloadItem);
    }

    /// <summary>Filterable view bound to the DataGrid.</summary>
    public DataGridCollectionView ItemsView { get; }

    public ICommand RemoveItemCommand { get; }

    public bool IsEmpty => ItemsView is null || ItemsView.Count == 0;

    public StatusFilter Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            Refresh();
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            _search = value;
            Refresh();
        }
    }

    /// <summary>Re-evaluates the filter (call when items or their statuses change).</summary>
    public void Refresh()
    {
        ItemsView?.Refresh();
        this.RaisePropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(object o)
    {
        if (o is not DownloadItemViewModel vm)
            return false;

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var s = _search.Trim();
            var inName = vm.FileName?.Contains(s, StringComparison.OrdinalIgnoreCase) == true;
            var inUrl = vm.Url?.Contains(s, StringComparison.OrdinalIgnoreCase) == true;
            if (!inName && !inUrl)
                return false;
        }

        return _filter switch
        {
            StatusFilter.Active => vm.Status is DownloadStatus.Running or DownloadStatus.Paused or DownloadStatus.Created,
            StatusFilter.Completed => vm.Status == DownloadStatus.Completed,
            StatusFilter.Failed => vm.Status is DownloadStatus.Failed or DownloadStatus.Stopped,
            _ => true
        };
    }

    private async Task RemoveDownloadItem(DownloadItemViewModel item)
    {
        if (_manager != null)
            await _manager.Remove(item);
        Refresh();
    }
}
