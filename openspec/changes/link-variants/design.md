# Design: link-variants

## Context

The resolve pipeline is: Add window → `DownloadManager.Start` → `ResolvePlanAsync(url, ct, cookieFilePath)` → `PluginManager.FindResolver(url)` → `ILinkResolver.ResolveAsync(url, ResolveOptions, ct)` → `DownloadPlan`. The resolver auto-picks one outcome per URL. The Add window (`AddDownloadItemViewModel`) already does a debounced background pre-resolve of name/size for a single URL, and `IsMultiple` gates single-URL-only UI. The SDK already has a proven non-breaking extension pattern: default-implemented interface overloads + `ResolveOptions` (used for the cookie hand-off).

Decisions settled with the author (2026-07-11): multi-select; Download disabled until the variant list loads on variant-capable links; include Audio-only for video; generic SDK mechanism, not HLS-specific.

## Goals / Non-Goals

**Goals:**
- Any resolver can offer selectable variants; the app UI and persistence are plugin-agnostic.
- Zero behavior change for links whose resolver offers no variants, for multi-URL adds, and for external plugins compiled against the old SDK.
- One heavy extraction per URL (list + resolve share it).

**Non-Goals:**
- No per-variant settings/preferences (e.g. "always 720p") — a follow-up.
- No variant UI outside the Add window (API/CLI adds keep the default pick; `VariantId` is accepted in the API JSON but optional).
- No GitHub-plugin variant implementation in this change (mechanism supports it later).

## Decisions

- **SDK shape**: `LinkVariant { Id, Label, Description?, ExpectedSize?, IsDefault }` POCO + `ILinkResolver.GetVariantsAsync(string url, ResolveOptions?, CancellationToken)` default-implemented to `null`; `ResolveOptions.VariantId` (string, init-only). Ids are resolver-defined stable strings ("1080", "audio", "gemma3:12b").
- **Host lookup**: `PluginManager.GetVariantsAsync(url, ct)` — finds the enabled claiming resolver, calls its `GetVariantsAsync`, returns null on none/error (logged). The Add VM consumes it through an injectable delegate (same seam style as `resolveFileInfo`) so tests need no PluginManager.
- **Add window**: single-URL debounce triggers variant lookup alongside the existing name/size resolve. VM state: `Variants` (ObservableCollection of `VariantChoiceViewModel { Id, Label, IsChecked }`), `IsFetchingVariants`, `HasVariants`. `CanDownload` = existing rule AND NOT (`IsFetchingVariants` on a claimed URL). Confirm builds one `DownloadItem` per checked variant (name suffixed by label when >1 so files don't collide); none checked = default (no VariantId). Editing the URL cancels/clears the pending lookup (per-keystroke CTS, same pattern as `TriggerResolve`).
- **Persistence/flow**: `DownloadItem.VariantId` (nullable string, JSON-persisted) → `DownloadManager.ResolvePlanAsync` copies it into `ResolveOptions`. Retry already clears `PlanJson` and re-resolves — it picks the persisted id up automatically.
- **HLS**: `YtDlpBinary` result cached per URL (small time-bounded cache, e.g. 5 min / last N URLs) keyed on (url, cookie presence). `SiteExtractor` gains `ListVariants(json)` (distinct heights desc + audio-only, sizes from format filesizes) and `Select(json, variantId)` — `"audio"` → best audio-only progressive part; `"<height>"` → best format at that height using the existing progressive/HLS/mux preference order capped/pinned to that height. Null variantId keeps today's selection exactly.
- **Ollama**: `OllamaModelRef.TryParse` currently requires a tag — extend to accept tag-less input for listing; `GetVariantsAsync` calls the registry tags endpoint (`/v2/<ns>/<model>/tags/list`) and returns tags (+ manifest sizes where cheap); `ResolveAsync` with `VariantId` = resolve `<model>:<variant>`. Fully-tagged input returns null variants (direct resolve).
- **i18n**: keys `Add_FetchingOptions`, `Add_ChooseVariant` (+ any picker text) in all 16 packs.
- **Versions**: HLS → 1.3.0, Ollama → minor bump (standing rule). SDK csproj version bump if it carries one.

## Risks / Trade-offs

- **Waiting on yt-dlp (~5–20 s) before Download enables** (author's explicit choice): mitigated by the spinner and by the failure fallback (lookup error/none → button enables, default pick).
- **Cache staleness**: yt-dlp signed URLs expire — the cache is short-lived and only bridges list→resolve within one Add flow; Retry re-extracts fresh.
- **Name collisions on multi-variant adds**: suffix the variant label into the filename when more than one variant of the same URL is added.
- **External plugins**: purely additive SDK (DIM + new POCO) — old plugins load and behave unchanged; verified by existing PluginTests fakes.
