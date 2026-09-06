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
    private readonly Func<string, string> _getResolverName;
    private readonly ApiAddRequest _apiRequest;
    private string _resolverName;
    private bool _fetchingVariants;
    private string _variantError = string.Empty;
    private CancellationTokenSource _variantsCts;
    private IReadOnlyList<string> _parsed = Array.Empty<string>();

    /// <summary>Above this many links the modal shows a compact summary instead of an editable box —
    /// Avalonia's multi-line TextBox lays out ALL lines (no virtualization), so rendering thousands
    /// freezes the UI. Normal multi-adds (a few dozen) stay editable.</summary>
    public const int BulkPreviewThreshold = 200;

    public AddDownloadItemViewModel(
        Config config,
        string url,
        Func<string, DownloadConfiguration, Task<(string FileName, long FileSize)?>> resolveFileInfo = null,
        TimeSpan? resolveDebounce = null,
        Func<Task<string>> readClipboard = null,
        IDownloadManager manager = null,
        Func<string, CancellationToken, Task<IReadOnlyList<Plugins.LinkVariant>>> getVariants = null,
        Func<string, string> getResolverName = null,
        ApiAddRequest apiRequest = null)
    {
        _config = config;
        _apiRequest = apiRequest;
        _manager = manager;
        _getVariants = getVariants;
        _getResolverName = getResolverName;
        _resolveFileInfo = resolveFileInfo ?? DefaultResolveFileInfoAsync;
        _readClipboard = readClipboard ?? DefaultReadClipboardAsync;
        _resolveDebounce = resolveDebounce ?? TimeSpan.FromMilliseconds(600);
        _urls = url ?? string.Empty;
        _storageFolderPath = !string.IsNullOrWhiteSpace(config?.Settings?.DefaultSavePath)
            ? config.Settings.DefaultSavePath
            : DownloadSettings.New().DefaultSavePath;
        _fileName = string.Empty;
        _selectedQueue = config?.DefaultQueue;

        // A confirm-mode programmatic add opens this dialog carrying everything the request asked for, so
        // the user reviews the real download rather than a bare URL. Only the VISIBLE fields are seeded
        // here; the request context (cookies, headers, referer) is not the user's to edit and is applied
        // to the built item in BuildItems.
        if (apiRequest != null)
        {
            if (!string.IsNullOrWhiteSpace(apiRequest.Filename))
            {
                _fileName = apiRequest.Filename.Trim();
                _userTypedName = true; // a name the caller chose must not be overwritten by the name probe
            }
            if (!string.IsNullOrWhiteSpace(apiRequest.Path))
                _storageFolderPath = apiRequest.Path.Trim();
            if (!string.IsNullOrWhiteSpace(apiRequest.Queue) && config?.Queues != null)
                _selectedQueue = config.Queues.FirstOrDefault(q =>
                    string.Equals(q.Id, apiRequest.Queue, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(q.Name, apiRequest.Queue, StringComparison.OrdinalIgnoreCase)) ?? _selectedQueue;
        }

        _parsed = SplitUrls(_urls);

        SelectFileStoragePathCommand = ReactiveCommand.CreateFromTask(SelectFileStoragePathAsync);
        StartDownloadCommand = ReactiveCommand.Create(StartDownload);
        ClearUrlsCommand = ReactiveCommand.Create(() => { Urls = string.Empty; });
        AddQueueCommand = ReactiveCommand.Create(() => { IsAddingQueue = true; });
        ConfirmAddQueueCommand = ReactiveCommand.Create(ConfirmAddQueue);
        CancelAddQueueCommand = ReactiveCommand.Create(() => { NewQueueName = string.Empty; IsAddingQueue = false; });

        if (!string.IsNullOrWhiteSpace(_urls))
        {
            TriggerResolve();
            TriggerVariantLookup();
        }
        RefreshResolverBadge();

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
            // Parse once per change (cheap) and cache — the getters below used to re-split on every read.
            _parsed = SplitUrls(value);
            this.RaisePropertyChanged(nameof(CanDownload));
            this.RaisePropertyChanged(nameof(IsMultiple));
            this.RaisePropertyChanged(nameof(IsSingleLink));
            this.RaisePropertyChanged(nameof(IsFilenameEnabled));
            this.RaisePropertyChanged(nameof(LinkCount));
            this.RaisePropertyChanged(nameof(IsBulk));
            this.RaisePropertyChanged(nameof(BulkSummaryText));
            this.RaisePropertyChanged(nameof(ShowClipboardSuggestion));
            this.RaisePropertyChanged(nameof(LinksPlaceholder));
            TriggerResolve();
            TriggerVariantLookup();
            RefreshResolverBadge();
        }
    }

    // Accept multiple links separated by new lines, spaces, tabs, commas or semicolons so pasting a
    // batch into the single-line top box (which can collapse new lines to spaces) still splits (#19).
    private static readonly char[] UrlSeparators = { '\n', '\r', '\t', ' ', ',', ';' };

    /// <summary>Count the links in raw text (same splitting the dialog uses) — lets the paste handlers
    /// decide whether a paste is "bulk" without laying it out in a TextBox first.</summary>
    public static int CountUrls(string raw) => SplitUrls(raw).Count;

    private static IReadOnlyList<string> SplitUrls(string raw) =>
        (raw ?? string.Empty)
        .Split(UrlSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(u => u.Length > 0)
        .ToList();

    private IReadOnlyList<string> ParsedUrls => _parsed;

    /// <summary>Number of links currently entered.</summary>
    public int LinkCount => _parsed.Count;

    /// <summary>True for a large paste: the editable box is replaced by a summary chip so Avalonia
    /// never lays out thousands of lines (the ~10 s freeze). See <see cref="BulkPreviewThreshold"/>.</summary>
    public bool IsBulk => _parsed.Count > BulkPreviewThreshold;

    /// <summary>Summary shown in bulk mode, e.g. "2000 links ready to add".</summary>
    public string BulkSummaryText => string.Format(Localizer.Instance["Add_BulkSummary"], _parsed.Count);

    /// <summary>Clears the entered links (returns from bulk summary to the editable box).</summary>
    public ICommand ClearUrlsCommand { get; }

    private static bool IsHttpUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Adding is blocked while a variant lookup is in flight (author's decision: on links whose
    /// resolver offers choices — video qualities, model tags — the user must see them before starting).
    /// Non-claimed links finish the lookup near-instantly, so plain adds are unaffected.</summary>
    public bool CanDownload => ParsedUrls.Count > 0 && !IsFetchingVariants;

    // ---- Resolver badge ("Handled by <plugin>") ----

    /// <summary>The display name of the plugin whose resolver claims the single pasted URL, or null.
    /// Cheap + sync (CanResolve pass only) so it updates live per keystroke; no badge for multi-URL
    /// input or unclaimed links (those are plain engine downloads).</summary>
    public string ResolverName
    {
        get => _resolverName;
        private set
        {
            this.RaiseAndSetIfChanged(ref _resolverName, value);
            this.RaisePropertyChanged(nameof(HasResolver));
            this.RaisePropertyChanged(nameof(ResolverBadgeText));
        }
    }

    public bool HasResolver => !string.IsNullOrWhiteSpace(_resolverName);

    /// <summary>Localized badge text, e.g. "Handled by Website offline copy".</summary>
    public string ResolverBadgeText => HasResolver
        ? string.Format(Localizer.Instance["Add_HandledBy"], _resolverName)
        : string.Empty;

    private void RefreshResolverBadge()
    {
        string name = null;
        if (_getResolverName != null && ParsedUrls is [var single])
        {
            try { name = _getResolverName(single); }
            catch { /* a broken plugin claim must not break typing in the Add box */ }
        }
        ResolverName = name;
    }

    // ---- Resolver variants (quality / model-tag picker) ----

    /// <summary>The selectable variants the claiming resolver offered for the single entered link
    /// (empty = none). Mutated in place so the ItemsControl stays bound.</summary>
    public System.Collections.ObjectModel.ObservableCollection<VariantOptionViewModel> Variants { get; } = new();

    public bool HasVariants => Variants.Count > 0;

    /// <summary>Why the claiming resolver couldn't offer any options (e.g. "this site wants a signed-in
    /// session"), or empty when there's nothing to report. Shown in the Add window so a link the plugin
    /// can't handle explains itself up front instead of failing silently and again after Download.</summary>
    public string VariantError
    {
        get => _variantError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _variantError, value);
            this.RaisePropertyChanged(nameof(HasVariantError));
            this.RaisePropertyChanged(nameof(ShowVariantSection));
        }
    }

    public bool HasVariantError => !string.IsNullOrEmpty(_variantError);

    /// <summary>The whole variant block (spinner, list or error) — gates the star row in the dialog grid.</summary>
    public bool ShowVariantSection => IsFetchingVariants || HasVariants || HasVariantError;

    /// <summary>True while the resolver's variant lookup runs (shows "Fetching options…" and blocks Add).</summary>
    public bool IsFetchingVariants
    {
        get => _fetchingVariants;
        private set
        {
            this.RaiseAndSetIfChanged(ref _fetchingVariants, value);
            this.RaisePropertyChanged(nameof(CanDownload));
            this.RaisePropertyChanged(nameof(ShowVariantSection));
        }
    }

    /// <summary>Kick (or clear) the variant lookup for the current input. Only a single URL is looked up;
    /// editing the input cancels the previous lookup. Failure/none quietly falls back to a plain add.</summary>
    private async void TriggerVariantLookup()
    {
        _variantsCts?.Cancel();
        Variants.Clear();
        VariantError = string.Empty;
        this.RaisePropertyChanged(nameof(HasVariants));
        this.RaisePropertyChanged(nameof(ShowVariantSection));

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
                this.RaisePropertyChanged(nameof(ShowVariantSection));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The add still proceeds (the resolver's default pick may work), but say WHY there are no
            // choices — the plugin's message is the only hint the user gets before starting.
            if (!cts.IsCancellationRequested && ParsedUrls is [var same] && same == url)
                VariantError = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // superseded by newer input / the safety valve — nothing to report
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
                // Only prefill a name that looks like a real file. A page URL's last path segment
                // ("watch" from youtube.com/watch?v=…) has no extension and is junk — leave the box
                // empty so the resolver/engine names the download properly at start.
                if (!_userTypedName && !string.IsNullOrWhiteSpace(info.Value.FileName)
                    && System.IO.Path.HasExtension(info.Value.FileName))
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
            return ApplyApiRequest(variantItems);
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

        return ApplyApiRequest(items);
    }

    /// <summary>Carries a confirm-mode programmatic add's context onto the items the user just confirmed, so
    /// a confirmed download is byte-for-byte the download a silent add would have created.</summary>
    private List<DownloadItem> ApplyApiRequest(List<DownloadItem> items)
    {
        if (_apiRequest == null)
            return items;

        // Mirrors and a caller-picked variant only make sense for the link the request was ABOUT. If the
        // user retyped the URL (or split it into several), they are addressing a different file and
        // inheriting the old link's fallbacks or quality id would be wrong.
        var unchanged = items.Count == 1 &&
                        string.Equals(items[0].Url?.Trim(), _apiRequest.Url?.Trim(), StringComparison.Ordinal);
        if (unchanged)
        {
            foreach (var mirror in _apiRequest.Mirrors.Where(m => !string.IsNullOrWhiteSpace(m)))
                items[0].Urls.Add(mirror.Trim());
            // A variant the user picked in this dialog wins over the one the caller asked for.
            if (items[0].VariantId == null && !string.IsNullOrWhiteSpace(_apiRequest.VariantId))
                items[0].VariantId = _apiRequest.VariantId.Trim();
        }

        foreach (var item in items)
        {
            item.FromBrowserDownload = _apiRequest.FromBrowser;
            LocalApiService.ApplyRequestContext(item, _apiRequest);
        }
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
