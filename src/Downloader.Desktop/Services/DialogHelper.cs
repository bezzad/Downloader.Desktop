using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;

namespace Downloader.Desktop.Services;

/// <summary>
/// A helper class to manage dialogs via extension methods. Add more on your own
/// </summary>
public static class DialogHelper
{
    public static IClassicDesktopStyleApplicationLifetime AppLifetime =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
    
    public static Window MainWindow => AppLifetime?.MainWindow;
    
    public static async Task<TResult> ShowDialog<TV, TVm, TResult>(TV view, TVm viewModel)
        where TV : Window
        where TVm : ViewModelBase
    {
        // Access the main window to open the modal dialog
        if (MainWindow != null)
        {
            view.DataContext = viewModel;
            viewModel.View = view;

            // Show as a modal dialog and wait for it to close
            return await view.ShowDialog<TResult>(MainWindow);
        }

        return default;
    }

    /// <summary>Opens the read-only details dialog for a download (info + live per-part progress).</summary>
    public static async Task ShowDetails(DownloadItemViewModel item)
    {
        if (MainWindow == null || item == null)
            return;

        var view = new DownloadDetailsView();
        var viewModel = new DownloadDetailsViewModel(item);
        view.DataContext = viewModel;
        view.Closed += (_, _) => viewModel.Cleanup();

        await view.ShowDialog(MainWindow);
    }

    public static async Task<Uri> OpenFolderPicker(string title)
    {
        if (MainWindow != null)
        {
            var result = await MainWindow.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions()
                {
                    Title = title,
                    AllowMultiple = false
                });

            // Check if a file was selected
            if (result.Count > 0)
            {
                // Set the selected file path in the TextBox
                return result[0].Path;
            }
        }

        return new Uri("~");
    }
}