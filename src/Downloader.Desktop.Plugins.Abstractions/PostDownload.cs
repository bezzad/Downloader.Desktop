namespace Downloader.Desktop.Plugins;

// ── Phase 4: POST-DOWNLOAD ACTION (optional, user-initiated) ──────────────────────────────────────
//
// A post-download action is something a plugin can OFFER on a finished download that its own resolver
// produced (e.g. the Ollama plugin's "Add to Ollama" on a downloaded model blob). Unlike a
// post-processor it is NOT part of the download pipeline — the host surfaces it as a button on the
// completion notification / the finished item, and it runs only when the user clicks it.

/// <summary>A user-initiated action a plugin offers on a completed download it resolved.</summary>
public interface IPostDownloadAction
{
    /// <summary>Short button label, e.g. "Add to Ollama".</summary>
    string Label { get; }

    /// <summary>Cheap, pure check: does this action apply to this finished download?
    /// <paramref name="sourceUrl"/> is the ORIGINAL input the user added (before resolving);
    /// <paramref name="filePath"/> is the finished file on disk.</summary>
    bool CanOffer(string sourceUrl, string filePath);

    /// <summary>Run the action (on user click). Throw with a clear message on failure — the host shows
    /// it as the item's friendly error. Must never modify or delete the downloaded file.</summary>
    Task ExecuteAsync(string sourceUrl, string filePath, IProgress<double>? progress,
        CancellationToken cancellationToken);
}
