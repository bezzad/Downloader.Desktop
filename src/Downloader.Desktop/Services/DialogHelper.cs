using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Downloader.Desktop.Models;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;

namespace Downloader.Desktop.Services;

/// <summary>
/// A helper class to manage dialogs via extension methods. Add more on your own
/// </summary>
public static class DialogHelper
{
    public const string AddDownloadWindowKey = "AddDownload";
    public const string DetailsWindowKey = "Details";

    public static IClassicDesktopStyleApplicationLifetime AppLifetime =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public static Window MainWindow => AppLifetime?.MainWindow;

    /// <summary>The window the user is currently looking at — the front-most open dialog if one is up,
    /// else the main window. File pickers must parent to this; opening one from the (background) MainWindow
    /// while a modal dialog is in front opens behind it / fails on some Linux WMs.</summary>
    public static Window ActiveWindow =>
        AppLifetime?.Windows?.LastOrDefault(w => w.IsActive) ?? MainWindow;

    private static readonly List<Window> Modals = [];

    /// <summary>The modal dialogs currently on screen (at most one, see <see cref="BeginModal"/>).</summary>
    internal static IReadOnlyList<Window> OpenModals => Modals;

    /// <summary>Closes every modal dialog that is still open.</summary>
    public static void CloseOpenModals()
    {
        // Snapshot: Close() raises Closed, which removes the entry from Modals.
        foreach (var modal in Modals.ToList())
            modal.Close();
        Modals.Clear();
    }

    /// <summary>Registers <paramref name="view"/> as the one open modal, closing any that is still up.
    /// Every dialog is owned by <see cref="MainWindow"/>, so a modal opened from inside another one
    /// (Donate from About) is its SIBLING, not its child — the owner raises the first dialog back above
    /// the new one and it appears to open underneath. Only ever keep one modal on screen.</summary>
    internal static void BeginModal(Window view)
    {
        CloseOpenModals();

        if (view == null)
            return;

        Modals.Add(view);
        view.Closed += (_, _) => Modals.Remove(view);
    }

