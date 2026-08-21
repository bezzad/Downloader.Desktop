namespace Downloader.Desktop.Plugins.Hls;

/// <summary>
/// Shared handling for the external tools this plugin provisions (ffmpeg).
/// <para>
/// The rule this exists to enforce: a tool downloaded straight to its final path leaves a TRUNCATED
/// file behind when the app is closed or killed mid-download, and an existence-only "is it installed?"
/// check then treats that corpse as installed forever — the tool can never start and every extraction
/// fails with no way back (seen in the wild: a 23 MB fragment of a 40 MB yt-dlp, still without its
/// executable bit, months old). So downloads land on a temporary path and are moved into place only
/// once complete, and "installed" means present, plausibly sized AND runnable.
/// </para>
/// </summary>
internal static class BinaryFile
{
    /// <summary>Below this, a "downloaded tool" is an error page or a fragment, not a binary.</summary>
    internal const long MinUsableBytes = 1024 * 1024;

    /// <summary>Is this path a tool we can actually run? Existence alone is not enough.</summary>
    internal static bool IsUsable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MinUsableBytes) return false;
            if (OperatingSystem.IsWindows()) return true;
            // On Unix the executable bit is set only after a download finishes, so its absence is a
            // reliable "this was never completed" marker for already-broken installs.
            return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Drop an unusable leftover so the caller re-downloads instead of failing forever.</summary>
    internal static void DeleteIfPresent(string path)
    {
        try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Download to a temporary sibling and move it into place only once it is complete, so an
    /// interrupted download can never masquerade as an installed tool.</summary>
    internal static async Task DownloadToAsync(HttpClient http, string url, string path, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial";
        try
        {
            await using (var fs = File.Create(partial))
            await using (var stream = await http.GetStreamAsync(url, ct).ConfigureAwait(false))
                await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            DeleteIfPresent(partial);
            throw;
        }
    }

    /// <summary>Mark a downloaded tool runnable (no-op on Windows). In-process — spawning <c>chmod</c>
    /// can silently fail under sandboxes such as snap confinement.</summary>
    internal static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
    }
}
