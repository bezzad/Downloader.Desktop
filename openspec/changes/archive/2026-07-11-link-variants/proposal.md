# Proposal: link-variants

## Why

One pasted link often stands for **several downloadable things**, and today the resolver silently picks one:

- A YouTube/video page has many qualities (144p→1080p) plus audio-only; the HLS plugin auto-picks the best.
- An Ollama model name without a tag (`gemma3`) has many sub-models (`gemma3:4b`, `gemma3:12b`, …); the plugin currently requires a fully-tagged input and can't offer the list.
- A GitHub repo release has multiple assets; the plugin picks the "best match".

The author wants the user to choose — but as a **generic plugin-SDK mechanism**, not a YouTube-only feature: any resolver can offer variants, the Add window shows them, and the user multi-selects which to download (decisions settled 2026-07-11: multi-select; the Download button waits for the variant list on variant-capable links; audio-only included for video).

## What Changes

- **Plugin SDK** (`Downloader.Desktop.Plugins.Abstractions`): `ILinkResolver` gains a default-implemented `GetVariantsAsync(url, options, ct)` returning `IReadOnlyList<LinkVariant>?` (`null`/empty = no choices — existing plugins unchanged), and `ResolveOptions` gains `VariantId` so the host resolves exactly the chosen variant.
- **Host / PluginManager**: expose variant lookup for a URL (enabled resolvers only), mirroring `FindResolver`.
- **Add window**: for a single pasted URL claimed by a variant-capable resolver, fetch variants in the background ("Fetching options…"), show a **multi-select checkbox list** (default variant pre-checked). The Download button is **disabled until the list arrives** for such links (author's decision). Each checked variant becomes its own download item. Multi-URL paste skips the picker.
- **DownloadManager / persistence**: `DownloadItem.VariantId` (persisted) flows into `ResolveOptions.VariantId` on every resolve, so Retry re-resolves the same variant.
- **HLS plugin**: variants = distinct video heights with approximate sizes + "Audio only" (m4a). One `yt-dlp -J` extraction serves both the listing and the subsequent resolve (per-URL cache — no double 15 s extraction). `SiteExtractor` honors the requested variant.
- **Ollama plugin**: a tag-less model reference lists the registry's tags (with sizes) as variants; a fully-tagged reference resolves directly as today.
- **i18n**: new Add-dialog keys in all 16 language packs.
- Plugin versions bumped (standing rule).

## Capabilities

### New Capabilities
- `link-variants`: a resolver can enumerate the selectable variants behind one link; the Add window lets the user multi-select them; each selection becomes an independent download resolved to exactly that variant, including across restarts/retries.

### Modified Capabilities
<!-- none — existing resolver behavior is unchanged when no variant is chosen or offered -->

## Impact

- `src/Downloader.Desktop.Plugins.Abstractions/Pipeline.cs` (SDK — additive, non-breaking via DIM, same pattern as the cookie hand-off)
- `src/Downloader.Desktop/Services/PluginManager.cs`, `Services/DownloadManager.cs` (+`.Plans.cs` resolve path), `Models/DownloadItem.cs`
- `src/Downloader.Desktop/ViewModels/AddDownloadItemViewModel.cs`, `Views/AddDownloadItemView.axaml` (picker UI)
- `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Hls/*` (SiteExtractor, HlsResolver, YtDlp JSON cache), `.../Downloader.Desktop.Plugins.Ollama/*` (registry tags list)
- `Assets/i18n/*.json` (16 packs), tests in `Downloader.Desktop.Tests` (SDK fakes, extractor variants, Add-VM flow)

---
**Released in v2.0.0** (2026-07-11): tag `v2.0.0`, GitHub Release with 6 binary assets + HLS plugin 1.3.0 zip + plugins-catalog.json; Snap Store latest/stable 2.0.0; Homebrew tap bumped (arm64 2409c9e9…, x64 7ec3dcee…); winget PR microsoft/winget-pkgs#400755 (awaiting moderator). Codecov badge fixed the same day (coverlet.collector added → 42%).
