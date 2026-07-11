using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Plugins;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// The Plugins (add-ons) page: lists installed plugins with an enable toggle, and lets the user install a
/// plugin DLL or open the plugins folder. This is how an external plugin reaches the app at runtime.
/// </summary>
public class PluginsViewModel : ViewModelBase
{
    private readonly PluginManager _manager;
    private readonly Config _config;
    private IReadOnlyList<CatalogPluginInfo> _catalog = Array.Empty<CatalogPluginInfo>();

    public ObservableCollection<PluginRowViewModel> Plugins { get; } = new();

    /// <summary>Optional plugins from the release catalog that are NOT installed yet (the "More plugins"
    /// section). Populated on demand by <see cref="RefreshCatalogAsync"/>.</summary>
    public ObservableCollection<CatalogPluginRowViewModel> CatalogPlugins { get; } = new();

    public ICommand InstallCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ReloadCommand { get; }

    public PluginsViewModel(PluginManager manager, Config config)
    {
        _manager = manager;
        _config = config;
        InstallCommand = ReactiveCommand.CreateFromTask(InstallAsync);
        OpenFolderCommand = ReactiveCommand.Create(OpenFolder);
        ReloadCommand = ReactiveCommand.Create(Reload);
        Refresh();
    }

    /// <summary>Design-time ctor.</summary>
    public PluginsViewModel() : this(new PluginManager(), Config.New()) { }

    public bool IsEmpty => Plugins.Count == 0;

    private bool _isCatalogLoading;
    public bool IsCatalogLoading
    {
        get => _isCatalogLoading;
        private set => this.RaiseAndSetIfChanged(ref _isCatalogLoading, value);
    }

    /// <summary>True when there are optional plugins to offer (drives the "More plugins" section).</summary>
    public bool HasCatalog => CatalogPlugins.Count > 0;

    /// <summary>
    /// Fetch the release catalog and refresh both the "More plugins" list (optional plugins not yet
    /// installed) and the update badges on installed rows. Network-tolerant: on any failure the section
    /// simply stays empty. Called by the view when the Plugins section is shown.
    /// </summary>
    public async Task RefreshCatalogAsync(CancellationToken ct = default)
    {
        IsCatalogLoading = true;
        try
        {
            _catalog = await PluginCatalogService.FetchAsync(ct).ConfigureAwait(true);
        }
        catch
        {
            _catalog = Array.Empty<CatalogPluginInfo>();
        }
        ApplyCatalog();
        IsCatalogLoading = false;
    }

    /// <summary>Test seam: inject a catalog directly (no network) and re-apply it.</summary>
    internal void SetCatalogForTest(IReadOnlyList<CatalogPluginInfo> catalog)
    {
        _catalog = catalog ?? Array.Empty<CatalogPluginInfo>();
        ApplyCatalog();
    }

    /// <summary>Rebuild the catalog list + installed-row update badges from the last-fetched catalog.</summary>
    private void ApplyCatalog()
    {
        var installed = _manager?.Plugins.Select(p => p.Id).ToHashSet() ?? new HashSet<string>();

        CatalogPlugins.Clear();
        foreach (var info in _catalog.Where(c => !installed.Contains(c.Id)))
            CatalogPlugins.Add(new CatalogPluginRowViewModel(info, AddFromCatalogAsync));

        // Update badges on installed rows whose catalog version is newer than the loaded one.
        foreach (var row in Plugins)
        {
            var match = _catalog.FirstOrDefault(c => c.Id == row.Id);
            row.SetUpdate(match != null && PluginCatalogService.IsNewer(match.Version, _manager?.InstalledVersion(row.Id)) ? match : null);
        }

        this.RaisePropertyChanged(nameof(HasCatalog));
    }

    /// <summary>Add (install) an optional plugin from the catalog: download plugin → verify sha256 → load →
    /// fetch any declared runtime dependencies (e.g. ffmpeg/yt-dlp for HLS), reporting progress on the row
    /// and rolling the plugin back out if the user cancels or a dependency fails.</summary>
    internal async Task AddFromCatalogAsync(CatalogPluginRowViewModel row)
    {
        if (row == null || row.IsBusy)
            return;
        row.IsBusy = true;
        row.ErrorText = null;
        row.SetProgress(null, 0);
        row.BeginCancellable();
        try
        {
            var progress = new Progress<PluginDependencyProgress>(p =>
                row.SetProgress(p.Total > 1 ? $"{p.DependencyName} ({p.Index}/{p.Total})" : p.DependencyName, p.PercentComplete));
            var result = await PluginCatalogService
                .InstallOrUpdateAsync(_manager, row.Info, row.CancellationToken, progress)
                .ConfigureAwait(true);
            if (result.Success)
            {
                CatalogPlugins.Remove(row);
                _config?.DisabledPlugins?.Remove(row.Id); // ensure it starts enabled
                Refresh();
                ApplyCatalog();
                NotificationService.Inform(Localizer.Instance["Plugins_Installed"], result.Plugin?.Name ?? row.Name, false);
            }
            else
            {
                row.ErrorText = result.Error;
                NotificationService.Inform(Localizer.Instance["Plugins_AddFailed"], result.Error, true);
            }
        }
        catch (OperationCanceledException)
        {
            // User cancel — no error toast. The plugin was rolled back (PluginCatalogService), so
            // re-sync both lists: it must reappear in "More plugins" with an idle Add button, not
            // vanish until a restart re-fetches the catalog.
            Refresh();
            ApplyCatalog();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to add catalog plugin", ex);
            row.ErrorText = ex.Message;
            NotificationService.Inform(Localizer.Instance["Plugins_AddFailed"], ex.Message, true);
        }
        finally
        {
            row.IsBusy = false;
            row.EndCancellable();
        }
    }

