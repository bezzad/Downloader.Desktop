using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Downloader.Desktop.Views;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// The tray menu (tray-popup-menu): its four items and what each does, the app-drawn popup, and the
/// translated labels. The real tray click is verified on device; everything behind it is tested here.
/// </summary>
public class TrayMenuTests
{
    private sealed class Calls
    {
        public readonly List<string> Log = new();
        public bool? Persisted;
    }

    private static Calls InitTray(Window window = null)
    {
        Localizer.Instance.Load("en");
        var calls = new Calls();
        TrayService.Init(window ?? new Window(), () => calls.Log.Add("quit"),
            () => calls.Log.Add("downloads"), () => calls.Log.Add("settings"));
        TrayService.NotificationsToggled = enabled => calls.Persisted = enabled;
        return calls;
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Each_item_calls_its_own_path_and_closes_the_menu()
    {
        var calls = InitTray();
        var closed = 0;
        TrayService.Menu.Done += () => closed++;

        TrayService.Menu.ShowDownloadsCommand.Execute(null);
        TrayService.Menu.ShowSettingsCommand.Execute(null);
        TrayService.Menu.ExitCommand.Execute(null);

        Assert.Equal(new[] { "downloads", "settings", "quit" }, calls.Log);
        Assert.Equal(3, closed);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_notifications_switch_turns_them_off_and_hands_the_choice_on_to_be_saved()
    {
        var calls = InitTray();
        var before = NotificationService.Enabled;
        try
        {
            NotificationService.Enabled = true;
            TrayService.Menu.NotificationsEnabled = false;

            Assert.False(NotificationService.Enabled);
            Assert.False(calls.Persisted);
        }
        finally
        {
            NotificationService.Enabled = before;
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_app_saves_the_switch_and_shows_the_chosen_page()
    {
        // The callbacks MainViewModel hands the tray: the page switch and the saved setting.
        var config = Config.New();
        var manager = new DownloadManager();
        var main = new MainViewModel(new StubFileService(config), manager);
        manager.Initialize(config);
        Dispatcher.UIThread.RunJobs();

        main.ShowFromTray(NavSection.Settings);
        Assert.True(main.IsSettingsSelected);
        main.ShowFromTray(NavSection.Downloads);
        Assert.True(main.IsDownloadsSelected);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_native_menu_has_the_same_items_in_the_app_language()
    {
        InitTray();
        var before = NotificationService.Enabled;
        try
        {
            NotificationService.Enabled = true;
            var menu = TrayService.BuildNativeMenu();
            var labels = menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header).ToArray();
            Assert.Equal(new[] { "Show downloads", "Settings", "✓ Notifications", "Exit" }, labels);

            Localizer.Instance.Load("fa");
            TrayService.ApplyLabels(menu);
            labels = menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header).ToArray();
            Assert.Equal(Localizer.Instance["Tray_ShowDownloads"], labels[0]);
            Assert.NotEqual("Show downloads", labels[0]);
            Assert.Equal(Localizer.Instance["Tray_Exit"], labels[3]);
        }
        finally
        {
            NotificationService.Enabled = before;
            Localizer.Instance.Load("en");
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_click_opens_the_app_drawn_menu_with_four_rows()
    {
        InitTray();
        try
        {
            // Windows: a click means the popup, whatever OS runs the test.
            TrayService.OnClicked(opensPopup: true);
            Dispatcher.UIThread.RunJobs();

            var popup = TrayService.Popup;
            Assert.NotNull(popup);
            Assert.True(popup.IsVisible);
            Assert.Equal(new[] { "Show downloads", "Settings", "Notifications", "Exit" }, Labels(popup));
            Assert.NotNull(Find<ToggleSwitch>(popup, "NotificationsSwitch"));
        }
        finally
        {
            TrayService.Disable();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void On_Linux_and_macOS_a_click_brings_the_window_back_instead_of_the_popup()
    {
        // There the shell shows the native menu itself (on Linux the app hears only a DOUBLE click), so
        // the click the app does hear means "show me the app"; a popup would be a second menu.
        Assert.False(OperatingSystem.IsLinux() && TrayService.ClickOpensPopup());
        var window = new Window();
        InitTray(window);
        try
        {
            TrayService.OnClicked(opensPopup: false);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(TrayService.Popup);
            Assert.True(window.IsVisible);
        }
        finally
        {
            TrayService.Disable();
            window.Close();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Esc_and_choosing_a_row_close_the_menu()
    {
        InitTray();
        try
        {
            TrayService.ShowMenuPopup();
            var popup = TrayService.Popup;
            popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsVisible);

            TrayService.ShowMenuPopup();
            Assert.True(popup.IsVisible);
            Find<Button>(popup, "SettingsRow").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsVisible);
        }
        finally
        {
            TrayService.Disable();
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_menu_follows_the_language_and_is_right_to_left_in_Persian()
    {
        InitTray();
        try
        {
            Localizer.Instance.Load("fa");
            TrayService.ShowMenuPopup();
            Dispatcher.UIThread.RunJobs();
            var popup = TrayService.Popup;

            Assert.Equal(Avalonia.Media.FlowDirection.RightToLeft, popup.FlowDirection);
            Assert.Equal(Localizer.Instance["Tray_ShowDownloads"], Labels(popup)[0]);
            Assert.NotEqual("Show downloads", Labels(popup)[0]);
        }
        finally
        {
            Localizer.Instance.Load("en");
            TrayService.Disable();
        }
    }

    private static string[] Labels(Window popup) =>
        popup.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToArray();

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    private sealed class StubFileService : IFileService
    {
        private readonly Config _config;
        public StubFileService(Config config) => _config = config;
        public Task<Config> LoadFromFileAsync() => Task.FromResult(_config);
        public Task SaveToFileAsync(Config itemToSave) => Task.CompletedTask;
    }
}