    /// <summary>Copies text to the system clipboard (best-effort).</summary>
    public static async Task CopyTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var clipboard = (MainWindow as TopLevel)?.Clipboard;
        if (clipboard != null)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
    }
    
    /// <summary>Restores a window's last user-resized size from <see cref="Config.WindowSizes"/> (if any),
    /// clamped to the window's own Min bounds and the owner's screen working area. Call before showing.</summary>
    public static void ApplyPersistedSize(Window view, string key, Config config)
    {
        if (view == null || config?.WindowSizes == null || !config.WindowSizes.TryGetValue(key, out var size))
            return;

        var width = Math.Max(size.Width, view.MinWidth);
        var height = Math.Max(size.Height, view.MinHeight);

        var screen = view.Screens?.ScreenFromWindow(view) ?? view.Screens?.Primary;
        if (screen != null)
        {
            width = Math.Min(width, screen.WorkingArea.Width);
            height = Math.Min(height, screen.WorkingArea.Height);
        }

        view.Width = width;
        view.Height = height;
    }

    /// <summary>Persists a window's current size into <see cref="Config.WindowSizes"/> under the given key.
    /// The in-memory config is picked up by the app's existing periodic autosave — no explicit save here.</summary>
    public static void SavePersistedSize(Window view, string key, Config config)
    {
        if (view == null || config == null)
            return;

        config.WindowSizes ??= new System.Collections.Generic.Dictionary<string, WindowSize>();
        config.WindowSizes[key] = new WindowSize { Width = view.Width, Height = view.Height };
    }

    public static async Task<TResult> ShowDialog<TV, TVm, TResult>(TV view, TVm viewModel, Config config = null)
        where TV : Window
        where TVm : ViewModelBase
    {
        // Access the main window to open the modal dialog
        if (MainWindow != null)
        {
            view.DataContext = viewModel;
            viewModel.View = view;

            ApplyPersistedSize(view, AddDownloadWindowKey, config);
            view.Closing += (_, _) => SavePersistedSize(view, AddDownloadWindowKey, config);

            // Show as a modal dialog and wait for it to close
            BeginModal(view);
            return await view.ShowDialog<TResult>(MainWindow);
        }

        return default;
    }

    /// <summary>Opens the read-only details dialog for a download (info + live per-part progress).</summary>
    public static async Task ShowDetails(DownloadItemViewModel item, Config config = null)
    {
        if (MainWindow == null || item == null)
            return;

        var view = new DownloadDetailsView();
        var viewModel = new DownloadDetailsViewModel(item);
        view.DataContext = viewModel;
        view.Closed += (_, _) => viewModel.Cleanup();

        ApplyPersistedSize(view, DetailsWindowKey, config);
        view.Closing += (_, _) => SavePersistedSize(view, DetailsWindowKey, config);

        BeginModal(view);
        await view.ShowDialog(MainWindow);
    }

    /// <summary>Modal Yes/No confirmation. Returns true only when the user explicitly confirms;
    /// with no main window (headless/startup edge) it confirms silently rather than blocking.
    /// Deliberately does NOT call <see cref="BeginModal"/>: a confirmation belongs on top of whatever
    /// asked for it, so it must not close its own caller.</summary>
    public static async Task<bool> Confirm(string title, string message)
    {
        if (MainWindow == null)
            return true;
        var view = new ConfirmView { DataContext = new ConfirmViewModel(title, message) };
        var result = await view.ShowDialog<bool?>(MainWindow);
        return result == true;
    }

    /// <summary>Opens the in-app Donate modal (replaces the browser round-trip to Donate.md).</summary>
    public static async Task ShowDonate()
    {
        if (MainWindow == null)
            return;
        var vm = new DonateViewModel();
        var view = new DonateView { DataContext = vm };
        vm.View = view;
        BeginModal(view);
        await view.ShowDialog(MainWindow);
    }

    /// <summary>Opens the modal About dialog (app identity, donate, links and contacts).</summary>
    public static async Task ShowAbout()
    {
        if (MainWindow == null)
            return;

        var view = new AboutView { DataContext = new AboutViewModel() };
        BeginModal(view);
        await view.ShowDialog(MainWindow);
    }

    /// <summary>Shows the in-app "update available" prompt (Download / Later). Non-modal Topmost window so
    /// it's visible even if the main window is hidden in the tray.</summary>
    public static void ShowUpdatePrompt(UpdateInfo info)
    {
        if (info == null)
            return;
        var vm = new UpdatePromptViewModel(info.Version, info.ReleaseUrl);
        var view = new UpdatePromptView { DataContext = vm };
        vm.CloseRequested += () => { try { view.Close(); } catch { /* already closed */ } };
        view.Show();
        view.Activate();
    }

    /// <summary>
    /// Test seams for the three platform pickers. A picker can only ever be cancelled in a test — the
    /// platform has no one to ask — so everything a caller does with a CHOSEN path (install this plugin
    /// DLL, export the log here, save downloads there) was unreachable. These let a test answer the
    /// picker; the app never sets them.
    /// </summary>
    internal static Func<Uri> OpenFilePickerOverride { get; set; }

    /// <inheritdoc cref="OpenFilePickerOverride"/>
    internal static Func<Uri> SaveFilePickerOverride { get; set; }

    /// <inheritdoc cref="OpenFilePickerOverride"/>
    internal static Func<Uri> OpenFolderPickerOverride { get; set; }

    /// <summary>Asks the user to pick an existing file (filtered by extension); returns its path or null.</summary>
    public static async Task<Uri> OpenFilePicker(string title, string filterName, string extension)
    {
        if (OpenFilePickerOverride is { } picked)
            return picked();

        var owner = ActiveWindow;
        if (owner == null)
            return null;

        var result = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(filterName) { Patterns = new[] { "*." + extension } }
            }
        });

        return result.Count > 0 ? result[0].Path : null;
    }

    /// <summary>Asks the user where to save a file; returns the chosen path or null.</summary>
    public static async Task<Uri> SaveFilePicker(string title, string suggestedName)
    {
        if (SaveFilePickerOverride is { } picked)
            return picked();

        if (MainWindow == null)
            return null;

        var file = await MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName
        });

        return file?.Path;
    }

    public static async Task<Uri> OpenFolderPicker(string title, Window owner = null)
    {
        if (OpenFolderPickerOverride is { } picked)
            return picked();

        // Parent the picker to the active window (e.g. the Add dialog) so it stays on top,
        // falling back to the main window.
        var parent = owner ?? MainWindow;
        if (parent != null)
        {
            var result = await parent.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions()
                {
                    Title = title,
                    AllowMultiple = false
                });

            if (result.Count > 0)
                return result[0].Path;
        }

        // Cancelled / nothing chosen — return null so callers skip it. (Returning a bogus relative
        // Uri here previously threw UriFormatException when callers read .LocalPath. #21)
        return null;
    }
}