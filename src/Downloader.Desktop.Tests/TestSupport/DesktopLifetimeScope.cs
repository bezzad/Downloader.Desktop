using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Downloader.Desktop.Tests;

/// <summary>
/// Gives the headless runtime a classic desktop lifetime with a real main window, for the duration of
/// one test.
///
/// Why this exists: <c>DialogHelper</c> resolves everything through
/// <c>Application.Current.ApplicationLifetime.MainWindow</c>, and every one of its entry points begins
/// with "if there is no main window, do nothing". Under the headless runtime the lifetime is null, so
/// the whole file used to no-op — the dialog code read as covered-by-early-return while none of it had
/// ever run. With a lifetime installed the real flows (show, persist the size, track the one open
/// modal, return the result) execute.
///
/// <para><c>Application.ApplicationLifetime</c>'s setter refuses to run once the app is initialised, so
/// the backing field is set directly. That is a deliberate, test-only reach into Avalonia: if a future
/// Avalonia renames the field this throws with the name it looked for, rather than silently going back
/// to no-opping and quietly dropping the coverage.</para>
/// </summary>
internal sealed class DesktopLifetimeScope : IDisposable
{
    private static readonly FieldInfo LifetimeField =
        typeof(Application).GetField("_applicationLifetime", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "Avalonia's Application._applicationLifetime is gone — DesktopLifetimeScope needs updating.");

    private readonly Application _app;
    private readonly object _previous;

    public Window MainWindow { get; }

    public DesktopLifetimeScope()
    {
        _app = Application.Current ?? throw new InvalidOperationException("No Avalonia application.");
        _previous = LifetimeField.GetValue(_app);

        MainWindow = new Window { Width = 800, Height = 600 };
        LifetimeField.SetValue(_app, new ClassicDesktopStyleApplicationLifetime { MainWindow = MainWindow });
        MainWindow.Show();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Runs pending dispatcher work — dialogs are shown and closed across dispatcher turns.</summary>
    public static void Pump(int turns = 4)
    {
        for (var i = 0; i < turns; i++)
            Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        try { Services.DialogHelper.CloseOpenModals(); } catch { /* best-effort */ }
        Pump();
        try { MainWindow.Close(); } catch { /* best-effort */ }
        Pump();
        LifetimeField.SetValue(_app, _previous);
    }
}
