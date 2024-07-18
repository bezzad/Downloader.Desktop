using Downloader.Desktop.Models;
using ReactiveUI;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

public class DownloadItemViewModel : ViewModelBase
{
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

    private DownloadItem _item;
    
    public ICommand SelectFilesCommand { get; }
    
    public string? FileName
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
    
    public string? Url
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

    public DownloadItem GetItem()
    {
        return _item;
    }
}
