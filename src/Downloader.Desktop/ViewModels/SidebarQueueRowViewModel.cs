using System;
using System.Windows.Input;
using Downloader.Desktop.Models;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// One queue row in the sidebar's Queues section. Clicking it narrows the list to that queue's
/// downloads. (Not the Queues page's <see cref="QueueRowViewModel"/>, which edits the queue.)
/// </summary>
public class SidebarQueueRowViewModel : ViewModelBase
{
    private int _count;
    private bool _isSelected;

    public SidebarQueueRowViewModel(DownloadQueue queue, Action<string> select)
    {
        Queue = queue;
        SelectCommand = ReactiveCommand.Create(() => select?.Invoke(queue.Id));
    }

    public DownloadQueue Queue { get; }

    public string Id => Queue.Id;

    public string Name => Queue.Name;

    /// <summary>How many downloads are in this queue under the status filter and search.</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (_count == value)
                return;

            _count = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => _count == 0;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public ICommand SelectCommand { get; }
}
