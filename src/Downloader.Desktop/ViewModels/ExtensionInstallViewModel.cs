using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive;
using System.Windows.Input;
using Avalonia.Input.Platform;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>One browser found on this machine, and whether its extension has ever called the app.</summary>
public class ExtensionBrowserRow : ViewModelBase
{
    private readonly Func<ExtensionBrowserRow, Task> _openStore;

    public ExtensionBrowserRow(DetectedBrowser browser, Func<ExtensionBrowserRow, Task> openStore = null)
    {
        Browser = browser;
        _openStore = openStore;
        IsSelected = true;   // the user opened this dialog to install; make the common case one click
        OpenStoreCommand = ReactiveCommand.CreateFromTask(() => _openStore?.Invoke(this) ?? Task.CompletedTask);
    }

    public DetectedBrowser Browser { get; }
    public string Id => Browser.Id;
    public string Name => Browser.Name;
    public BrowserFamily Family => Browser.Family;
    public ICommand OpenStoreCommand { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>The version this browser's extension last reported, or null if it never called.</summary>
    private string _connectedVersion;
    public string ConnectedVersion
    {
        get => _connectedVersion;
        set
        {
            this.RaiseAndSetIfChanged(ref _connectedVersion, value);
            this.RaisePropertyChanged(nameof(IsConnected));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>
    /// <b>Only the extension can say this.</b> Unpacking files proves nothing — the manual load can fail
    /// in ways the app cannot see (Developer mode disabled by policy, the user closed the tab), and a tick
    /// that meant "we unzipped something" would be worse than no tick at all.
    /// </summary>
    public bool IsConnected => !string.IsNullOrWhiteSpace(ConnectedVersion);

    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set
        {
            this.RaiseAndSetIfChanged(ref _updateAvailable, value);
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    private string _availableVersion;
    public string AvailableVersion
    {
        get => _availableVersion;
        set => this.RaiseAndSetIfChanged(ref _availableVersion, value);
    }

    public string StatusText =>
        !IsConnected ? Localizer.Instance["Ext_NotConnected"]
        : UpdateAvailable ? string.Format(Localizer.Instance["Ext_UpdateAvailable"], ConnectedVersion, AvailableVersion)
        : string.Format(Localizer.Instance["Ext_Connected"], ConnectedVersion);
}

/// <summary>
/// One browser family's build: where it came from, how the user installs it, and — stated plainly — what
/// that costs them. The honesty is deliberate: the manual path has real drawbacks on both families, and a
/// dialog that hid them would generate the bug reports instead.
/// </summary>
public class ExtensionTargetRow : ViewModelBase
{
    public ExtensionTargetRow(BrowserFamily family, ExtensionCatalogEntry entry, string installedPath)
    {
        Family = family;
        Entry = entry;
        InstalledPath = installedPath;
    }

    public BrowserFamily Family { get; }
    public ExtensionCatalogEntry Entry { get; }

    /// <summary>Null when the running app is too old for every published build of this family.</summary>
    public bool HasBuild => Entry != null;

    /// <summary>A published store listing turns the manual steps into a footnote. Its absence is what
    /// makes the manual path primary — never a dead link.</summary>
    public bool UseStore => Entry?.HasStore == true;

    public string StoreUrl => Entry?.StoreUrl;
    public string AvailableVersion => Entry?.Version;

    public string Title => Entry?.Name ?? (Family == BrowserFamily.Gecko
        ? Localizer.Instance["Ext_Family_Gecko"]
        : Localizer.Instance["Ext_Family_Chromium"]);

    private string _installedPath;
    public string InstalledPath
    {
        get => _installedPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _installedPath, value);
            this.RaisePropertyChanged(nameof(IsUnpacked));
        }
    }

    public bool IsUnpacked => !string.IsNullOrWhiteSpace(InstalledPath);

    /// <summary>The numbered steps for this family, in the browser's own words.</summary>
    public IReadOnlyList<string> Steps => Family == BrowserFamily.Gecko
        ? new[]
        {
            Localizer.Instance["Ext_Steps_Gecko_1"],
            Localizer.Instance["Ext_Steps_Gecko_2"],
            Localizer.Instance["Ext_Steps_Gecko_3"],
        }
        : new[]
        {
            Localizer.Instance["Ext_Steps_Chromium_1"],
            Localizer.Instance["Ext_Steps_Chromium_2"],
            Localizer.Instance["Ext_Steps_Chromium_3"],
        };

    /// <summary>
    /// What a manually loaded extension costs on this family. Gecko's is the one that MUST be said: a
    /// manually loaded add-on there is removed when the browser restarts, and there is no permanent
    /// unsigned install on a release build.
    /// </summary>
    public string Limitations => Family == BrowserFamily.Gecko
        ? Localizer.Instance["Ext_Limits_Gecko"]
        : Localizer.Instance["Ext_Limits_Chromium"];
}

/// <summary>
/// The "Install browser extension" dialog.
///
/// <para>Everything the OS or the network does arrives through a delegate, so the decisions this class
/// makes — which browsers to list, store path or manual path, connected or not, up to date or not — are
/// testable without a browser, a release, or a network. The app wires the real implementations in
/// <c>DialogHelper</c>.</para>
/// </summary>
public class ExtensionInstallViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<DetectedBrowser>> _detect;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ExtensionCatalogEntry>>> _fetchCatalog;
    private readonly Func<ExtensionCatalogEntry, IProgress<double>, CancellationToken, Task<ExtensionInstallResult>> _install;
    private readonly Func<string, string> _lastSeenVersion;
    private readonly Func<string, string> _installedPath;

    private IReadOnlyList<ExtensionCatalogEntry> _catalog = Array.Empty<ExtensionCatalogEntry>();
    private CancellationTokenSource _cts;

    public ExtensionInstallViewModel(
        Func<IReadOnlyList<DetectedBrowser>> detect = null,
        Func<CancellationToken, Task<IReadOnlyList<ExtensionCatalogEntry>>> fetchCatalog = null,
        Func<ExtensionCatalogEntry, IProgress<double>, CancellationToken, Task<ExtensionInstallResult>> install = null,
        Func<string, string> lastSeenVersion = null,
        Func<string, string> installedPath = null)
    {
        _detect = detect ?? BrowserDetector.Detect;
        _fetchCatalog = fetchCatalog ?? ExtensionCatalogService.FetchAsync;
        _install = install ?? ExtensionInstallService.InstallAsync;
        _lastSeenVersion = lastSeenVersion ?? (browser => LocalApiService.LastSeenExtension(browser)?.Version);
        _installedPath = installedPath ?? (target =>
        {
            var installed = ExtensionInstallService.ReadInstalled(target);
            return installed == null ? null : ExtensionInstallService.TargetPath(target);
        });

        InstallCommand = ReactiveCommand.CreateFromTask(InstallSelectedAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        CopyPathCommand = ReactiveCommand.CreateFromTask<ExtensionTargetRow>(CopyPathAsync);
        OpenFolderCommand = ReactiveCommand.Create<ExtensionTargetRow>(OpenFolder);
        CancelCommand = ReactiveCommand.Create(CancelInstall);

        Localizer.Instance.PropertyChanged += OnLanguageChanged;
    }

    public ObservableCollection<ExtensionBrowserRow> Browsers { get; } = new();
    public ObservableCollection<ExtensionTargetRow> Targets { get; } = new();

    /// <summary>Typed rather than <see cref="ICommand"/> so a test can await the execution instead of
    /// sleeping after a fire-and-forget <c>Execute</c> — a timing-based assertion here was flaky under
    /// full-suite load. XAML binds it exactly the same way.</summary>
    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand CancelCommand { get; }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanInstall));
        }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        private set => this.RaiseAndSetIfChanged(ref _progress, value);
    }

    private string _errorMessage;
    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    private string _notice;
    /// <summary>A non-error explanation: no browsers found, or no build this app can use.</summary>
    public string Notice
    {
        get => _notice;
        private set
        {
            this.RaiseAndSetIfChanged(ref _notice, value);
            this.RaisePropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);

    public bool HasBrowsers => Browsers.Count > 0;

    public bool CanInstall => !IsBusy && Targets.Any(t => t.HasBuild && !t.UseStore);

    /// <summary>Detect the browsers, read the catalog, and reconcile both against what is already
    /// installed and what has actually called us. Safe to call again — it is the Refresh command too.</summary>
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        Notice = null;
        try
        {
            Browsers.Clear();
            foreach (var b in _detect() ?? Array.Empty<DetectedBrowser>())
                Browsers.Add(new ExtensionBrowserRow(b, OpenStoreAsync));

            _catalog = await _fetchCatalog(CancellationToken.None).ConfigureAwait(true)
                       ?? Array.Empty<ExtensionCatalogEntry>();

            RebuildTargets();
            RefreshConnectionState();

            if (!HasBrowsers)
                Notice = Localizer.Instance["Ext_NoBrowsers"];
            else if (_catalog.Count == 0)
                // Offline, no release, or every published build needs a newer app. All three end here,
                // and saying "couldn't reach" is honest about all of them.
                Notice = Localizer.Instance["Ext_NoBuild"];
        }
        finally
        {
            IsBusy = false;
            this.RaisePropertyChanged(nameof(HasBrowsers));
        }
    }

    /// <summary>One target row per family that a detected browser actually belongs to — no point offering
    /// a Firefox build to someone who has no Gecko browser.</summary>
    private void RebuildTargets()
    {
        Targets.Clear();
        foreach (var family in Browsers.Select(b => b.Family).Distinct().OrderBy(f => f))
        {
            var entry = EntryFor(family);
            Targets.Add(new ExtensionTargetRow(family, entry, entry == null ? null : _installedPath(entry.Id)));
        }
        this.RaisePropertyChanged(nameof(CanInstall));
    }

    private ExtensionCatalogEntry EntryFor(BrowserFamily family)
    {
        var wanted = family == BrowserFamily.Gecko ? "gecko" : "chromium";
        return _catalog.FirstOrDefault(e => string.Equals(e.Family, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Re-reads what each browser's extension last reported. Cheap, so the dialog can call it
    /// again while it is open — that is how the tick appears without the user reopening anything.</summary>
    public void RefreshConnectionState()
    {
        foreach (var row in Browsers)
        {
            var reported = _lastSeenVersion(row.Id);
            var published = EntryFor(row.Family)?.Version;
            row.ConnectedVersion = reported;
            row.AvailableVersion = published;
            row.UpdateAvailable = reported != null && ExtensionCatalogService.IsNewer(published, reported);
        }
    }

    /// <summary>Internal so tests await the work instead of sleeping after a fire-and-forget command
    /// execution — a timing-based test here would be flaky on a loaded machine for no benefit.</summary>
    internal async Task InstallSelectedAsync()
    {
        var families = Browsers.Where(b => b.IsSelected).Select(b => b.Family).Distinct().ToList();
        if (families.Count == 0)
        {
            Notice = Localizer.Instance["Ext_PickABrowser"];
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        Notice = null;
        Progress = 0;
        _cts = new CancellationTokenSource();
        try
        {
            foreach (var family in families)
            {
                var target = Targets.FirstOrDefault(t => t.Family == family);
                if (target == null || !target.HasBuild)
                {
                    // The app is older than every published build for this family. Saying so is the
                    // point — offering a build whose API this app cannot serve produces a broken
                    // extension and a confused user.
                    ErrorMessage = Localizer.Instance["Ext_NeedsNewerApp"];
                    continue;
                }
                if (target.UseStore)
                    continue;   // nothing to unpack: the store installs and updates it

                var result = await _install(target.Entry,
                    new Progress<double>(p => Progress = p), _cts.Token).ConfigureAwait(true);

                if (result.Success)
                    target.InstalledPath = result.Path;
                else
                    ErrorMessage = result.Error;
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    private void CancelInstall()
    {
        try { _cts?.Cancel(); }
        catch { /* already gone */ }
    }

    private async Task CopyPathAsync(ExtensionTargetRow target)
    {
        var clipboard = View?.Clipboard;
        if (clipboard == null || target?.InstalledPath == null)
            return;
        try { await clipboard.SetTextAsync(target.InstalledPath); }
        catch { /* a clipboard the platform denied is not worth an error dialog */ }
    }

    private void OpenFolder(ExtensionTargetRow target)
    {
        if (!string.IsNullOrWhiteSpace(target?.InstalledPath))
            ShellLauncher.OpenFolder(target.InstalledPath);
    }

    /// <summary>
    /// Opens the store listing in <b>that</b> browser, by absolute executable path — so the extension is
    /// installed into the browser the user picked, not whichever one happens to be their default.
    /// </summary>
    private Task OpenStoreAsync(ExtensionBrowserRow row)
    {
        var url = Targets.FirstOrDefault(t => t.Family == row.Family)?.StoreUrl;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(row.Browser.ExecutablePath))
            return Task.CompletedTask;

        if (!ShellLauncher.Run(row.Browser.ExecutablePath, url))
            ShellLauncher.Open(url);   // fall back to the default browser rather than doing nothing
        return Task.CompletedTask;
    }

    private void OnLanguageChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        foreach (var row in Browsers)
            row.RaisePropertyChanged(nameof(ExtensionBrowserRow.StatusText));
        foreach (var target in Targets)
        {
            target.RaisePropertyChanged(nameof(ExtensionTargetRow.Title));
            target.RaisePropertyChanged(nameof(ExtensionTargetRow.Steps));
            target.RaisePropertyChanged(nameof(ExtensionTargetRow.Limitations));
        }
    }

    /// <summary>Unsubscribes from the language change — a dialog that leaks this keeps the whole VM alive
    /// for the life of the process (see DownloadItemViewModel.Detach).</summary>
    public void Detach() => Localizer.Instance.PropertyChanged -= OnLanguageChanged;
}
