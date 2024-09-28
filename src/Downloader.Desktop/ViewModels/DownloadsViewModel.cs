using Avalonia.Controls;
using Downloader.Desktop.Models;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

public class DownloadsViewModel : ViewModelBase
{
    public DownloadsViewModel()
    {
        SelectFilesCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
        RemoveItemCommand = ReactiveCommand.CreateFromTask<DownloadItemViewModel>(RemoveDownloadItem);

        // We can use this to add some items for the designer. 
        // You can also use a DesignTime-ViewModel
        if (Design.IsDesignMode)
        {
            DownloadItems = new(new[]
            {
                new DownloadItemViewModel() { FileName = "Hello" },
                new DownloadItemViewModel() { FileName = "Downloader" }
            });
        }

        // We can listen to any property changes with "WhenAnyValue" and do whatever we want in "Subscribe".
        //this.WhenAnyValue(o => o.Name)
        //.Subscribe(o => this.RaisePropertyChanged(nameof(Greeting)));
    }

    /// <summary>
    /// Gets a collection of <see cref="DownloadItem"/> which allows adding and removing items
    /// </summary>
    public ObservableCollection<DownloadItemViewModel> DownloadItems { get; } = new();

    public ICommand SelectFilesCommand { get; }
    public ICommand RemoveItemCommand { get; }

    private string[] _selectedFiles;

    public string[] SelectedFiles
    {
        get => _selectedFiles;
        set => this.RaiseAndSetIfChanged(ref _selectedFiles, value);
    }

    private async Task SelectFilesAsync()
    {
        await Task.Delay(2000);
    }

    private async Task RemoveDownloadItem(DownloadItemViewModel item)
    {
        DownloadItems.Remove(item);
        await Task.Delay(2000);
    }
}