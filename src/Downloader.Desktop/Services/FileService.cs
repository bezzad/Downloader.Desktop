using Downloader.Desktop.Models;
using System;
using System.IO;
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
    private static readonly string ConfigFileName =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader", "config.json");

    /// <summary>
    /// Stores the given items into a file on disc
    /// </summary>
    /// <param name="itemToSave">The item to save</param>
    public async Task SaveToFileAsync(Config itemToSave)
    {
        // Ensure all directories exists
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFileName)!);

        // We use a FileStream to write all items to disc
        await using var fs = File.Create(ConfigFileName);
        await JsonSerializer.SerializeAsync(fs, itemToSave);
    }

    /// <summary>
    /// Loads the file from disc and returns the items stored inside
    /// </summary>
    /// <returns>An IEnumerable of items loaded or null in case the file was not found</returns>
    public async Task<Config> LoadFromFileAsync()
    {
        try
        {
            // We try to read the saved file and return the ToDoItemsList if successful
            await using var fs = File.OpenRead(ConfigFileName);
            var config = await JsonSerializer.DeserializeAsync<Config>(fs);
            if (config is null)
                config = Config.New();

            return config;
        }
        catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
        {
            // In case the file was not found, we simply return default config
            return Config.New();
        }
    }
}