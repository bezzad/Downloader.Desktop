using System;
using System.Windows.Input;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Backs the in-app "update available" dialog: a clear prompt with **Download update** / **Later**
/// buttons (and a "What's new" link to the release page). The download only starts when the user clicks
/// Download — nothing is pulled in the background — so the Settings progress bar is actually seen.
/// </summary>
public class UpdatePromptViewModel : ViewModelBase
{
    private readonly string _version;
    private readonly string _releaseUrl;

    public UpdatePromptViewModel(string version, string releaseUrl)
    {
        _version = version;
        _releaseUrl = releaseUrl;
        DownloadCommand = ReactiveCommand.Create(Download);
        LaterCommand = ReactiveCommand.Create(Later);
        ViewChangesCommand = ReactiveCommand.Create(ViewChanges);
    }

    /// <summary>Design-time ctor.</summary>
    public UpdatePromptViewModel() : this("0.0.0", null) { }

    private static string L(string key) => Localizer.Instance[key];

    public string Title => L("Update_Available_Title");
    public string Message => string.Format(L("Update_Available_Msg"), "Downloader v" + _version);
    public string ViewChangesText => L("Update_ViewChanges");
    public string DownloadText => L("Update_DownloadBtn");
    public string LaterText => L("Update_LaterBtn");
    public bool HasReleaseUrl => !string.IsNullOrWhiteSpace(_releaseUrl);

    public ICommand DownloadCommand { get; }
    public ICommand LaterCommand { get; }
    public ICommand ViewChangesCommand { get; }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action CloseRequested;

    private void Download()
    {
        CloseRequested?.Invoke();
        _ = UpdateFlow.StartDownloadAsync(); // progress shows in Settings; "Restart to update" when ready
    }

    private void Later()
    {
        CloseRequested?.Invoke();
        UpdateFlow.Dismiss();
    }

    private void ViewChanges()
    {
        if (string.IsNullOrWhiteSpace(_releaseUrl))
            return;
        ShellLauncher.Open(_releaseUrl);
    }
}
