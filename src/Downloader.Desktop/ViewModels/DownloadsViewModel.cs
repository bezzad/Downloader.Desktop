using System;
using Avalonia.Controls;
using Downloader.Desktop.Models;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.ViewModels;

public class DownloadsViewModel : ViewModelBase
{
    private readonly IFileService _fileService;

    public DownloadsViewModel(IFileService fileService)
    {
        _fileService = fileService ?? throw new NullReferenceException("Missing File Service instance.");
        SelectFilesCommand = ReactiveCommand.CreateFromTask(SelectFilesAsync);
        RemoveItemCommand = ReactiveCommand.CreateFromTask<DownloadItemViewModel>(RemoveDownloadItem);

        // We can use this to add some items for the designer. 
        // You can also use a DesignTime-ViewModel
        if (Design.IsDesignMode)
        {
            DownloadItems = new(new[]
            {
                new DownloadItemViewModel() { FileName = "Hello" },
                new DownloadItemViewModel() { FileName = "Avalonia" }
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

    private async Task RemoveDownloadItem(DownloadItemViewModel item)
    {
        DownloadItems.Remove(item);
        await Task.Delay(2000);
    }

    public async Task SaveDownloadItems()
    {
        // To save the items, we map them to the ToDoItem-Model which is better suited for I/O operations
        var itemsToSave = DownloadItems.Select(item => item.GetItem());
        await _fileService.SaveToFileAsync(itemsToSave);
    }
}