using System;
using System.IO;
using Downloader.Desktop.Services;

namespace Downloader.Desktop.Tests;

/// <summary>Points <see cref="AppLog"/> at a temp folder and turns it on for one test; restores both on
/// dispose. The logger is process-wide, so every test that reads the log must go through this.</summary>
public sealed class LogScope : IDisposable
{
    private readonly string _oldFolder = AppLog.LogFolder;
    private readonly bool _wasEnabled = AppLog.IsEnabled;

    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "dldesktop-log-" + Guid.NewGuid().ToString("N"));

    public LogScope(bool enable = true)
    {
        AppLog.SetEnabled(false); // closes any writer pointing at the old folder
        Directory.CreateDirectory(Folder);
        AppLog.LogFolder = Folder;
        AppLog.SetEnabled(enable);
    }

    /// <summary>Everything written so far (read with sharing on — the writer keeps the file open).</summary>
    public string Text()
    {
        var file = AppLog.CurrentLogFile;
        if (!File.Exists(file))
            return "";
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    public void Dispose()
    {
        AppLog.SetEnabled(false);
        AppLog.LogFolder = _oldFolder;
        AppLog.SetEnabled(_wasEnabled);
        try { Directory.Delete(Folder, recursive: true); } catch { /* best-effort */ }
    }
}
