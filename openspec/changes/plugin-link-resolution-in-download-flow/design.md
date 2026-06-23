# Design: plugin-link-resolution-in-download-flow

## Where the hook goes

`DownloadManager.Start` already runs its setup off the UI thread inside `Task.Run`. The plugin resolution is
the first step there, before `UrlResolver.ResolveAsync` (HTTP-redirect following) and the engine call:

    (urls[0], fileName) = await ResolveViaPluginsAsync(urls[0], fileName, default);

`ResolveViaPluginsAsync` is `public` (matches the existing `*ForTest` seam convention — no InternalsVisibleTo
in this repo) so it is unit-testable directly without doing a real network download.

## Why optional ctor injection

`DownloadManager` keeps its parameterless ctor (used by every existing test) and adds
`DownloadManager(PluginManager)`. MS DI picks the greediest resolvable ctor, and `PluginManager` is already a
registered singleton, so the app gets the plugin-aware manager automatically; tests opt in by passing a
`PluginManager` they populated with a fake plugin. The singleton is the same instance `MainViewModel` loads
plugins into at startup, so resolvers are present by the time any download starts.

## Scope limit (first part only)

A resolver may return a multi-part plan with a post-process recipe (HLS video+audio → mux, torrent transfer).
Assembling those needs the not-yet-built job coordinator. Until then `ResolveViaPluginsAsync` downloads only
`Parts[0]` and logs that it did so. The GitHub Releases case is a single `Combined` part, so it is fully
handled. This keeps the change minimal and avoids half-implementing muxing.
