﻿using Avalonia.Platform.Storage;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Downloader.Desktop.ViewModels;

namespace Downloader.Desktop.Services;

/// <summary>
/// A helper class to manage dialogs via extension methods. Add more on your own
/// </summary>
public static class DialogHelper
{
    public static Window GetMainWindow()
    {
        // Access the main window to open the file dialog
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
    }

    public static async Task<TResult> ShowDialog<TV, TVm, TResult>(TV view, TVm viewModel)
        where TV : Window
        where TVm : ViewModelBase
    {
        // Access the main window to open the modal dialog
        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            view.DataContext = viewModel;
            viewModel.View = view;

            // Show as a modal dialog and wait for it to close
            return await view.ShowDialog<TResult>(mainWindow);
        }

        return default;
    }

    public static async Task<Uri> OpenFolderPicker(string title)
    {
        var topWindow = GetMainWindow();
        if (topWindow != null)
        {
            var result = await topWindow.StorageProvider.OpenFolderPickerAsync(
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