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
    public ExtensionBrowserRow(DetectedBrowser browser)
    {
        Browser = browser;
        // Pre-select what we could confirm; an undetected browser is still listed and still installable,
        // it just isn't ticked by default. Reads the row's own IsInstalled so the tick and the hint can
        // never disagree.
        IsSelected = IsInstalled;
    }

    public DetectedBrowser Browser { get; }
    public string Id => Browser.Id;
    public string Name => Browser.Name;
    public BrowserFamily Family => Browser.Family;

    /// <summary>Whether detection could confirm this browser here — a hint next to the name, never a
    /// reason to hide the row (detection cannot see a browser outside the app's filesystem view).</summary>
    public bool IsInstalled => Browser?.IsInstalled == true;

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
        !IsConnected ? (IsInstalled
            ? Localizer.Instance["Ext_NotConnected"]
            : Localizer.Instance["Ext_NotDetected"])
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

    /// <summary>
    /// There is always something to install: the release catalog when it is reachable, otherwise the copy
    /// bundled with the app. A release published before this feature existed carries no catalog at all,
    /// so without the bundled floor this would be false on every machine today.
    /// </summary>
    public bool HasBuild => true;

    /// <summary>
    /// True when the build on offer is the app's own copy rather than the published catalog — either
    /// because there is no catalog entry, or because the bundled copy is the NEWER of the two. That second
    /// case matters right after an app update: installing the catalog's older build over it would be a
    /// downgrade dressed up as an install.
    /// </summary>
    public bool IsBundled =>
        Entry == null || ExtensionCatalogService.IsNewer(BundledVersion, Entry.Version);

    /// <summary>The version that would be installed — the newer of the catalog's and the bundled copy's.</summary>
    public string AvailableVersion => ExtensionCatalogService.Newer(Entry?.Version, BundledVersion) ?? "";

    /// <summary>Set by the view model so the row can report the bundled version without reaching out.</summary>
    public string BundledVersion { get; init; } = "";

    /// <summary>
    /// Whether the files on disk are older than what this app could put there. Answered from the install
    /// record alone, so it holds whether or not any browser has ever loaded them — a user asked to be able
    /// to see that the copy in their Chrome folder was stale, and the dialog previously printed both
    /// version numbers without ever drawing the conclusion (2026-09-04).
    /// </summary>
    public bool UpdateAvailable =>
        HasInstalledVersion && ExtensionCatalogService.IsNewer(AvailableVersion, InstalledVersion);

    /// <summary>Where this family stands: nothing installed yet, up to date, or behind.</summary>
    public string UpdateText =>
        !HasInstalledVersion ? null
        : UpdateAvailable ? string.Format(Localizer.Instance["Ext_FilesOutdated"], AvailableVersion)
        : Localizer.Instance["Ext_FilesUpToDate"];

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

    private string _installedVersion;
    /// <summary>
    /// The version of the files currently on disk for this family, read from the install record — the
    /// answer to "which version do I actually have?" that does not depend on the extension having called
    /// the app. It is NOT a claim that a browser loaded it: that is <see cref="ExtensionBrowserRow"/>'s
    /// connected state, and only the extension itself can say so.
    /// </summary>
    public string InstalledVersion
    {
        get => _installedVersion;
        set
        {
            this.RaiseAndSetIfChanged(ref _installedVersion, value);
            this.RaisePropertyChanged(nameof(HasInstalledVersion));
            this.RaisePropertyChanged(nameof(InstalledVersionText));
            this.RaisePropertyChanged(nameof(UpdateAvailable));
            this.RaisePropertyChanged(nameof(UpdateText));
        }
    }

    public bool HasInstalledVersion => !string.IsNullOrWhiteSpace(InstalledVersion);

    public string InstalledVersionText =>
        HasInstalledVersion ? string.Format(Localizer.Instance["Ext_InstalledVersion"], InstalledVersion) : null;

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
    private readonly Func<string, InstalledCopy> _readInstalled;
    private readonly Func<string, bool, ExtensionInstallResult> _installBundled;
    private readonly Func<string> _bundledVersion;

    private IReadOnlyList<ExtensionCatalogEntry> _catalog = Array.Empty<ExtensionCatalogEntry>();
    private CancellationTokenSource _cts;

    public ExtensionInstallViewModel(
        Func<IReadOnlyList<DetectedBrowser>> detect = null,
        Func<CancellationToken, Task<IReadOnlyList<ExtensionCatalogEntry>>> fetchCatalog = null,
        Func<ExtensionCatalogEntry, IProgress<double>, CancellationToken, Task<ExtensionInstallResult>> install = null,
        Func<string, string> lastSeenVersion = null,
        Func<string, InstalledCopy> readInstalled = null,
        Func<string, bool, ExtensionInstallResult> installBundled = null,
        Func<string> bundledVersion = null)
    {
        _installBundled = installBundled ?? ExtensionInstallService.InstallBundled;
        _bundledVersion = bundledVersion ?? ExtensionInstallService.BundledVersion;
        // EVERY supported browser, not only the detected ones — see BrowserDetector.All.
        _detect = detect ?? BrowserDetector.All;
        _fetchCatalog = fetchCatalog ?? ExtensionCatalogService.FetchAsync;
        _install = install ?? ExtensionInstallService.InstallAsync;
        _lastSeenVersion = lastSeenVersion ?? (browser => LocalApiService.LastSeenExtension(browser)?.Version);
        _readInstalled = readInstalled ?? ExtensionInstallService.ReadInstalledCopy;

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

    public bool CanInstall => !IsBusy && Targets.Count > 0;

    /// <summary>True when any family's unpacked files are older than what this app can install.</summary>
    public bool AnyUpdateAvailable => Targets.Any(t => t.UpdateAvailable);

    /// <summary>
    /// The action button's label. It says UPDATE once anything on disk is behind, because "Get the files"
    /// on a machine that already has them reads as "nothing to do here" — which is how a stale copy went
    /// unnoticed. Same button either way: an update IS a re-install into the same folder.
    /// </summary>
    public string InstallButtonText =>
        Localizer.Instance[AnyUpdateAvailable ? "Ext_Update" : "Ext_Install"];

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
                Browsers.Add(new ExtensionBrowserRow(b));

            _catalog = await _fetchCatalog(CancellationToken.None).ConfigureAwait(true)
                       ?? Array.Empty<ExtensionCatalogEntry>();

            RebuildTargets();
            RefreshConnectionState();

            if (!HasBrowsers)
                Notice = Localizer.Instance["Ext_NoBrowsers"];
            else if (_catalog.Count == 0)
                // Offline, no release, or a release older than this feature. The app's own copy is
                // installed instead, so this is information rather than a dead end.
                Notice = Localizer.Instance["Ext_UsingBundled"];
        }
        finally
        {
            IsBusy = false;
            this.RaisePropertyChanged(nameof(HasBrowsers));
        }
    }

    /// <summary>
    /// One target row per family, ALWAYS both — the folder for every browser, whatever is detected here.
    /// Building the list from detected browsers instead meant a machine where only Firefox was found (or
    /// only Firefox was VISIBLE, which is what a snap-confined app sees) offered no Chromium folder at
    /// all, so there was no way to install the extension into the browser the user was actually running.
    /// </summary>
    private void RebuildTargets()
    {
        Targets.Clear();
        foreach (var family in new[] { BrowserFamily.Chromium, BrowserFamily.Gecko })
        {
            var entry = EntryFor(family);
            var targetId = entry?.Id ?? TargetIdFor(family);
            // Path AND version come from the same seam: reaching for the real TargetPath here made the
            // dialog read the developer's own config folder in a test that had stubbed the record.
            var installed = _readInstalled(targetId);
            Targets.Add(new ExtensionTargetRow(family, entry, installed?.Path)
            {
                BundledVersion = _bundledVersion(),
                InstalledVersion = installed?.Version,
            });
        }
        this.RaisePropertyChanged(nameof(CanInstall));
        RaiseUpdateState();
    }

    /// <summary>Re-reads the aggregates the footer button depends on.</summary>
    private void RaiseUpdateState()
    {
        this.RaisePropertyChanged(nameof(AnyUpdateAvailable));
        this.RaisePropertyChanged(nameof(InstallButtonText));
    }

    /// <summary>The install-folder name for a family when the catalog does not name one.</summary>
    internal static string TargetIdFor(BrowserFamily family) =>
        family == BrowserFamily.Gecko ? "firefox" : "chrome";

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
            // What this app could actually put on disk — the catalog's build or its own bundled copy,
            // whichever is newer. Comparing against the catalog alone meant a browser running an old
            // extension was never told, because no published release carries a catalog yet.
            var available = ExtensionCatalogService.Newer(EntryFor(row.Family)?.Version, _bundledVersion());
            row.ConnectedVersion = reported;
            row.AvailableVersion = available;
            row.UpdateAvailable = reported != null && ExtensionCatalogService.IsNewer(available, reported);
        }
    }

    /// <summary>Internal so tests await the work instead of sleeping after a fire-and-forget command
    /// execution — a timing-based test here would be flaky on a loaded machine for no benefit.</summary>
    internal async Task InstallSelectedAsync()
    {
        // Nothing ticked installs BOTH families rather than refusing: on a machine where detection can
        // confirm nothing (a snap-confined app sees the base snap's /usr/bin, not the host's) no row is
        // pre-selected, and a dialog whose only button then said "pick a browser" was a dead end.
        var families = Browsers.Where(b => b.IsSelected).Select(b => b.Family).Distinct().ToList();
        if (families.Count == 0)
            families = Targets.Select(t => t.Family).Distinct().ToList();

        IsBusy = true;
        ErrorMessage = null;
        Notice = null;
        Progress = 0;
        var updated = false;
        _cts = new CancellationTokenSource();
        try
        {
            foreach (var family in families)
            {
                var target = Targets.FirstOrDefault(t => t.Family == family);
                if (target == null)
                    continue;

                // No catalog entry means the release could not be reached, or predates this feature —
                // install the copy that ships inside the app rather than leaving the user with nothing.
                //
                // An UPDATE is this same operation: the files are written back into the SAME folder
                // (ExtensionInstallService stages a copy and swaps it in). Keeping that path fixed is what
                // lets the browser keep the extension it already has — a browser derives an unpacked
                // extension's identity from its absolute path, so installing to a new folder would read as
                // a different extension with an empty settings store.
                var wasUpdate = target.UpdateAvailable;
                var result = target.IsBundled
                    ? _installBundled(TargetIdFor(target.Family), target.Family == BrowserFamily.Gecko)
                    : await _install(target.Entry,
                        new Progress<double>(p => Progress = p), _cts.Token).ConfigureAwait(true);

                if (result.Success)
                {
                    target.InstalledPath = result.Path;
                    target.InstalledVersion = target.AvailableVersion;
                    if (wasUpdate)
                        updated = true;
                }
                else
                {
                    ErrorMessage = result.Error;
                }
            }

            // Writing new files is not the same as the browser reading them: a browser loads an unpacked
            // extension once and keeps that copy until it is reloaded or the browser restarts. Say so, or
            // the user updates the folder and wonders why their browser still reports the old version.
            if (updated && !HasError)
                Notice = Localizer.Instance["Ext_ReloadAfterUpdate"];
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
            RaiseUpdateState();
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
        var path = target?.InstalledPath;
        if (string.IsNullOrWhiteSpace(path) || !ShellLauncher.OpenFolder(path))
            // The folder path is the one thing the user has to hand to their browser, so a button that
            // silently fails to show it leaves them stuck with no idea why.
            ErrorMessage = string.Format(Localizer.Instance["Err_OpenFolder_Detail"],
                string.IsNullOrWhiteSpace(path) ? "?" : path);
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
            target.RaisePropertyChanged(nameof(ExtensionTargetRow.InstalledVersionText));
            target.RaisePropertyChanged(nameof(ExtensionTargetRow.UpdateText));
        }
        this.RaisePropertyChanged(nameof(InstallButtonText));
    }

    /// <summary>Unsubscribes from the language change — a dialog that leaks this keeps the whole VM alive
    /// for the life of the process (see DownloadItemViewModel.Detach).</summary>
    public void Detach() => Localizer.Instance.PropertyChanged -= OnLanguageChanged;
}
