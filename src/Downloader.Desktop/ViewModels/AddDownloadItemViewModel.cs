using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Downloader.Desktop.Models;
using Downloader.Desktop.Services;
using ReactiveUI;

namespace Downloader.Desktop.ViewModels;

/// <summary>
/// Collects one or more URLs (one per line) + a destination folder (and optional name for a single
/// URL) and returns the <see cref="DownloadItem"/> descriptors. Blank names are auto-resolved by the
/// engine from the URL / Content-Disposition headers.
/// </summary>
public class AddDownloadItemViewModel : ViewModelBase
{
    private readonly Config _config;
    private readonly Func<string, DownloadConfiguration, Task<(string FileName, long FileSize)?>> _resolveFileInfo;
    private readonly Func<Task<string>> _readClipboard;
    private readonly TimeSpan _resolveDebounce;
    private string _urls;
    private string _fileName;
    private string _storageFolderPath;
    private DownloadQueue _selectedQueue;
    private string _sizeText;
    private string _clipboardSuggestion;
    private bool _resolving;
    private bool _userTypedName;
    private CancellationTokenSource _resolveCts;

    public AddDownloadItemViewModel(
        Config config,
        string url,
        Func<string, DownloadConfiguration, Task<(string FileName, long FileSize)?>> resolveFileInfo = null,
        TimeSpan? resolveDebounce = null,
        Func<Task<string>> readClipboard = null)
    {
        _config = config;
        _resolveFileInfo = resolveFileInfo ?? DefaultResolveFileInfoAsync;
        _readClipboard = readClipboard ?? DefaultReadClipboardAsync;
        _resolveDebounce = resolveDebounce ?? TimeSpan.FromMilliseconds(600);
        _urls = url ?? string.Empty;
        _storageFolderPath = !string.IsNullOrWhiteSpace(config?.Settings?.DefaultSavePath)
            ? config.Settings.DefaultSavePath
            : DownloadSettings.New().DefaultSavePath;
        _fileName = string.Empty;
        _selectedQueue = config?.DefaultQueue;

        SelectFileStoragePathCommand = ReactiveCommand.CreateFromTask(SelectFileStoragePathAsync);
        StartDownloadCommand = ReactiveCommand.Create(StartDownload);

        if (!string.IsNullOrWhiteSpace(_urls))
            TriggerResolve();

        // Only offer a clipboard suggestion when the dialog opens with no seed URL ("user have no any
        // link added before"). Exposed as a task so tests can await the async probe deterministically.
        ClipboardSuggestionReady = string.IsNullOrWhiteSpace(_urls)
            ? LoadClipboardSuggestionAsync()
            : Task.CompletedTask;
    }

