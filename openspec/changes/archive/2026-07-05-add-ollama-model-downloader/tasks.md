## 1. Restructure bundled plugins (in-repo home)

- [x] 1.1 `samples/Downloader.Desktop.SamplePlugin` → `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.GitHub`
  (same id `com.bezzad.github-releases`; namespace `Downloader.Desktop.Plugins.GitHub`, file
  `GitHubReleasesPlugin.cs`). Solution updated (sample removed; GitHub + Ollama projects added);
  `samples/` gone; docs paths updated (`docs/writing-plugins.md`, CLAUDE.md layout).
- [x] 1.2 App csproj `StageBundledPlugins`/`…OnPublish` targets copy each bundled plugin's DLL +
  `.deps.json` into `$(OutDir)/plugins` and `$(PublishDir)/plugins` (RID-agnostic recursive glob per the
  CI staging gotcha; loud `<Error>` if nothing found). Publish tarballs/macOS bundle pick the folder up
  automatically (they package the publish dir). Test csproj staging retargeted to the new GitHub path.

## 2. Built-in plugin loading (host)

- [x] 2.1 `PluginManager`: `BuiltInPluginsRoot` (= app dir `/plugins`), `LoadBuiltIns()`,
  `LoadFromDirectory(dir, isBuiltIn)`, `PluginDescriptor.IsBuiltIn`; `RemovePlugin` refuses built-ins.
  `%AppData%/Downloader/plugins` stays the removable user-plugin home.
- [x] 2.2 Per-plugin enabled state: the existing persisted `Config.DisabledPlugins` list (id present =
  disabled, default enabled) covers built-ins too — `MainViewModel` applies it after `LoadBuiltIns()`.
  No new field needed.
- [x] 2.3 Settings → Plugins UI: `PluginRowViewModel.IsBuiltIn`/`CanRemove`; the trash button is hidden
  for built-ins (toggle stays).

## 3. SDK: post-download action (additive)

- [x] 3.1 `IPostDownloadAction` (`Label`, `CanOffer(sourceUrl, filePath)`, `ExecuteAsync`) in
  `Abstractions/PostDownload.cs` + `IPluginContext.RegisterPostDownloadAction` as a **default interface
  method** (no-op) so existing hosts/fakes keep compiling — genuinely additive.
- [x] 3.2 `DownloadItem.ResolverPluginId` (persisted), recorded in `Start` via
  `PluginManager.FindResolverPluginId` when a resolver claims the link.
- [x] 3.3 Host surfacing: on completion (engine + plan paths) `OfferPostDownloadAction` fires an
  actionable notification (`NotificationService.ShowAction`); the row gets a button + context-menu item
  (`PostActionLabel`/`HasPostAction`/`PostActionCommand`, refreshed on status change and after plugins
  load). `RunPostDownloadAction` runs off-thread; failures land as the item's friendly error + an error
  toast; the download stays Completed and the file is never modified.

## 4. Ollama plugin (`Downloader.Desktop.Plugins.Ollama`, id `com.bezzad.ollama-models`)

- [x] 4.1 `OllamaModelRef.TryParse`: `ollama.com/library/<model>[:tag]` URLs (+ community
  `/<user>/<model>`), bare `name[:tag]` / `user/name[:tag]` (`:latest` default, `library` namespace
  default); rejects other URLs, paths, multi-segment inputs and file-extension-looking tokens
  (`video.mp4`) so normal adds are never hijacked. Unit-test matrix in `OllamaLogicTests`.
- [x] 4.2 `IOllamaRegistry` (stub-able) + `HttpOllamaRegistry` (base URL injectable; default
  `registry.ollama.ai`): manifest fetch/parse (`OllamaManifest`, picks the
  `application/vnd.ollama.image.model` layer), blob URLs, clear not-found/unreachable errors.
- [x] 4.3 `OllamaResolver.ResolveAsync`: manifest → single-part plan (model blob URL + `ExpectedSize`)
  + suggested `model-tag.gguf` name. The manifest is re-fetched by the installer (no staleness risk —
  blobs are content-addressed).
- [x] 4.4 `AddToOllamaAction` + `OllamaInstaller`: streamed sha256 verify against the manifest digest,
  metadata layers fetched into `{store}/blobs` (skip existing), model blob **hard-link-or-copy**
  (P/Invoke `link()`/`CreateHardLink`, copy fallback), manifest written **last** (temp+move) at
  `manifests/registry.ollama.ai/{ns}/{model}/{tag}`; store root `$OLLAMA_MODELS` else
  `~/.ollama/models` (missing `~/.ollama` → "Install Ollama first" error); the downloaded file is never
  modified.

## 5. Add flow: non-URL input

- [x] 5.1 Bare names already flow end-to-end: the Add dialog accepts any token, and `Start` →
  `ResolvePlanAsync` lets an enabled resolver claim it (unclaimed → the engine fails it like today).
  Covered by `Bare_model_name_is_claimed_by_an_enabled_resolver_and_rejected_when_disabled`.

## 6. Tests (15 new; full suite 211/211 green)

- [x] 6.1 Claim matrix + manifest parsing (`OllamaLogicTests`, canned manifest JSON).
- [x] 6.2 Resolve against a loopback fake registry (blob URL/size/name; 404 → clear error)
  (`OllamaIntegrationTests`).
- [x] 6.3 Installer into a temp store: happy path (blob layout incl. `sha256-*` names, manifest content,
  original file intact) + digest mismatch → no manifest written. (The "no ~/.ollama at the DEFAULT
  root" error path can't be simulated safely on a machine that has one — logic reviewed instead.)
- [x] 6.4 Built-in loading: flagged `IsBuiltIn`, `RemovePlugin` refuses built-ins but still removes user
  plugins; disable still works (persisted via the existing `DisabledPlugins` round-trip).
- [x] 6.5 Post-download action: offered only by the resolving plugin for matching input; not offered
  when disabled/wrong-plugin/no-id; not run without click; failure surfaces the friendly item error and
  the row stays Completed.
- [x] 6.6 Add-flow: bare name claimed when the resolver is enabled, unclaimed when disabled.

## 7. Docs & wrap-up

- [x] 7.1 CLAUDE.md layout (Abstractions + new `Downloader.Desktop.Plugins/` home, `samples/` removal);
  `docs/writing-plugins.md` (paths + `IPostDownloadAction` row).
- [x] 7.1b README feature bullet: "Download Ollama models" (paste a link or type `gemma3:12b` → download
  → one-click Add to Ollama) + built-in plugins (GitHub Releases + Ollama Models).
- [ ] 7.2 Settings/Plugins screenshots: **carried over to the next Linux session** (macOS captures must
  not be committed per SKILL.md). Archived with this box open by the author's decision — the reminder
  also lives in the SKILL screenshot routine.
- [x] 7.4 **(reprocess — author feedback)** The completion toast's button said a bare "Open", which read
  as open/unzip the file. `NotificationService.ShowAction` now takes an `actionText`; the offer passes the
  action's own label so the button literally says **"Add to Ollama"**, and the message explains it:
  "<file> finished downloading — click to run 'Add to Ollama'." (`PostAction_OfferMsg`, all 16 packs).
- [x] 7.3 **Author-verified (2026-07-05):** the `gemma3:1b` flow works end-to-end (download → the
  clarified "Add to Ollama" toast button → installed into the local store). Archived on the author's
  explicit `/opsx:archive`.
