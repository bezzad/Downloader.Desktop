using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Services;

/// <summary>
/// Opt-in file logger for app/download diagnostics. Off by default; when the user turns it on it is
/// fully detailed (Debug and above). Writes one daily file under the app data "logs" folder and keeps
/// <see cref="KeepDays"/> days of them. Also exposes an <see cref="ILoggerFactory"/> so the
/// <c>Downloader</c> engine and the plugins write into the same file.
/// Never write a raw URL, cookie or header here — pass URLs through <see cref="LogText.Url"/>.
/// </summary>
public static class AppLog
{
    /// <summary>How many days of log files are kept.</summary>
    public const int KeepDays = 7;

    private static readonly object Gate = new();
    private static bool _enabled;
    private static StreamWriter _writer;
    private static string _writerPath;

    /// <summary>Settable by tests only, so a test never writes into the real logs folder.</summary>
    public static string LogFolder { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader", "logs");

    public static string CurrentLogFile => Path.Combine(LogFolder, FileNameFor(DateTime.Now));

    /// <summary>Shared factory handed to the engine so its internal logs land in our file.</summary>
    public static ILoggerFactory Factory { get; } = Microsoft.Extensions.Logging.LoggerFactory.Create(b =>
        b.SetMinimumLevel(LogLevel.Debug).AddProvider(new AppLoggerProvider()));

    public static bool IsEnabled => _enabled;

    public static void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
            return;
        if (enabled)
        {
            _enabled = true;
            Info("Logging enabled.");
            return;
        }

        Info("Logging disabled.");
        _enabled = false;
        lock (Gate)
            CloseWriter();
    }

    /// <summary>Writes the session header (version, OS, settings …) as a block of lines.</summary>
    public static void WriteHeader(string header)
    {
        if (!_enabled || string.IsNullOrEmpty(header))
            return;
        Write("INFO", "===== Session start =====");
        foreach (var line in header.Split('\n'))
            if (line.Trim().Length > 0)
                Write("INFO", line.TrimEnd('\r'));
    }

    public static void Debug(string message) => Write("DEBUG", message);
    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    /// <summary>Logs an error with the FULL exception (type, message, inner exceptions, stack).</summary>
    public static void Error(string message, Exception ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}{Environment.NewLine}{ex}");

    internal static void Write(string level, string message)
    {
        if (!_enabled)
            return;

        try
        {
            lock (Gate)
            {
                var path = CurrentLogFile;
                if (_writer == null || _writerPath != path)
                {
                    // A new day (or a first write): open the new file and drop the old ones.
                    CloseWriter();
                    Directory.CreateDirectory(LogFolder);
                    Prune(LogFolder, DateTime.Now, KeepDays);
                    _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
                    _writerPath = path;
                }

                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
            }
        }
        catch
        {
            // Logging must never break the app; a failed write is dropped.
            CloseWriter();
        }
    }

    private static void CloseWriter()
    {
        try { _writer?.Dispose(); }
        catch { /* never throw from logging */ }
        _writer = null;
        _writerPath = null;
    }

    private static string FileNameFor(DateTime day) => $"downloader-{day:yyyy-MM-dd}.log";

    /// <summary>
    /// Deletes this app's log files (<c>downloader-yyyy-MM-dd.log</c>) dated more than
    /// <paramref name="days"/> days before <paramref name="now"/>. Any other file is left alone.
    /// </summary>
    public static void Prune(string folder, DateTime now, int days)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;
            var oldest = now.Date.AddDays(-days);
            foreach (var file in Directory.GetFiles(folder, "downloader-*.log"))
            {
                var stamp = Path.GetFileNameWithoutExtension(file)["downloader-".Length..];
                if (DateTime.TryParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var day) && day < oldest)
                    File.Delete(file);
            }
        }
        catch
        {
            // best-effort; a locked file is tried again next time
        }
    }

    /// <summary>Every log file retention keeps, oldest first.</summary>
    public static string[] KeptFiles()
    {
        if (!Directory.Exists(LogFolder))
            return [];
        var files = Directory.GetFiles(LogFolder, "downloader-*.log");
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Saves every kept log file into one zip at <paramref name="zipPath"/>. Returns how many files went
    /// in; 0 means there was nothing to export and no zip was written.
    /// </summary>
    public static int ExportZip(string zipPath)
    {
        var files = KeptFiles();
        if (files.Length == 0)
            return 0;

        lock (Gate)
        {
            using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
            foreach (var file in files)
            {
                // Today's file is held open by the writer, so read it with sharing on.
                using var source = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var entry = zip.CreateEntry(Path.GetFileName(file)).Open();
                source.CopyTo(entry);
            }
        }
        return files.Length;
    }

    /// <summary>Bridges <see cref="ILogger"/> calls from packages into the app log file.</summary>
    private sealed class AppLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new AppLogger(categoryName);
        public void Dispose() { }
    }

    private sealed class AppLogger : ILogger
    {
        private readonly string _category;
        public AppLogger(string category) => _category = category;

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        // Respect the user's toggle; when off, nothing is written anyway.
        public bool IsEnabled(LogLevel logLevel) => AppLog._enabled && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var level = logLevel switch
            {
                LogLevel.Trace or LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                _ => "ERROR"
            };
            var shortCat = _category?.Split('.') is { Length: > 0 } parts ? parts[^1] : _category;
            var msg = formatter(state, exception);
            if (exception != null)
                msg += Environment.NewLine + exception;
            Write(level, $"[{shortCat}] {msg}");
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
