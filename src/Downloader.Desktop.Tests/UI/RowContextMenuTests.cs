using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
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
/// The downloads row: no Type column and no action strip, the type icon beside the name, and one
/// right-click menu that acts on the selection (row-context-menu).
/// </summary>
public class RowContextMenuTests
{
    // ── the grid ─────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_grid_has_no_type_column_and_no_action_column()
    {
        var (_, page) = NewPage();
        var (_, grid, _) = Host(page);

        Assert.DoesNotContain(grid.Columns, c => c.SortMemberPath == "CategoryOrder");
        // Every remaining column is either a fixed helper (grip, checkbox) or a named, sortable one.
        Assert.All(grid.Columns.Skip(2), c => Assert.False(string.IsNullOrEmpty(c.SortMemberPath)));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_name_cell_shows_the_category_icon_with_the_category_tooltip()
    {
        var (manager, page) = NewPage();
        var vm = Add(manager, "movie.mp4", DownloadStatus.Stopped);
        var (_, grid, _) = Host(page);

        var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == vm);
        Assert.Contains(row.GetVisualDescendants().OfType<PathIcon>(),
            icon => Equals(ToolTip.GetTip(icon), vm.CategoryName));
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<Button>(), b => b is not CheckBox);
    }

    // ── enabled rules ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_completed_download_can_be_opened_but_not_paused_stopped_or_restarted()
    {
        var (manager, page) = NewPage();
        Select(Add(manager, "done.zip", DownloadStatus.Completed));

        Assert.True(Can(page.OpenCommand));
        Assert.False(Can(page.PauseMenuCommand));
        Assert.False(Can(page.StopMenuCommand));
        Assert.False(Can(page.RestartCommand));
        Assert.False(Can(page.ResumeMenuCommand));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_mixed_selection_enables_what_applies_to_any_row()
    {
        var (manager, page) = NewPage();
        Select(Add(manager, "done.zip", DownloadStatus.Completed));
        Select(Add(manager, "run.zip", DownloadStatus.Running));

        Assert.True(Can(page.OpenCommand));
        Assert.True(Can(page.PauseMenuCommand));
        Assert.True(Can(page.StopMenuCommand));
        Assert.False(Can(page.RestartCommand)); // neither row is restartable
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_rules_follow_a_status_change()
    {
        var (manager, page) = NewPage();
        var vm = Select(Add(manager, "a.zip", DownloadStatus.Running));
        Assert.True(Can(page.PauseMenuCommand));

        vm.Status = DownloadStatus.Paused;

        Assert.False(Can(page.PauseMenuCommand));
        Assert.True(Can(page.ResumeMenuCommand));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Properties_needs_exactly_one_row()
    {
        var (manager, page) = NewPage();
        Assert.False(Can(page.PropertiesCommand));
        Select(Add(manager, "a.zip", DownloadStatus.Stopped));
        Assert.True(Can(page.PropertiesCommand));
        Select(Add(manager, "b.zip", DownloadStatus.Stopped));
        Assert.False(Can(page.PropertiesCommand));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Resume_retries_a_failed_download()
    {
        var (manager, page) = NewPage(queueRunning: false);
        var vm = Select(Add(manager, "a.zip", DownloadStatus.Failed));
        vm.GetItem().PlanJson = "{}";

        page.ResumeMenuCommand.Execute(null);

        // Retry (not Resume) is what clears the saved plan.
        Assert.Null(vm.GetItem().PlanJson);
        Assert.NotEqual(DownloadStatus.Failed, vm.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_menu_acts_on_every_selected_row()
    {
        var (manager, page) = NewPage();
        var a = Select(Add(manager, "a.zip", DownloadStatus.Running));
        var b = Select(Add(manager, "b.zip", DownloadStatus.Running));
        var c = Add(manager, "c.zip", DownloadStatus.Running);

        Assert.False(page.PrepareMenuFor(b)); // right-click on a selected row keeps the selection
        page.PauseMenuCommand.Execute(null);

        Assert.Equal(DownloadStatus.Paused, a.Status);
        Assert.Equal(DownloadStatus.Paused, b.Status);
        Assert.Equal(DownloadStatus.Running, c.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Right_click_on_an_unselected_row_makes_it_the_only_selected_row()
    {
        var (manager, page) = NewPage();
        var a = Select(Add(manager, "a.zip", DownloadStatus.Running));
        var b = Select(Add(manager, "b.zip", DownloadStatus.Running));
        var c = Add(manager, "c.zip", DownloadStatus.Running);

        Assert.True(page.PrepareMenuFor(c));
        page.PauseMenuCommand.Execute(null);

        Assert.False(a.IsChecked);
        Assert.False(b.IsChecked);
        Assert.Equal(DownloadStatus.Running, a.Status);
        Assert.Equal(DownloadStatus.Paused, c.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Move_to_queue_is_offered_only_with_more_than_one_queue()
    {
        var (manager, page) = NewPage();
        Assert.False(page.ShowQueue);

        var media = manager.AddQueue("Media");
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.ShowQueue);
        var vm = Select(Add(manager, "a.zip", DownloadStatus.Stopped));

        page.MoveToQueueTargets.Single(t => t.Name == "Media").Command.Execute(null);

        Assert.Equal(media.Id, vm.GetItem().QueueId);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Copy_link_copies_every_selected_url_one_per_line()
    {
        var (manager, page) = NewPage();
        Select(Add(manager, "a.zip", DownloadStatus.Stopped));
        Select(Add(manager, "b.zip", DownloadStatus.Stopped));
        Select(Add(manager, "c.zip", DownloadStatus.Stopped));
        string copied = null;
        page.CopyText = t => { copied = t; return Task.CompletedTask; };

        page.CopyLinkCommand.Execute(null);

        Assert.Equal("https://10.255.255.1/a.zip\nhttps://10.255.255.1/b.zip\nhttps://10.255.255.1/c.zip", copied);
    }

    // ── Restart ──────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_stopped_download_restarts_from_zero_and_loses_its_partial_file()
    {
        var (manager, _) = NewPage(queueRunning: false);
        var vm = Add(manager, "movie.mkv", DownloadStatus.Stopped);
        vm.Progress = 40;
        vm.Downloaded = 400;
        var partial = vm.GetItem().FilePath + ".download";
        File.WriteAllText(partial, "part");

        manager.Restart(vm);

        Assert.False(File.Exists(partial));
        Assert.Equal(0, vm.Progress);
        Assert.Equal(0, vm.Downloaded);
        Assert.True(vm.Status is DownloadStatus.Created or DownloadStatus.Running);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Restart_waits_in_the_queue_when_the_cap_is_reached()
    {
        var (manager, _) = NewPage(queueRunning: true);
        manager.Config.DefaultQueue.MaxConcurrent = 1;
        Add(manager, "busy.zip", DownloadStatus.Running);
        var vm = Add(manager, "again.zip", DownloadStatus.Stopped);

        manager.Restart(vm);

        Assert.Equal(DownloadStatus.Created, vm.Status);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Restart_of_a_completed_download_does_nothing_and_keeps_the_file()
    {
        var (manager, page) = NewPage(queueRunning: false);
        var vm = Add(manager, "done.zip", DownloadStatus.Completed);
        var final = vm.GetItem().FilePath;
        File.WriteAllText(final, "the whole file");

        manager.Restart(vm);

        Assert.Equal(DownloadStatus.Completed, vm.Status);
        Assert.Equal(100, vm.Progress);
        Assert.True(File.Exists(final));
    }

    // ── shortcuts ────────────────────────────────────────────────────────────────────────────────────

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Shortcuts_use_the_platform_command_key_except_delete()
    {
        Assert.Equal(new KeyGesture(Key.C, KeyModifiers.Control), DownloadsView.Shortcut(Key.C, KeyModifiers.Control));
        Assert.Equal(new KeyGesture(Key.C, KeyModifiers.Meta), DownloadsView.Shortcut(Key.C, KeyModifiers.Meta));
        Assert.Equal(new KeyGesture(Key.Delete), DownloadsView.Shortcut(Key.Delete, KeyModifiers.Meta));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Delete_on_the_grid_removes_the_selection()
    {
        var (manager, page) = NewPage();
        Select(Add(manager, "a.zip", DownloadStatus.Stopped));
        Select(Add(manager, "b.zip", DownloadStatus.Stopped));
        var kept = Add(manager, "c.zip", DownloadStatus.Stopped);
        var (window, grid, _) = Host(page);

        grid.Focus();
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { kept }, manager.Items.ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Ctrl_c_copies_links_only_while_the_grid_has_focus()
    {
        var (manager, page) = NewPage();
        Select(Add(manager, "a.zip", DownloadStatus.Stopped));
        var copies = new List<string>();
        page.CopyText = t => { copies.Add(t); return Task.CompletedTask; };
        var (window, grid, box) = Host(page);
        var command = (RawInputModifiers)(Application.Current!.PlatformSettings?.HotkeyConfiguration.CommandModifiers
                                          ?? KeyModifiers.Control);

        box.Focus();
        window.KeyPress(Key.C, command, PhysicalKey.C, "c");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(copies);

        grid.Focus();
        window.KeyPress(Key.C, command, PhysicalKey.C, "c");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "https://10.255.255.1/a.zip" }, copies);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static bool Can(ICommand command)
    {
        Dispatcher.UIThread.RunJobs();
        return command.CanExecute(null);
    }

    private static DownloadItemViewModel Select(DownloadItemViewModel vm)
    {
        vm.IsChecked = true;
        return vm;
    }

    private static (DownloadManager Manager, DownloadsViewModel Page) NewPage(bool queueRunning = false)
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        var config = Config.New();
        config.Settings.DefaultSavePath = TempDir();
        config.DefaultQueue.IsRunning = queueRunning;
        manager.Initialize(config);
        return (manager, new DownloadsViewModel(manager));
    }

    private static (Window Window, DataGrid Grid, TextBox Box) Host(DownloadsViewModel page)
    {
        var box = new TextBox { Text = "search text" };
        var view = new DownloadsView { DataContext = page, Height = 400 };
        var window = new Window { Width = 1100, Height = 500, Content = new StackPanel { Children = { box, view } } };
        window.Show();
        for (int i = 0; i < 5; i++)
            Dispatcher.UIThread.RunJobs();
        return (window, view.FindControl<DataGrid>("Root"), box);
    }

    private static DownloadItemViewModel Add(DownloadManager manager, string name, DownloadStatus status)
    {
        var vm = manager.Add(new DownloadItem
        {
            Urls = new List<string> { "https://10.255.255.1/" + name },
            SaveFolder = TempDir(),
            FileName = name,
        }, autoStart: false);
        vm.Status = status;
        return vm;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-rowmenu-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
