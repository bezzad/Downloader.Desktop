using System;
using System.Windows.Input;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The tray menu's items: Show downloads, Settings, a notifications switch and Exit. Each command calls
/// the app's existing path and then raises <see cref="Done"/> so the popup closes.
/// </summary>
public class TrayMenuViewModel : ViewModelBase
{
    private readonly Action<bool> _notificationsChanged;

    public TrayMenuViewModel(Action showDownloads, Action showSettings, Action<bool> notificationsChanged, Action exit)
    {
        _notificationsChanged = notificationsChanged;
        ShowDownloadsCommand = ReactiveCommand.Create(() => Choose("Show downloads", showDownloads));
        ShowSettingsCommand = ReactiveCommand.Create(() => Choose("Settings", showSettings));
        ExitCommand = ReactiveCommand.Create(() => Choose("Exit", exit));
    }

    public ICommand ShowDownloadsCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ExitCommand { get; }

    /// <summary>Asks the view to close (after an item is chosen).</summary>
    public event Action Done;

    /// <summary>The notifications switch. Writes the live flag and hands the choice on to be saved;
    /// the menu stays open so the switch can be seen to move.</summary>
    public bool NotificationsEnabled
    {
        get => NotificationService.Enabled;
        set
        {
            if (NotificationService.Enabled == value)
                return;

            AppLog.Info($"UI: tray menu notifications {(value ? "on" : "off")}");
            NotificationService.Enabled = value;
            _notificationsChanged?.Invoke(value);
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Re-reads the switch after it was changed elsewhere (Settings, the native menu).</summary>
    public void Refresh() => this.RaisePropertyChanged(nameof(NotificationsEnabled));

    private void Choose(string item, Action action)
    {
        AppLog.Info($"UI: tray menu \"{item}\"");
        Done?.Invoke();
        action?.Invoke();
    }
}
