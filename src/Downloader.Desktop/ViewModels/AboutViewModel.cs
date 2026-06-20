using System;
using System.Diagnostics;
using System.Windows.Input;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Backs the About dialog: app identity (logo/title/version), a donate link, and "about this project"
/// content sections plus contact buttons (GitHub / Telegram / Email). All links open in the OS default
/// app. Original layout — not a copy of any other application.
/// </summary>
public class AboutViewModel : ViewModelBase
{
    // Canonical project links.
    public const string RepoUrl = "https://github.com/bezzad/Downloader.Desktop";
    public const string EngineRepoUrl = "https://github.com/bezzad/downloader";
    public const string DonateUrl = "https://github.com/bezzad/Downloader.Desktop/blob/main/Donate.md";
    public const string LicenseUrl = "https://github.com/bezzad/Downloader.Desktop/blob/main/LICENSE";
    public const string TranslateUrl = "https://github.com/bezzad/Downloader.Desktop/tree/main/src/Downloader.Desktop/Assets/i18n";
    public const string GithubProfileUrl = "https://github.com/bezzad";
    public const string TelegramUrl = "https://t.me/bezzad";
    public const string Email = "behzad.khosravifar@gmail.com";

    public AboutViewModel()
    {
        OpenRepoCommand = ReactiveCommand.Create(() => Open(RepoUrl));
        OpenEngineCommand = ReactiveCommand.Create(() => Open(EngineRepoUrl));
        OpenDonateCommand = ReactiveCommand.Create(() => Open(DonateUrl));
        OpenLicensesCommand = ReactiveCommand.Create(() => Open(LicenseUrl));
        OpenTranslateCommand = ReactiveCommand.Create(() => Open(TranslateUrl));
        OpenGithubCommand = ReactiveCommand.Create(() => Open(GithubProfileUrl));
        OpenTelegramCommand = ReactiveCommand.Create(() => Open(TelegramUrl));
        OpenEmailCommand = ReactiveCommand.Create(() => Open($"mailto:{Email}"));
    }

    public ICommand OpenRepoCommand { get; }
    public ICommand OpenEngineCommand { get; }
    public ICommand OpenDonateCommand { get; }
    public ICommand OpenLicensesCommand { get; }
    public ICommand OpenTranslateCommand { get; }
    public ICommand OpenGithubCommand { get; }
    public ICommand OpenTelegramCommand { get; }
    public ICommand OpenEmailCommand { get; }

    /// <summary>"Downloader" — the app/brand name (not localized).</summary>
    public string AppName => "Downloader";

    /// <summary>The clean 3-part version shown under the title (same value the update check uses).</summary>
    public string VersionText => string.Format(Localizer.Instance["About_Version"], UpdateService.CurrentVersion);

    /// <summary>Repo address shown as the website link in the dialog.</summary>
    public string WebsiteText => "github.com/bezzad/Downloader.Desktop";

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // best-effort: nothing actionable if no handler is registered
        }
    }
}
