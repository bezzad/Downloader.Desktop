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

    /// <summary>Copies text to the system clipboard (best-effort).</summary>
    public static async Task CopyTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var clipboard = (MainWindow as TopLevel)?.Clipboard;
        if (clipboard != null)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
    }
    
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

    /// <summary>Opens the modal About dialog (app identity, donate, links and contacts).</summary>
    public static async Task ShowAbout()
    {
        if (MainWindow == null)
            return;

        var view = new AboutView { DataContext = new AboutViewModel() };
        await view.ShowDialog(MainWindow);
    }

    /// <summary>Asks the user where to save a file; returns the chosen path or null.</summary>
    public static async Task<Uri> SaveFilePicker(string title, string suggestedName)
    {
        if (MainWindow == null)
            return null;

        var file = await MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName
        });

        return file?.Path;
    }

    public static async Task<Uri> OpenFolderPicker(string title, Window owner = null)
    {
        // Parent the picker to the active window (e.g. the Add dialog) so it stays on top,
        // falling back to the main window.
        var parent = owner ?? MainWindow;
        if (parent != null)
        {
            var result = await parent.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions()
                {
                    Title = title,
                    AllowMultiple = false
                });

            if (result.Count > 0)
                return result[0].Path;
        }

        // Cancelled / nothing chosen — return null so callers skip it. (Returning a bogus relative
        // Uri here previously threw UriFormatException when callers read .LocalPath. #21)
        return null;
    }
}