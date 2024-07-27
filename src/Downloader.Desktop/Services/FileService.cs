using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Downloader.Desktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

/// <summary>
/// This class provides the needed functions to save and restore a ToDoList. This step is fully optional for this tutorial
/// </summary>
public class FileService : IFileService
{
    // This is a hard coded path to the file. It may not be available on every platform. In your real world App you may 
    // want to make this configurable
    private static readonly string JsonFileName =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader", "downloads.json");

    private readonly Window _target;

    public FileService(Window target)
    {
        _target = target;
    }

    /// <summary>
    /// Stores the given items into a file on disc
    /// </summary>
    /// <param name="itemsToSave">The items to save</param>
    public async Task SaveToFileAsync(IEnumerable<DownloadItem> itemsToSave)
    {
        // Ensure all directories exists
        Directory.CreateDirectory(Path.GetDirectoryName(JsonFileName)!);

        // We use a FileStream to write all items to disc
        await using var fs = File.Create(JsonFileName);
        await JsonSerializer.SerializeAsync(fs, itemsToSave);
    }

    /// <summary>
    /// Loads the file from disc and returns the items stored inside
    /// </summary>
    /// <returns>An IEnumerable of items loaded or null in case the file was not found</returns>
    public async Task<IEnumerable<DownloadItem>?> LoadFromFileAsync()
    {
        try
        {
            // We try to read the saved file and return the ToDoItemsList if successful
            await using var fs = File.OpenRead(JsonFileName);
            return await JsonSerializer.DeserializeAsync<IEnumerable<DownloadItem>>(fs);
        }
        catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
        {
            // In case the file was not found, we simply return null
            return [];
        }
    }

    public async Task<IStorageFile?> OpenFileAsync()
    {
        var files = await _target.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = "Open Text File",
            AllowMultiple = false
        });

        return files.Count >= 1 ? files[0] : null;
    }

    public async Task<IStorageFile?> SaveFileAsync()
    {
        return await _target.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            Title = "Save Text File"
        });
    }
}