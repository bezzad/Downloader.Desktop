# Proposal: plugin-link-resolution-in-download-flow

## Why

The plugin SDK (`ILinkResolver`), the loader (`PluginManager`), and the sample **GitHub Releases** plugin all
exist and work, but **nothing in the app ever called a resolver**. `DownloadManager.Start` handed the pasted
link straight to the engine via `UrlResolver.ResolveAsync` (HTTP-redirect following only). So pasting a repo
link such as `https://github.com/bezzad/Downloader.Desktop` downloaded the **HTML page**, not the latest
release asset — the plugin's headline feature was dead because it had no consumer.

## What Changes

- `DownloadManager` takes an optional `PluginManager` and, at the start of every download, calls a new
  `ResolveViaPluginsAsync(url, fileName)`: if an **enabled** plugin's resolver `CanResolve` the link, the link
  is rewritten to the resolver's first real `DownloadPart.Url` and the `SuggestedFileName` is used (when the
  user didn't type one) before the engine runs.
- Non-claimed links, a missing plugin manager, and resolver errors all pass the link through unchanged (no
  regression for ordinary URLs).
- Multi-part / transfer / post-process plans (HLS, torrent) are out of scope here: only the first part is
  downloaded for now, and that is logged. Full assembly waits for the job coordinator.

## Impact

- Affected specs: `plugins` (new requirement: the download flow resolves links through enabled plugins).
- Affected code: `Services/DownloadManager.cs` (new ctor + `ResolveViaPluginsAsync`, called in `Start`).
  No SDK/contract change; DI already registers `PluginManager` as a singleton shared with the manager.
- Tests: app tests prove the manager consults the resolver and preserves non-claimed links / typed names /
  no-plugin case; plugin tests cover `CanResolve` gating and a **gated live-network** resolve of the real
  repo (`DLDESKTOP_NET=1`). 145 tests green (Debug + Release).