    private static async Task<string> DefaultReadClipboardAsync()
    {
        try
        {
            var clipboard = (DialogHelper.MainWindow as Avalonia.Controls.TopLevel)?.Clipboard;
            if (clipboard == null)
                return null;
            return await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard).ConfigureAwait(true);
        }
        catch
        {
            return null; // clipboard unavailable / denied — no suggestion, dialog still opens fine
        }
    }

    private static async Task<(string FileName, long FileSize)?> DefaultResolveFileInfoAsync(string url, DownloadConfiguration configuration)
    {
        var info = await UrlResolver.ResolveFileInfoAsync(url, configuration).ConfigureAwait(true);
        return info == null ? null : (info.FileName, info.FileSize);
    }

    public ICommand SelectFileStoragePathCommand { get; }
    public ICommand StartDownloadCommand { get; }

    /// <summary>One or more links, one per line.</summary>
    public string Urls
    {
        get => _urls;
        set
        {
            this.RaiseAndSetIfChanged(ref _urls, value);
            this.RaisePropertyChanged(nameof(CanDownload));
            this.RaisePropertyChanged(nameof(IsMultiple));
            this.RaisePropertyChanged(nameof(IsSingleLink));
            this.RaisePropertyChanged(nameof(IsFilenameEnabled));
            this.RaisePropertyChanged(nameof(ShowClipboardSuggestion));
            this.RaisePropertyChanged(nameof(LinksPlaceholder));
            TriggerResolve();
        }
    }

    // Accept multiple links separated by new lines, spaces, tabs, commas or semicolons so pasting a
    // batch into the single-line top box (which can collapse new lines to spaces) still splits (#19).
    private static readonly char[] UrlSeparators = { '\n', '\r', '\t', ' ', ',', ';' };

    private static IReadOnlyList<string> SplitUrls(string raw) =>
        (raw ?? string.Empty)
        .Split(UrlSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(u => u.Length > 0)
        .ToList();

    private IReadOnlyList<string> ParsedUrls => SplitUrls(_urls);

    private static bool IsHttpUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public bool CanDownload => ParsedUrls.Count > 0;

    /// <summary>True when more than one URL is entered (file name field is then ignored).</summary>
    public bool IsMultiple => ParsedUrls.Count > 1;

    /// <summary>True when exactly one URL is entered — enables name/size pre-resolution.</summary>
    public bool IsSingleLink => ParsedUrls.Count == 1;

    /// <summary>The File name box is disabled for multi-link adds (per-file names don't apply — folder only).</summary>
    public bool IsFilenameEnabled => !IsMultiple;

    /// <summary>Resolved size for a single link (e.g. "altogether 712 MB"), or null/Unknown when not yet known.</summary>
    public string SizeText
    {
        get => _sizeText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _sizeText, value);
            this.RaisePropertyChanged(nameof(HasSizeText));
        }
    }

    public bool HasSizeText => !string.IsNullOrEmpty(_sizeText);

    /// <summary>True while a single-link name/size probe is in flight (shows a "Resolving…" hint).</summary>
    public bool Resolving
    {
        get => _resolving;
        private set => this.RaiseAndSetIfChanged(ref _resolving, value);
    }

    /// <summary>Completes once the initial clipboard-suggestion probe (if any) has finished. For tests/sequencing.</summary>
    public Task ClipboardSuggestionReady { get; }

    /// <summary>URL(s) found on the clipboard when the dialog opened empty, offered as a non-committed
    /// suggestion. Never written into <see cref="Urls"/> until the user accepts it (Enter/Tab).</summary>
    public string ClipboardSuggestion
    {
        get => _clipboardSuggestion;
        private set
        {
            this.RaiseAndSetIfChanged(ref _clipboardSuggestion, value);
            this.RaisePropertyChanged(nameof(ShowClipboardSuggestion));
            this.RaisePropertyChanged(nameof(LinksPlaceholder));
        }
    }

    /// <summary>Show the placeholder-style suggestion overlay only while the real box is empty and a suggestion exists.</summary>
    public bool ShowClipboardSuggestion => !string.IsNullOrEmpty(_clipboardSuggestion) && string.IsNullOrEmpty(_urls);

    /// <summary>The links box placeholder — blanked while the clipboard suggestion overlay is showing so the two
    /// don't visually collide (both only render when the box is empty).</summary>
    public string LinksPlaceholder => ShowClipboardSuggestion ? string.Empty : Localizer.Instance["Add_LinksPlaceholder"];

    /// <summary>Reads the clipboard once and, if it holds ≥1 valid http/https URL, stores it as a suggestion.
    /// Any failure (no clipboard, denied, non-URL text) leaves the dialog unchanged.</summary>
    private async Task LoadClipboardSuggestionAsync()
    {
        try
        {
            var text = await _readClipboard().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(text))
                return;
            // The user may have typed while the async read was in flight — don't suggest over real input.
            if (!string.IsNullOrEmpty(_urls))
                return;

            var urls = SplitUrls(text).Where(IsHttpUrl).ToList();
            if (urls.Count == 0)
                return;

            ClipboardSuggestion = string.Join(Environment.NewLine, urls);
        }
        catch
        {
            // fail open — a clipboard hiccup must never break opening the dialog
        }
    }

    /// <summary>Commits the clipboard suggestion into <see cref="Urls"/> (reusing the full parse/resolve
    /// pipeline) and clears the suggestion. No-op if there's nothing to accept.</summary>
    public void AcceptClipboardSuggestion()
    {
        if (string.IsNullOrEmpty(_clipboardSuggestion))
            return;
        Urls = _clipboardSuggestion;
        ClipboardSuggestion = null;
    }

    /// <summary>
    /// For a single link: debounce, then probe the remote file name + size off the server and prefill the
    /// dialog (non-blocking). Cancels any in-flight probe on each change; only applies a result if the URL
    /// is still the same single link and the user hasn't typed their own name.
    /// </summary>
    private async void TriggerResolve()
    {
        _resolveCts?.Cancel();
        var urls = ParsedUrls;
        if (urls.Count != 1)
        {
            Resolving = false;
            SizeText = null;
            return;
        }

        var url = urls[0];
        var cts = new CancellationTokenSource();
        _resolveCts = cts;
        try
        {
            if (!_userTypedName)
            {
                var fromUrl = UrlResolver.NameFromUrl(url);
                // Keep the textbox in sync with the current URL immediately; probe result may refine it later.
                this.RaiseAndSetIfChanged(ref _fileName, fromUrl ?? string.Empty, nameof(Filename));
            }

            await Task.Delay(_resolveDebounce, cts.Token).ConfigureAwait(true); // debounce keystrokes
            Resolving = true;
            var info = await _resolveFileInfo(url, _config?.Settings?.ToConfiguration()).ConfigureAwait(true);

            if (cts.IsCancellationRequested)
                return;
            // Make sure the input still describes this exact single link.
            var now = ParsedUrls;
            if (now.Count != 1 || now[0] != url)
                return;

            if (info.HasValue)
            {
                if (!_userTypedName && !string.IsNullOrWhiteSpace(info.Value.FileName))
                {
                    this.RaiseAndSetIfChanged(ref _fileName, info.Value.FileName, nameof(Filename));
                }
                SizeText = info.Value.FileSize > 0
                    ? DownloadItemViewModel.FormatBytes(info.Value.FileSize)
                    : "Unknown size";
            }
            else
            {
                SizeText = null;
            }
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke — ignore
        }
        catch
        {
            SizeText = null; // best-effort; dialog stays usable
        }
        finally
        {
            if (_resolveCts == cts)
                Resolving = false;
        }
    }

    public string StorageFolderPath
    {
        get => _storageFolderPath;
        set => this.RaiseAndSetIfChanged(ref _storageFolderPath, value);
    }

    /// <summary>Optional, single-URL only. Blank means "auto-detect from the link".</summary>
    public string Filename
    {
        get => _fileName;
        set
        {
            // A non-empty value the user typed wins over a later auto-resolve; clearing it re-enables resolve.
            _userTypedName = !string.IsNullOrWhiteSpace(value);
            this.RaiseAndSetIfChanged(ref _fileName, value);
        }
    }

    public List<DownloadQueue> Queues => _config?.Queues;

    public DownloadQueue SelectedQueue
    {
        get => _selectedQueue;
        set => this.RaiseAndSetIfChanged(ref _selectedQueue, value);
    }

    public bool ShowQueuePicker => (_config?.Queues?.Count ?? 0) > 1;

    private async Task SelectFileStoragePathAsync()
    {
        var path = await DialogHelper.OpenFolderPicker("Select a folder to save the file(s) in", View);
        if (path != null)
            StorageFolderPath = path.LocalPath;
    }

    private void StartDownload()
    {
        var urls = ParsedUrls;
        if (urls.Count == 0)
            return;

        var folder = string.IsNullOrWhiteSpace(StorageFolderPath)
            ? _config?.Settings?.DefaultSavePath
            : StorageFolderPath;

        // Remember the chosen folder as the default for next time — unless the user turned that off,
        // in which case adding a download must not change the default save path.
        if (_config?.Settings is { RememberLastSavePath: true } && !string.IsNullOrWhiteSpace(folder))
            _config.Settings.DefaultSavePath = folder;

        var single = urls.Count == 1;
        // Tag a multi-URL add as one group so the list can show them together (#13).
        var group = single ? null : $"Batch · {DateTime.Now:dd MMM HH:mm}";
        var items = urls.Select(u => new DownloadItem
        {
            Urls = new List<string> { u.Trim() },
            SaveFolder = folder,
            // Custom name only applies to a single download; batches always auto-resolve.
            FileName = single && !string.IsNullOrWhiteSpace(Filename) ? Filename.Trim() : null,
            Group = group,
            QueueId = SelectedQueue?.Id ?? _config?.DefaultQueue?.Id,
            Status = DownloadStatus.Created,
            LastTry = DateTime.Now
        }).ToList();

        View.Close(items);
    }
}
