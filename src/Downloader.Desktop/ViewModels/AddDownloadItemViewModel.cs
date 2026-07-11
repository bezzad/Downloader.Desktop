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
    private readonly IDownloadManager _manager;
    private readonly Func<string, DownloadConfiguration, Task<(string FileName, long FileSize)?>> _resolveFileInfo;
    private readonly Func<Task<string>> _readClipboard;
    private bool _isAddingQueue;
    private string _newQueueName;
    private readonly TimeSpan _resolveDebounce;
    private string _urls;
    private string _fileName;
    private string _storageFolderPath;
    private DownloadQueue _selectedQueue;
    private string _sizeText;
    private string _clipboardSuggestion;
    private int _clipboardUrlCount;
    private bool _resolving;
    private bool _userTypedName;
    private CancellationTokenSource _resolveCts;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<Plugins.LinkVariant>>> _getVariants;
    private bool _fetchingVariants;
    private CancellationTokenSource _variantsCts;

    public AddDownloadItemViewModel(
        Config config,
        string url,
        Func<string, DownloadConfiguration, Task<(string FileName, long FileSize)?>> resolveFileInfo = null,
        TimeSpan? resolveDebounce = null,
        Func<Task<string>> readClipboard = null,
        IDownloadManager manager = null,
        Func<string, CancellationToken, Task<IReadOnlyList<Plugins.LinkVariant>>> getVariants = null)
    {
        _config = config;
        _manager = manager;
        _getVariants = getVariants;
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
        AddQueueCommand = ReactiveCommand.Create(() => { IsAddingQueue = true; });
        ConfirmAddQueueCommand = ReactiveCommand.Create(ConfirmAddQueue);
        CancelAddQueueCommand = ReactiveCommand.Create(() => { NewQueueName = string.Empty; IsAddingQueue = false; });

        if (!string.IsNullOrWhiteSpace(_urls))
        {
            TriggerResolve();
            TriggerVariantLookup();
        }

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
            TriggerVariantLookup();
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

    /// <summary>Adding is blocked while a variant lookup is in flight (author's decision: on links whose
    /// resolver offers choices — video qualities, model tags — the user must see them before starting).
    /// Non-claimed links finish the lookup near-instantly, so plain adds are unaffected.</summary>
    public bool CanDownload => ParsedUrls.Count > 0 && !IsFetchingVariants;

    // ---- Resolver variants (quality / model-tag picker) ----

    /// <summary>The selectable variants the claiming resolver offered for the single entered link
    /// (empty = none). Mutated in place so the ItemsControl stays bound.</summary>
    public System.Collections.ObjectModel.ObservableCollection<VariantOptionViewModel> Variants { get; } = new();

    public bool HasVariants => Variants.Count > 0;

    /// <summary>True while the resolver's variant lookup runs (shows "Fetching options…" and blocks Add).</summary>
    public bool IsFetchingVariants
    {
        get => _fetchingVariants;
        private set
        {
            this.RaiseAndSetIfChanged(ref _fetchingVariants, value);
            this.RaisePropertyChanged(nameof(CanDownload));
        }
    }

    /// <summary>Kick (or clear) the variant lookup for the current input. Only a single URL is looked up;
    /// editing the input cancels the previous lookup. Failure/none quietly falls back to a plain add.</summary>
    private async void TriggerVariantLookup()
    {
        _variantsCts?.Cancel();
        Variants.Clear();
        this.RaisePropertyChanged(nameof(HasVariants));

        var urls = ParsedUrls;
        if (_getVariants == null || urls.Count != 1)
        {
            IsFetchingVariants = false;
            return;
        }

        var url = urls[0];
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90)); // safety valve — never wedge Add
        _variantsCts = cts;
        try
        {
            IsFetchingVariants = true;
            var variants = await _getVariants(url, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || ParsedUrls is not [var current] || current != url)
                return;

            if (variants is { Count: > 0 })
            {
                foreach (var v in variants)
                    Variants.Add(new VariantOptionViewModel(v));
                this.RaisePropertyChanged(nameof(HasVariants));
            }
        }
        catch
        {
            // lookup failure = no choices; the add proceeds with the resolver's default pick
        }
        finally
        {
            if (_variantsCts == cts)
                IsFetchingVariants = false;
        }
    }

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
    /// suggestion (the FULL text that gets committed on accept). Never written into <see cref="Urls"/>
    /// until the user accepts it (Enter/Tab). For display use <see cref="ClipboardSuggestionDisplay"/>.</summary>
    public string ClipboardSuggestion
    {
        get => _clipboardSuggestion;
        private set
        {
            this.RaiseAndSetIfChanged(ref _clipboardSuggestion, value);
            this.RaisePropertyChanged(nameof(ShowClipboardSuggestion));
            this.RaisePropertyChanged(nameof(ClipboardSuggestionDisplay));
            this.RaisePropertyChanged(nameof(LinksPlaceholder));
        }
    }

    /// <summary>One-line overlay text for the suggestion: the single URL, or an "N links" summary for a
    /// multi-URL clipboard — so a big clipboard never floods the box (the overlay stays a single line).</summary>
    public string ClipboardSuggestionDisplay =>
        _clipboardUrlCount > 1
            ? string.Format(Localizer.Instance["Add_ClipboardMultiple"], _clipboardUrlCount)
            : _clipboardSuggestion;

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

            _clipboardUrlCount = urls.Count;
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
        _clipboardUrlCount = 0;
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

    // A fresh list each read: re-raising PropertyChanged with the SAME list instance would not make the
    // ComboBox re-enumerate (same lesson as the queue MenuFlyout gotcha).
    public List<DownloadQueue> Queues => _config?.Queues?.ToList();

    public DownloadQueue SelectedQueue
    {
        get => _selectedQueue;
        set => this.RaiseAndSetIfChanged(ref _selectedQueue, value);
    }

    public bool ShowQueuePicker => (_config?.Queues?.Count ?? 0) > 1;

    // ---- Inline queue creation (the "Add queue" button next to Add) ----

    public ICommand AddQueueCommand { get; }
    public ICommand ConfirmAddQueueCommand { get; }
    public ICommand CancelAddQueueCommand { get; }

    /// <summary>The button only shows when the dialog has a manager to create queues through
    /// (design-time / legacy constructions don't).</summary>
    public bool CanAddQueue => _manager != null;

    /// <summary>True while the inline "name the new queue" row is showing.</summary>
    public bool IsAddingQueue
    {
        get => _isAddingQueue;
        private set => this.RaiseAndSetIfChanged(ref _isAddingQueue, value);
    }

    public string NewQueueName
    {
        get => _newQueueName;
        set => this.RaiseAndSetIfChanged(ref _newQueueName, value);
    }

    /// <summary>Creates the queue through the manager (so QueuesChanged/pump wiring stays consistent),
    /// refreshes the picker and selects the new queue. Empty/whitespace names are ignored.</summary>
    public void ConfirmAddQueue()
    {
        var name = NewQueueName?.Trim();
        if (string.IsNullOrEmpty(name) || _manager == null)
            return;

        var queue = _manager.AddQueue(name);
        NewQueueName = string.Empty;
        IsAddingQueue = false;
        this.RaisePropertyChanged(nameof(Queues));
        this.RaisePropertyChanged(nameof(ShowQueuePicker));
        SelectedQueue = queue; // the download(s) being added land in the new queue
    }

    private async Task SelectFileStoragePathAsync()
    {
        var path = await DialogHelper.OpenFolderPicker("Select a folder to save the file(s) in", View);
        if (path != null)
            StorageFolderPath = path.LocalPath;
    }

    private void StartDownload()
    {
        var items = BuildItems();
        if (items.Count > 0)
            View.Close(items);
    }

    /// <summary>The download descriptors the current dialog state produces (one per URL, or one per
    /// checked variant of a single variant-capable link). Internal seam so tests don't need a window.</summary>
    internal List<DownloadItem> BuildItems()
    {
        var urls = ParsedUrls;
        if (urls.Count == 0)
            return new List<DownloadItem>();

        var folder = string.IsNullOrWhiteSpace(StorageFolderPath)
            ? _config?.Settings?.DefaultSavePath
            : StorageFolderPath;

        // Remember the chosen folder as the default for next time — unless the user turned that off,
        // in which case adding a download must not change the default save path.
        if (_config?.Settings is { RememberLastSavePath: true } && !string.IsNullOrWhiteSpace(folder))
            _config.Settings.DefaultSavePath = folder;

        var single = urls.Count == 1;

        // A single link whose resolver offered variants: one download per CHECKED variant. A variant that
        // IS its own link (Ollama tag) substitutes the URL; a facet variant (video quality) keeps the
        // original link and persists the variant id so retries re-resolve the same choice. None checked
        // (or no variants) = plain add with the resolver's default pick.
        var chosen = single ? Variants.Where(v => v.IsChecked).ToList() : new List<VariantOptionViewModel>();
        if (chosen.Count > 0)
        {
            var vGroup = chosen.Count > 1 ? $"Batch · {DateTime.Now:dd MMM HH:mm}" : null;
            var variantItems = chosen.Select(v => new DownloadItem
            {
                Urls = new List<string> { v.SubstituteUrl ?? urls[0].Trim() },
                SaveFolder = folder,
                // The resolver names each variant distinctly; a typed name only applies to a lone pick.
                FileName = chosen.Count == 1 && !string.IsNullOrWhiteSpace(Filename) ? Filename.Trim() : null,
                VariantId = v.SubstituteUrl == null ? v.Id : null,
                Group = vGroup,
                QueueId = SelectedQueue?.Id ?? _config?.DefaultQueue?.Id,
                Status = DownloadStatus.Created,
                LastTry = DateTime.Now
            }).ToList();
            return variantItems;
        }

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

        return items;
    }
}

/// <summary>One selectable resolver variant row in the Add dialog (checkbox + label).</summary>
public class VariantOptionViewModel : ViewModelBase
{
    private bool _isChecked;

    public VariantOptionViewModel(Plugins.LinkVariant variant)
    {
        Id = variant.Id;
        Label = variant.Label;
        SubstituteUrl = variant.SubstituteUrl;
        _isChecked = variant.IsDefault;
    }

    public string Id { get; }
    public string Label { get; }
    public string SubstituteUrl { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => this.RaiseAndSetIfChanged(ref _isChecked, value);
    }
}
