using Downloader.Desktop.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Downloader.Desktop.Services;

/// <summary>
/// Persists the app <see cref="Config"/> (settings, download list, queues, schedules) as JSON.
/// Writes are serialized through a lock so frequent setting changes can't corrupt the file.
/// </summary>
public class FileService : IFileService
{
    private static readonly string DefaultConfigFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader", "config.json");

    /// <summary>
    /// Test seam. The real path is resolved from the user's own %AppData%/~/.config, so exercising the
    /// save path would overwrite the developer's live config.json — which is why none of this was ever
    /// covered. Tests point it at a temp file; the app never sets it.
    /// </summary>
    internal static string ConfigFileOverride { get; set; }

    private static string ConfigFileName => ConfigFileOverride ?? DefaultConfigFile;

    private static readonly SemaphoreSlim WriteGate = new(1, 1);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Stores the given items into a file on disc
    /// </summary>
    /// <param name="itemToSave">The item to save</param>
    public async Task SaveToFileAsync(Config itemToSave)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFileName)!);

        // Serialize concurrent writes (autosave, settings changes, shutdown) and write atomically
        // via a temp file so a mid-write crash can't leave a truncated config.
        // ConfigureAwait(false) keeps continuations off the UI thread so a blocking .Wait()
        // during shutdown can't deadlock.
        await WriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tmp = ConfigFileName + ".tmp";
            await using (var fs = File.Create(tmp))
                await JsonSerializer.SerializeAsync(fs, itemToSave, Options).ConfigureAwait(false);
            File.Move(tmp, ConfigFileName, overwrite: true);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    /// <summary>
    /// Loads the file from disc and returns the items stored inside
    /// </summary>
    /// <returns>An IEnumerable of items loaded or null in case the file was not found</returns>
    public async Task<Config> LoadFromFileAsync()
    {
        try
        {
            // We try to read the saved file and return the stored config if successful
            await using var fs = File.OpenRead(ConfigFileName);
            var config = await JsonSerializer.DeserializeAsync<Config>(fs, Options);
            return config ?? Config.New();
        }
        catch (Exception)
        {
            // Missing, unreadable or incompatible (older schema) file — fall back to defaults
            // rather than crashing on startup.
            return Config.New();
        }
    }
}