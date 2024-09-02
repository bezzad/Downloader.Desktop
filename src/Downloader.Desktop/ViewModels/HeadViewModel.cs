using Downloader.Desktop.Services;
using ReactiveUI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Downloader.Desktop.ViewModels;

public class HeadViewModel : ViewModelBase
{
    public HeadViewModel()
    {
        AddUrlCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
    }

    public ICommand AddUrlCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand StopItemCommand { get; }
    public ICommand StartItemCommand { get; }
    public ICommand ShowOptionsViewCommand { get; }
    public string DownloadUrl { get; set; }

    /// <summary>
    /// Gets or sets a list of Files
    /// </summary>
    private IEnumerable<string>? _SelectedFiles;
    private IEnumerable<string>? SelectedFiles
    {
        get { return _SelectedFiles; }
        set { this.RaiseAndSetIfChanged(ref _SelectedFiles, value); }
    }

    /// <summary>
    /// A command used to select some files
    /// </summary>
    private async Task SelectFilesAsync()
    {
        SelectedFiles = await this.OpenFileDialogAsync("Hello Avalonia");
    }
}
