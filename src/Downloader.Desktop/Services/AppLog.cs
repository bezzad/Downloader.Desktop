using System;
using System.IO;

namespace Downloader.Desktop.Services;

/// <summary>
/// Lightweight, opt-in file logger for app/download diagnostics. Disabled by default; the user
/// turns it on in Settings. Writes one daily file under the app data "logs" folder.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static bool _enabled;

    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader", "logs");

    public static string CurrentLogFile => Path.Combine(LogFolder, $"downloader-{DateTime.Now:yyyy-MM-dd}.log");

    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
            Info("Logging enabled.");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception ex = null) =>
        Write("ERROR", ex == null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        if (!_enabled)
            return;

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogFolder);
                File.AppendAllText(CurrentLogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
