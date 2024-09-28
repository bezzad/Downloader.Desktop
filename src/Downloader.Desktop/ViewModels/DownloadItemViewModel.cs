using Downloader.Desktop.Models;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

public class DownloadItemViewModel : ViewModelBase
{
    private DownloadItem _item;
    private bool _isChecked;

    /// <summary>
    /// Creates a new blank ToDoItemViewModel
    /// </summary>
    public DownloadItemViewModel()
    {
        _item = new();
    }

    public DownloadItemViewModel(DownloadItem item)
    {
        _item = item ?? new();
    }

    public string FileName
    {
        get => _item.FileName;
        set
        {
            if (_item.FileName != value)
            {
                this.RaisePropertyChanged(nameof(FileName));
                _item.FileName = value;
            }
        }
    }

    public long? Size
    {
        get => _item.Size;
        set
        {
            if (_item.Size != value)
            {
                this.RaisePropertyChanged(nameof(Size));
                _item.Size = value;
            }
        }
    }

    public long Downloaded
    {
        get => _item.Downloaded;
        set
        {
            if (_item.Downloaded != value)
            {
                this.RaisePropertyChanged(nameof(Downloaded));
                _item.Downloaded = value;
            }
        }
    }

    public long TransferRate { get; set; }

    public string Status =>
        _item.Size > 0
            ? (_item.Downloaded / _item.Size * 100) + "%"
            : _item.Status.ToString();

    public string LastTry => _item.LastTry?.ToString("dd MMM yyyy");

    public string Url
    {
        get => _item.Url;
        set
        {
            if (_item.Url != value)
            {
                this.RaisePropertyChanged(nameof(Url));
                _item.Url = value;
            }
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => this.RaiseAndSetIfChanged(ref _isChecked, value);
    }

    public DownloadItem GetItem()
    {
        return _item;
    }
}