    /// <summary>Accept an offered update on an installed row: unload old → download → verify → reload.</summary>
    internal async Task UpdateInstalledAsync(PluginRowViewModel row)
    {
        if (row?.PendingUpdate == null || row.IsBusy)
            return;
        row.IsBusy = true;
        try
        {
            var result = await PluginCatalogService.InstallOrUpdateAsync(_manager, row.PendingUpdate).ConfigureAwait(true);
            if (result.Success)
            {
                Refresh();
                ApplyCatalog();
                NotificationService.Inform(Localizer.Instance["Plugins_Installed"], result.Plugin?.Name ?? row.Name, false);
            }
            else
            {
                // The old copy may already be unloaded (removed just before the swap) — re-sync the lists
                // so the row reflects reality instead of a stale "installed" entry.
                Refresh();
                ApplyCatalog();
                NotificationService.Inform(Localizer.Instance["Plugins_AddFailed"], result.Error, true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to update plugin", ex);
            Refresh();
            ApplyCatalog();
            NotificationService.Inform(Localizer.Instance["Plugins_AddFailed"], ex.Message, true);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private void Refresh()
    {
        Plugins.Clear();
        foreach (var d in _manager?.Plugins ?? new List<PluginDescriptor>())
            Plugins.Add(new PluginRowViewModel(d, _manager, _config, RemoveRow, r => _ = UpdateInstalledAsync(r)));
        this.RaisePropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Uninstall a plugin (called by a row's Remove button): drop it from disk + memory and refresh.
    /// A catalog-tier plugin (e.g. HLS) must reappear in "More plugins" with an Add button right away —
    /// without <see cref="ApplyCatalog"/> it stayed invisible in both lists until the app restarted and
    /// re-fetched the catalog from scratch.</summary>
    private void RemoveRow(PluginRowViewModel row)
    {
        if (row == null)
            return;
        _manager?.RemovePlugin(row.Id);
        _config?.DisabledPlugins?.Remove(row.Id);
        Plugins.Remove(row);
        this.RaisePropertyChanged(nameof(IsEmpty));
        ApplyCatalog();
        NotificationService.Inform(Localizer.Instance["Plugins_Removed"], row.Name, false);
    }

    private void Reload()
    {
        _manager?.LoadFromDirectory(PluginManager.PluginsRoot);
        Refresh();
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(PluginManager.PluginsRoot);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = PluginManager.PluginsRoot,
                UseShellExecute = true
            });
        }
        catch
        {
            // best-effort
        }
    }

    private async Task InstallAsync()
    {
        var picked = await DialogHelper.OpenFilePicker(
            Localizer.Instance["Plugins_PickTitle"], "Plugin", "dll");
        if (picked == null)
            return;

        var before = _manager.Plugins.Select(p => p.Id).ToHashSet();
        try
        {
            Directory.CreateDirectory(PluginManager.PluginsRoot);
            var src = picked.LocalPath;
            var dest = Path.Combine(PluginManager.PluginsRoot, Path.GetFileName(src));
            File.Copy(src, dest, overwrite: true);
            // Carry the sidecar deps.json (emitted by EnableDynamicLoading) so a plugin with its own
            // dependencies still resolves them; without it AssemblyDependencyResolver finds nothing.
            var deps = Path.ChangeExtension(src, ".deps.json");
            if (File.Exists(deps))
                File.Copy(deps, Path.ChangeExtension(dest, ".deps.json"), overwrite: true);

            _manager.LoadFromDirectory(PluginManager.PluginsRoot);
            Refresh();

            var added = _manager.Plugins.Where(p => !before.Contains(p.Id)).ToList();
            if (added.Count > 0)
                NotificationService.Inform(
                    Localizer.Instance["Plugins_Installed"],
                    string.Join(", ", added.Select(a => a.Name)), false);
            else
                // The file copied but exposed no IDownloaderPlugin (wrong DLL, or a stale build compiled
                // against an older SDK) — tell the user instead of failing silently ("nothing happened").
                NotificationService.Inform(
                    Localizer.Instance["Plugins_Install"],
                    Localizer.Instance["Plugins_NoneFound"], true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to install plugin", ex);
            NotificationService.Inform(Localizer.Instance["Plugins_Install"], ex.Message, true);
        }
    }
}

/// <summary>One row in the Plugins list — name/version/author/description + an enable toggle.</summary>
public class PluginRowViewModel : ViewModelBase
{
    private readonly PluginDescriptor _descriptor;
    private readonly PluginManager _manager;
    private readonly Config _config;

    public PluginRowViewModel(PluginDescriptor descriptor, PluginManager manager, Config config,
        Action<PluginRowViewModel> onRemove = null, Action<PluginRowViewModel> onUpdate = null)
    {
        _descriptor = descriptor;
        _manager = manager;
        _config = config;
        RemoveCommand = ReactiveCommand.Create(() => onRemove?.Invoke(this));
        UpdateCommand = ReactiveCommand.Create(() => onUpdate?.Invoke(this));
    }

    public ICommand RemoveCommand { get; }
    public ICommand UpdateCommand { get; }

    public string Id => _descriptor.Id;
    public string Name => _descriptor.Name;
    public string Author => _descriptor.Author;
    public string Description => _descriptor.Description;
    public string VersionText => $"v{_descriptor.Version}";

    /// <summary>Built-ins ship with the app: they can be disabled but not removed (hides the trash button).</summary>
    public bool IsBuiltIn => _descriptor.IsBuiltIn;
    public bool CanRemove => !_descriptor.IsBuiltIn;

    private CatalogPluginInfo _pendingUpdate;
    /// <summary>The newer catalog entry offered for this installed plugin, or null when up to date.</summary>
    public CatalogPluginInfo PendingUpdate => _pendingUpdate;
    public bool UpdateAvailable => _pendingUpdate != null;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    /// <summary>Set (or clear) the available update for this row; refreshes the update badge/button.</summary>
    public void SetUpdate(CatalogPluginInfo info)
    {
        _pendingUpdate = info;
        this.RaisePropertyChanged(nameof(PendingUpdate));
        this.RaisePropertyChanged(nameof(UpdateAvailable));
    }

    public bool IsEnabled
    {
        get => _descriptor.IsEnabled;
        set
        {
            if (_descriptor.IsEnabled == value)
                return;
            _manager.SetEnabled(_descriptor.Id, value);
            // Persist: a DISABLED plugin id is remembered so it stays off across restarts.
            _config.DisabledPlugins ??= new List<string>();
            if (value) _config.DisabledPlugins.Remove(_descriptor.Id);
            else if (!_config.DisabledPlugins.Contains(_descriptor.Id)) _config.DisabledPlugins.Add(_descriptor.Id);
            this.RaisePropertyChanged();
        }
    }
}

/// <summary>One row in the "More plugins" catalog list — an optional plugin the user can Add on demand.</summary>
public class CatalogPluginRowViewModel : ViewModelBase
{
    private readonly CatalogPluginInfo _info;
    private CancellationTokenSource _cts;

    public CatalogPluginRowViewModel(CatalogPluginInfo info, Func<CatalogPluginRowViewModel, Task> onAdd)
    {
        _info = info;
        AddCommand = ReactiveCommand.CreateFromTask(() => onAdd?.Invoke(this) ?? Task.CompletedTask);
        CancelCommand = ReactiveCommand.Create(() => _cts?.Cancel());
    }

    public ICommand AddCommand { get; }
    public ICommand CancelCommand { get; }

    public CatalogPluginInfo Info => _info;
    public string Id => _info.Id;
    public string Name => _info.Name;
    public string Description => _info.Description;
    public string VersionText => $"v{_info.Version}";

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private string _errorText;
    /// <summary>Set when a download/verify/install attempt failed; shown inline under the row.</summary>
    public string ErrorText
    {
        get => _errorText;
        set { this.RaiseAndSetIfChanged(ref _errorText, value); this.RaisePropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(_errorText);

    private double _progressPercent;
    /// <summary>0-100 progress shown under the Add button while <see cref="IsBusy"/>.</summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => this.RaiseAndSetIfChanged(ref _progressPercent, value);
    }

    private string _progressText;
    /// <summary>E.g. "yt-dlp (2/3)"; null while just downloading the plugin package itself.</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => this.RaiseAndSetIfChanged(ref _progressText, value);
    }

    public void SetProgress(string text, double percent)
    {
        ProgressText = text;
        ProgressPercent = percent;
    }

    /// <summary>Cancellation token for the in-flight Add attempt (cancelled by <see cref="CancelCommand"/>).</summary>
    public CancellationToken CancellationToken => (_cts ??= new CancellationTokenSource()).Token;

    public void BeginCancellable() => _cts = new CancellationTokenSource();

    public void EndCancellable()
    {
        _cts?.Dispose();
        _cts = null;
    }
}
