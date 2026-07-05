## Context

Ollama distributes models over an OCI-registry-style HTTP protocol:

```
GET https://registry.ollama.ai/v2/library/<model>/manifests/<tag>   → JSON manifest
     layers: [ { mediaType, digest: sha256:…, size }, … ]
     - application/vnd.ollama.image.model     ← the GGUF weights (GBs; the payload)
     - …image.template / .params / .license   ← tiny metadata layers (bytes–KBs)
GET https://registry.ollama.ai/v2/library/<model>/blobs/sha256:<digest>
     → 302 to CDN; supports HTTP Range (multipart-friendly); anonymous for public models
Local store:
     ~/.ollama/models/blobs/sha256-<digest>                          (content-addressed)
     ~/.ollama/models/manifests/registry.ollama.ai/library/<model>/<tag>
     (OLLAMA_MODELS env overrides the root; a running Ollama picks up new
      manifests without restart — `ollama list` reads the store directly)
```

The host already routes any claimed link through enabled plugins' `ILinkResolver`s and downloads the
resolved URL with the resolver's suggested name (spec `plugins`). `PluginManager.LoadFromDirectory` loads
every `IDownloaderPlugin` in every DLL of a directory; `RemovePlugin` unloads + deletes the DLL file.

## Goals / Non-Goals

**Goals:**
- Paste `https://ollama.com/library/gemma3:12b` *or type `gemma3:12b`* → the model downloads through the
  engine (multipart, pause/resume, queue, speed cap) into the normal save folder.
- One explicit click ("Add to Ollama") installs the downloaded model into the local Ollama store; the model
  then shows in `ollama list`. No terminal, no `ollama pull`.
- GitHub Releases + Ollama plugins ship **built-in**: present after first install, disable-only.
- SDK stays additive; existing plugins (HLS) unaffected and still removable.

**Non-Goals:**
- Not an Ollama manager (no list/delete/run of models; no chatting). We download; installing is one action.
- No private/authenticated registries in v1 (public `library/` models only).
- No dependency on the Phase-2 multi-part pipeline (v1 is a single-blob plan).
- No changes to the separate `Downloader.Plugins` repo.

## Decisions

### D1: Downloader first, installer only on explicit action (author's call)
The engine downloads the **GGUF blob as a normal file** into the user's save folder. Placing files into
`~/.ollama` happens only when the user clicks the plugin's **"Add to Ollama"** action on the completed
download. If they never click, they still have a usable GGUF. *Rejected alternative:* post-processor
silently installing into the Ollama store — hidden magic, wrong identity for a download manager.

### D2: v1 plan = the single model blob (no Phase-2 dependency)
`ResolveAsync` fetches the manifest, picks the `…image.model` layer, and returns its blob URL as the
resolved asset (+ `SuggestedFileName` like `gemma3-12b.gguf`, + known size). Today's host flow can already
download one resolved URL. The tiny template/params/license layers are fetched by the **action** at
install time (KB-sized; not worth engine parts). When Phase-2 lands, the plan can grow real multi-part.

### D3: New SDK contract — post-download action (the author's "Alert on PostDownload")
Additive interface in Abstractions, registered like the other contributions:

```csharp
public interface IPostDownloadAction
{
    string Label { get; }                       // e.g. "Add to Ollama" (localizable by host key later)
    bool CanOffer(string sourceUrl, string filePath);   // cheap; no I/O beyond the local file
    Task ExecuteAsync(string sourceUrl, string filePath, CancellationToken ct);
}
```

`DownloadItem` records which plugin resolved it (`ResolverPluginId`), so the offer survives restarts and is
only shown for that plugin's downloads. Host surfaces the action as a button on the completion notification
and in the row's context menu / details. Errors from `ExecuteAsync` show like other friendly row errors.

### D4: "Add to Ollama" semantics
1. Re-fetch the manifest (or use one cached at resolve time in the item's metadata), verify the downloaded
   file's sha256 equals the model layer digest (stream hash — no full-file load).
2. Download the tiny metadata layers directly (HttpClient) into the blobs dir.
3. **Hard-link** the model file into `blobs/sha256-<digest>` when same volume; fall back to copy (the
   user keeps their downloaded file either way — we never move/delete their download).
4. Write the manifest file last (atomic finish). Store root = `OLLAMA_MODELS` env, else `~/.ollama/models`
   (`%USERPROFILE%\.ollama\models` on Windows). Clear error if the dir can't be found/created.

### D5: Bare model names in the Add flow
`CanResolve` already receives a raw string. The Add flow currently assumes URLs; change: if input isn't a
valid absolute URL, offer it to enabled resolvers — if one claims it, proceed with that resolver; otherwise
reject as today. The Ollama plugin claims bare names with a conservative pattern
(`^[a-z0-9][a-z0-9._-]*(:[a-z0-9._-]+)?$`, no spaces/slashes) plus `ollama.com/library/...` URLs. Names
without a tag resolve as `:latest`. No network in `CanResolve`.

### D6: Built-in vs user-installed plugins
Built-ins load from `<app dir>/plugins/` (shipped by the build; updated with the app), flagged
`IsBuiltIn` on the descriptor: Settings hides Remove and shows only the enable toggle. Per-plugin enabled
state persists in `Config` (id → bool; default enabled). User-installed plugins keep the existing
`%AppData%/Downloader/plugins` dir and Remove behavior. Naming note: assembly names
`Downloader.Desktop.Plugins.GitHub/.Ollama` do not collide with the SDK assembly
(`Downloader.Desktop.Plugins.Abstractions`) in the load-context exclusion check (exact name match).

## Risks / Trade-offs

- **Registry protocol drift** → it's the same protocol `ollama` itself uses; version the URL builder in one
  place; integration test hits only a stubbed loopback registry.
- **30 GB blobs** → known size up front lets the app show it before starting; hard-link install avoids
  doubling disk; document that copy fallback needs free space.
- **Digest mismatch** (corrupt/partial download) → action fails with a clear message; never writes a bad
  manifest (manifest written last).
- **Windows/macOS/Linux paths + running Ollama** → content-addressed blobs first + manifest last makes the
  install naturally atomic; no restart needed.
- **Bare-name false claims** (someone types a filename) → conservative pattern + only consulted when input
  is not a URL and not an existing path.

## Migration Plan

1. Move the sample plugin project into `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.GitHub`
   (same plugin id — user configs keep working), update solution/publish, delete `samples/`.
2. Add built-in loading + config enabled-map (additive; existing `%AppData%` plugins unaffected).
3. Add the SDK interface (additive), then the Ollama plugin, then the Add-flow non-URL path.
   Rollback = don't bundle the Ollama DLL; everything else is inert.

## Open Questions

- Should "Add to Ollama" offer a model-name override (install `gemma3:12b` under a custom local tag)?
  v1: no — keep the registry name.
- Later: Hugging Face GGUF URLs (`hf.co/...`) through the same action? The install mechanics are identical;
  parked until asked.
