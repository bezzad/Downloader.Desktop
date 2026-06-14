using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Downloader.Desktop.Services;

/// <summary>
/// Lightweight, opt-in file logger for app/download diagnostics. Disabled by default; the user
/// turns it on in Settings. Writes one daily file under the app data "logs" folder. Also exposes
/// an <see cref="ILoggerFactory"/> so the lower-level <c>Downloader</c> engine (and any other
/// package that accepts <see cref="ILogger"/>) writes into the same file when logging is enabled.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static bool _enabled;

    public static string LogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downloader", "logs");

    public static string CurrentLogFile => Path.Combine(LogFolder, $"downloader-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>Shared factory handed to the engine so its internal logs land in our file.</summary>
    public static ILoggerFactory Factory { get; } =
        Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddProvider(new AppLoggerProvider()));

    public static bool IsEnabled => _enabled;

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

    internal static void Write(string level, string message)
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
                msg += $" :: {exception.GetType().Name}: {exception.Message}";
            Write(level, $"[{shortCat}] {msg}");
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
