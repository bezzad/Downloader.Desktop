using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using Downloader.Desktop.ViewModels;
using Xunit;

namespace Downloader.Desktop.Tests.UI;

/// <summary>
/// Everywhere the app hands something to the operating system — a link to the browser, a folder to
/// the file manager, "reveal this file".
///
/// These paths were the last sizeable blind spot, and for a good reason: covering them for real would
/// open browser tabs and file-manager windows on whoever runs the suite. They now route through
/// <see cref="ShellLauncher"/>, whose override lets a test assert WHICH target a button would launch
/// without launching anything. That matters because the failure here is silent by design — every call
/// site swallows exceptions (there is nothing useful to tell a user whose machine has no handler), so
/// a button wired to the wrong URL, or to nothing at all, looks identical to one that works.
/// </summary>
public class ShellLaunchTests : IDisposable
{
    private readonly List<string> _opened = new();
    private readonly List<(string File, string[] Args)> _ran = new();
    private readonly bool _notificationsWereEnabled = NotificationService.Enabled;

    public ShellLaunchTests()
    {
        NotificationService.Enabled = false; // don't shell out to notify-send
        ShellLauncher.OpenOverride = target => { _opened.Add(target); return true; };
        ShellLauncher.RunOverride = (file, args) => { _ran.Add((file, args)); return true; };
    }

    public void Dispose()
    {
        ShellLauncher.OpenOverride = null;
        ShellLauncher.RunOverride = null;
        NotificationService.Enabled = _notificationsWereEnabled;
    }

