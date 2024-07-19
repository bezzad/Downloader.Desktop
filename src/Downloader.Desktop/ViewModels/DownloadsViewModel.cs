using Avalonia.Controls;
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

        // We can use this to add some items for the designer. 
        // You can also use a DesignTime-ViewModel
        if (Design.IsDesignMode)
        {
            DownloadItems = new(new[]
            {
                new DownloadItemViewModel() { FileName = "Hello" },
                new DownloadItemViewModel() { FileName = "Avalonia"}
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

    private string[]? _SelectedFiles;
    public string[]? SelectedFiles
    {
        get { return _SelectedFiles; }
        set { this.RaiseAndSetIfChanged(ref _SelectedFiles, value); }
    }

    private async Task SelectFilesAsync()
    {
        await Task.Delay(2000);
    }
}
