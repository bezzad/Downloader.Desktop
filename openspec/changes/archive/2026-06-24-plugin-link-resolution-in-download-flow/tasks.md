# Tasks: plugin-link-resolution-in-download-flow

## 1. Consume resolvers in the download flow
- [x] 1.1 Add optional `PluginManager` ctor to `DownloadManager` (keep the parameterless one for tests).
- [x] 1.2 Add `public ResolveViaPluginsAsync(url, fileName, ct)` — resolve via the first matching enabled plugin; pass through on no-match / no-manager / error; first part only (log multi-part).
- [x] 1.3 Call it at the top of `Start`'s off-thread work, before `UrlResolver.ResolveAsync`.

## 2. Tests
- [x] 2.1 App test: manager rewrites a claimed link to the asset URL + suggested name; preserves typed name; passes through unclaimed links; no-op without a plugin manager.
- [x] 2.2 Plugin test: `CanResolve` gating (owner/repo on github.com only).
- [x] 2.3 Plugin test (gated `DLDESKTOP_NET=1`): the real `bezzad/Downloader.Desktop` repo resolves to a release asset URL + name.

## 3. Verify
- [x] 3.1 `dotnet test` green in Debug and Release (145).
- [x] 3.2 Ran the gated live test locally — real repo resolves to an asset.
- [x] 3.3 Cache the "plugin boundary now has a consumer" note in the skill; commit + push to `develop`.