    // ---- the launcher itself ----------------------------------------------

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Nothing_is_launched_for_a_blank_target()
    {
        Assert.False(ShellLauncher.TryOpen(null));
        Assert.False(ShellLauncher.TryOpen(""));
        Assert.False(ShellLauncher.TryOpen("   "));
        Assert.False(ShellLauncher.Run(null));

        Assert.Empty(_opened);
        Assert.Empty(_ran);
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void Try_open_reports_whether_the_os_accepted_the_target()
    {
        ShellLauncher.OpenOverride = _ => false; // a machine with no handler registered

        Assert.False(ShellLauncher.TryOpen("https://example.invalid/"));
    }

    [Fact(Timeout = TestTimeouts.DefaultMs)]
    public void The_real_launcher_reports_a_command_that_does_not_exist()
    {
        // No override: this exercises the actual Process.Start path. Only the Run side is checked
        // for real — the Open side hands the target to the desktop's URL handler, and on Linux
        // xdg-open starts successfully even for a nonsense target (and would really open a window),
        // so there is no way to observe a refusal there without launching something.
        ShellLauncher.RunOverride = null;

        // A platform missing the reveal command must return false so the caller can fall back to
        // opening the folder, not throw.
        Assert.False(ShellLauncher.Run("downloader-no-such-command-" + Guid.NewGuid().ToString("N"), "--x"));
    }

    // ---- About / Donate / update prompt -----------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Every_about_link_opens_its_own_destination()
    {
        Localizer.Instance.Load("en");
        var vm = new AboutViewModel();

        vm.OpenRepoCommand.Execute(null);
        vm.OpenEngineCommand.Execute(null);
        vm.OpenLicensesCommand.Execute(null);
        vm.OpenTranslateCommand.Execute(null);
        vm.OpenGithubCommand.Execute(null);
        vm.OpenTelegramCommand.Execute(null);
        vm.OpenEmailCommand.Execute(null);

        Assert.Equal(new[]
        {
            AboutViewModel.RepoUrl,
            AboutViewModel.EngineRepoUrl,
            AboutViewModel.LicenseUrl,
            AboutViewModel.TranslateUrl,
            AboutViewModel.GithubProfileUrl,
            AboutViewModel.TelegramUrl,
            "mailto:" + AboutViewModel.Email,
        }, _opened.ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_donate_buttons_open_the_funding_pages()
    {
        var vm = new DonateViewModel();

        vm.OpenSponsorsCommand.Execute(null);
        vm.OpenLiberapayCommand.Execute(null);
        vm.OpenRepoCommand.Execute(null);

        Assert.Equal(new[]
        {
            DonateViewModel.GitHubSponsorsUrl,
            DonateViewModel.LiberapayUrl,
            DonateViewModel.RepoUrl,
        }, _opened.ToArray());
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void What_is_new_opens_the_release_page_when_there_is_one()
    {
        Localizer.Instance.Load("en");
        const string release = "https://github.com/bezzad/Downloader.Desktop/releases/tag/v9.9.9";

        new UpdatePromptViewModel("9.9.9", release).ViewChangesCommand.Execute(null);
        Assert.Equal(release, Assert.Single(_opened));

        _opened.Clear();
        new UpdatePromptViewModel("9.9.9", null).ViewChangesCommand.Execute(null);
        Assert.Empty(_opened); // no page to show, so nothing is launched
    }

    // ---- Settings ---------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_logs_button_opens_the_log_folder()
    {
        Localizer.Instance.Load("en");
        var vm = new SettingViewModel(Config.New());

        vm.OpenLogsFolderCommand.Execute(null);

        Assert.Equal(AppLog.LogFolder, Assert.Single(_opened));
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Emailing_logs_opens_a_browser_compose_page_carrying_diagnostics()
    {
        Localizer.Instance.Load("en");
        var vm = new SettingViewModel(Config.New());

        vm.EmailLogsCommand.Execute(null);

        // A web compose URL, not mailto: — many machines have no mail client registered at all (#25).
        var compose = _opened.Single(u => u.StartsWith("https://mail.google.com/"));
        Assert.Contains("to=", compose);
        Assert.Contains("su=", compose);
        // The diagnostics block is what makes a support mail actionable, so it must survive into the body.
        Assert.Contains("body=", compose);
        Assert.Contains(Uri.EscapeDataString(UpdateService.CurrentVersion.ToString()), compose);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Emailing_logs_falls_back_to_mailto_when_no_browser_answers()
    {
        Localizer.Instance.Load("en");
        var attempts = new List<string>();
        // Simulate a machine where nothing handles an http(s) URL.
        ShellLauncher.OpenOverride = target =>
        {
            attempts.Add(target);
            return !target.StartsWith("https://mail.google.com/");
        };

        new SettingViewModel(Config.New()).EmailLogsCommand.Execute(null);

        Assert.Contains(attempts, a => a.StartsWith("https://mail.google.com/"));
        Assert.Contains(attempts, a => a.StartsWith("mailto:"));
    }

    // ---- Plugins ----------------------------------------------------------

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void The_plugins_folder_button_opens_the_plugins_root()
    {
        Localizer.Instance.Load("en");
        var vm = new PluginsViewModel(new PluginManager(), Config.New());

        vm.OpenFolderCommand.Execute(null);

        Assert.Equal(PluginManager.PluginsRoot, Assert.Single(_opened));
    }

    // ---- a download row ---------------------------------------------------

    private static DownloadItemViewModel Row(string folder, string name)
    {
        Localizer.Instance.Load("en");
        var manager = new DownloadManager();
        manager.Initialize(Config.New());
        return manager.Add(new DownloadItem
        {
            Url = "https://10.255.255.1/" + name,
            FileName = name,
            SaveFolder = folder,
        }, autoStart: false);
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Open_folder_reveals_the_finished_file_itself()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-reveal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var row = Row(dir, "movie.mkv");
            File.WriteAllText(row.GetItem().FilePath, "done");

            row.OpenFolderCommand.Execute(null);

            // Revealing selects the file in the file manager rather than just opening the folder,
            // which is the whole point of the feature (#8).
            var (file, args) = Assert.Single(_ran);
            Assert.False(string.IsNullOrWhiteSpace(file));
            Assert.Contains(args, a => a.Contains("movie.mkv"));
            Assert.Empty(_opened); // no plain folder-open fallback needed
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Open_folder_reveals_the_partial_file_for_a_download_in_progress()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-reveal2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var row = Row(dir, "movie.mkv");
            // The engine writes "<name>.download" until it finishes; the final file does not exist yet.
            File.WriteAllText(row.GetItem().FilePath + ".download", "partial");

            row.OpenFolderCommand.Execute(null);

            var (_, args) = Assert.Single(_ran);
            Assert.Contains(args, a => a.Contains("movie.mkv.download"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Open_folder_falls_back_to_the_folder_when_no_file_is_on_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-reveal3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var row = Row(dir, "movie.mkv"); // nothing written

            row.OpenFolderCommand.Execute(null);

            Assert.Empty(_ran);
            Assert.Equal(dir, Assert.Single(_opened));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void A_reveal_that_the_platform_refuses_still_opens_the_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-reveal4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var row = Row(dir, "movie.mkv");
            File.WriteAllText(row.GetItem().FilePath, "done");
            // Simulate a desktop with no reveal mechanism (no FileManager1 D-Bus service).
            ShellLauncher.RunOverride = (_, _) => false;

            row.OpenFolderCommand.Execute(null);

            // The user must still get *something* — a desktop without the FileManager1 D-Bus service
            // (or without explorer/open) falls back to plainly opening the containing folder.
            Assert.Equal(dir, Assert.Single(_opened));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [AvaloniaFact(Timeout = TestTimeouts.DefaultMs)]
    public void Open_file_opens_the_file_when_it_exists_and_the_folder_otherwise()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dldesktop-openfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var row = Row(dir, "movie.mkv");

            row.OpenFileCommand.Execute(null);
            Assert.Equal(dir, Assert.Single(_opened)); // nothing downloaded yet

            _opened.Clear();
            File.WriteAllText(row.GetItem().FilePath, "done");
            row.OpenFileCommand.Execute(null);
            Assert.Equal(row.GetItem().FilePath, Assert.Single(_opened));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
