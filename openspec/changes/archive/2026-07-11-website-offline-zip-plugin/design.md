# Design — website-offline-zip-plugin

## Context

The reference tool (github.com/AhmadIbrahiim/Website-downloader) is a thin wrapper over `wget --mirror --page-requisites --convert-links --adjust-extension --no-parent` plus a zip step. We reproduce that natively in .NET (no external binaries — the app's "no dependencies" promise) as a catalog plugin.

Current plugin pipeline: `ILinkResolver` → static `DownloadPlan` parts → engine download → `IPostProcessor`. A recursive crawl discovers URLs *while downloading*, so it cannot be expressed as a static parts plan. The SDK already defines the right abstraction — `ITransferProvider`/`ITransfer` ("a self-managed transfer that owns the whole download") — but no app code consumes it yet (Phase-2 gap noted in SKILL.md).

Variant plumbing today: `PluginManager.GetVariantsAsync` consults only the FIRST resolver whose `CanResolve(url)` is true; `FindResolver` is first-match over plugin load order. A generic "any HTML page" resolver would shadow GitHub/HLS/Ollama.

Author decisions (locked): recursive same-site crawl; trigger via the Add-dialog variant picker; optional/catalog tier.

## Goals / Non-Goals

**Goals:**
- One-click "Offline copy (.zip)" for any `text/html` URL from the Add dialog.
- Recursive same-host page crawl with sane caps; page requisites from any host; CSS-referenced assets included; links rewritten so the zip content browses offline.
- Wire the host's transfer path (`ITransferProvider`) properly: progress, pause/resume/cancel, queue cap, terminal states — reusable by future plugins (torrent).
- Keep specific resolvers (GitHub/HLS/Ollama) always winning over the generic one.

**Non-Goals:**
- No JavaScript rendering (static fetch only — SPA content that needs a browser engine won't be captured; same as wget).
- No login/cookie handling in v1 (public pages).
- No resume of a partially-crawled site across app restarts (retry restarts the crawl).
- No cross-host page recursion (only requisites cross hosts).

## Decisions

1. **Crawl runs as `ITransfer`, not as a resolver plan.** Dynamic discovery ⇒ the plan-parts model doesn't fit. `DownloadManager.Start` gains a transfer branch: if `PluginManager.FindTransferProvider(url)` claims the item's URL, the manager creates the transfer, wires `ProgressChanged` → the existing `StageProgress`/UI-pump path, and awaits `StartAsync(ct)`; the returned path becomes the completed file. Pause/Resume route to `ITransfer.Pause()/Resume()` when a transfer is active (new `DownloadItemViewModel.ActiveTransfer` handle, transient); Cancel cancels the per-item CTS. `Start` still sets `Status=Running` synchronously, so `PumpQueue`'s cap accounting is unchanged.

2. **`websitezip:` scheme + `SubstituteUrl` variant.** The zip variant substitutes the item URL to `websitezip:<original-url>`. That makes routing unambiguous forever (retries, restarts): `WebsiteTransferProvider.CanHandle` = "starts with `websitezip:`" — zero risk of hijacking normal http downloads. The Add dialog needs no new UI; the variant picker already exists.

3. **Fallback resolver (`ILinkResolver.IsFallback`, DIM ⇒ non-breaking).** To get the variant *offered*, a resolver must claim plain http(s) URLs. The plugin's resolver claims URLs whose path looks page-like (no extension or .html/.htm/.php/.asp/.aspx/.jsp) but marks itself `IsFallback`. `PluginManager.FindResolver`/`FindResolverPluginId` become two-pass (non-fallback first), so specific plugins keep winning resolution. `GetVariantsAsync` merges variants from ALL claiming resolvers (non-fallback first, first default wins) so e.g. a YouTube URL can show HLS qualities *and* the offline-zip option. The fallback resolver's default `ResolveAsync` is a pass-through single-part plan (no behavior change for users who pick the normal download).
   - *Alternative considered*: content-type sniffing inside `CanResolve` — rejected, `CanResolve` must stay cheap/sync. The plugin's `GetVariantsAsync` does the cheap HEAD (5 s timeout, `text/html` check) and returns null otherwise, so binary URLs never show the variant.

4. **Crawler is pure .NET, regex-based parsing.** `HttpClient` + regex extraction of `src`/`href`/`srcset`/`poster`/`<link rel=stylesheet>`/`<script src>`/inline `style url(...)`; CSS files re-parsed for `url(...)`/`@import`. No HtmlAgilityPack (no new deps; wget itself is regex-grade here). BFS: same-host HTML pages recurse (default caps: depth 3, 200 pages); requisites from any host download once (cap 2000 assets). Local layout mirrors `<host>/<path>` under the zip root, default doc `index.html`, query strings hashed into the filename, extensions adjusted from content-type when missing. All references rewritten to *relative* local paths from the containing file's directory. Zip via `System.IO.Compression.ZipFile`; output `<host>.zip`.

5. **Pause = gate between requests.** The crawler checks an async gate (`SemaphoreSlim`-style) before each fetch; `Pause()` closes it, `Resume()` opens it. Cancellation deletes the temp working directory.

6. **Progress model.** Total is unknown up front; `TransferProgress.Percentage` = done/(done+queued) items (monotonic clamp so it never goes backwards as discovery grows the queue), `BytesReceived` accumulates real bytes, speed = 3 s sliding window. The row shows normal live progress/speed via the existing staging pump.

7. **Catalog packaging = clone of the HLS pattern.** Project under `src/Downloader.Desktop.Plugins/`, in the solution for build/test only, never referenced/staged by the app; `scripts/build-plugins.sh` zips it + extends `plugins-catalog.json` from `packaging/plugins/optional-plugins.json`; `PluginIsolationTests` extended to assert the app never bundles it. csproj `<Version>1.0.0</Version>` is the single version source.

## Risks / Trade-offs

- [SPA/JS-heavy pages capture poorly] → Documented limitation (same as wget); the zip still contains the served HTML. Future: optional headless rendering.
- [Crawl explosion on big sites] → depth/page/asset caps with conservative defaults baked into the plugin; hitting a cap ends the crawl gracefully (zip contains what was fetched).
- [Fallback resolver claims every page-like URL → it becomes the recorded resolving plugin for generic downloads] → its pass-through resolve is byte-identical behavior; it offers no post-download actions; two-pass ordering keeps specific plugins first.
- [Transfer path is new host plumbing] → guarded by unit tests with an in-process fake `ITransferProvider` (no network) + loopback integration test through the real plugin; state-transition guards stay centralized in `DownloadManager`.
- [HEAD not supported by some servers] → fall back to a ranged GET for the content-type check; on any failure return no variants (never block the Add dialog).
- [Restart mid-crawl] → item reloads as Stopped; Retry restarts the crawl from scratch (documented; parts-reuse is out of scope).

## Open Questions

None — scope, trigger UX, and tier were settled with the author before this change was opened.
