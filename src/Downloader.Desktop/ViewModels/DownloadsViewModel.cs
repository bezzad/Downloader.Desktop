using ReactiveUI;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

public class DownloadsViewModel : ViewModelBase
{
    public DownloadsViewModel()
    {
        SelectFilesCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
    }

    public ICommand SelectFilesCommand { get; }

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
