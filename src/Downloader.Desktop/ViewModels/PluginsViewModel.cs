using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Downloader.Desktop.Models;
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

    public ObservableCollection<PluginRowViewModel> Plugins { get; } = new();

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

    private void Refresh()
    {
        Plugins.Clear();
        foreach (var d in _manager?.Plugins ?? new List<PluginDescriptor>())
            Plugins.Add(new PluginRowViewModel(d, _manager, _config));
        this.RaisePropertyChanged(nameof(IsEmpty));
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
        try
        {
            Directory.CreateDirectory(PluginManager.PluginsRoot);
            var src = picked.LocalPath;
            var dest = Path.Combine(PluginManager.PluginsRoot, Path.GetFileName(src));
            File.Copy(src, dest, overwrite: true);
            _manager.LoadFromDirectory(PluginManager.PluginsRoot);
            Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to install plugin", ex);
        }
    }
}

/// <summary>One row in the Plugins list — name/version/author/description + an enable toggle.</summary>
public class PluginRowViewModel : ViewModelBase
{
    private readonly PluginDescriptor _descriptor;
    private readonly PluginManager _manager;
    private readonly Config _config;

    public PluginRowViewModel(PluginDescriptor descriptor, PluginManager manager, Config config)
    {
        _descriptor = descriptor;
        _manager = manager;
        _config = config;
    }

    public string Name => _descriptor.Name;
    public string Author => _descriptor.Author;
    public string Description => _descriptor.Description;
    public string VersionText => $"v{_descriptor.Version}";

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
