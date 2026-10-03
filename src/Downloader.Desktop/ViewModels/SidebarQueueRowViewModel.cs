using System;
using System.Windows.Input;
using Downloader.Desktop.Models;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// One queue in the sidebar's Queues section. Clicking it narrows the list to that queue's downloads.
/// (Named apart from the Queues page's <see cref="QueueRowViewModel"/>, which is a full queue card.)
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

    /// <summary>How many of this queue's downloads pass the status filter and the search box.</summary>
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

    /// <summary>An empty queue stays listed, de-emphasized, like an empty category.</summary>
    public bool IsEmpty => _count == 0;

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public ICommand SelectCommand { get; }
}
