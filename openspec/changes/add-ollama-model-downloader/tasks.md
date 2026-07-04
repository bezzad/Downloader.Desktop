## 1. Restructure bundled plugins (in-repo home)

- [ ] 1.1 Create `src/Downloader.Desktop.Plugins/` and move `samples/Downloader.Desktop.SamplePlugin` →
  `Downloader.Desktop.Plugins.GitHub` (same plugin id `com.bezzad.github-releases`; rename
  namespace/assembly; keep behavior). Remove `samples/` from the solution; update solution + docs paths.
- [ ] 1.2 Build outputs: both plugin projects copy their DLL + `.deps.json` into the app's
  `$(OutDir)/plugins/` on build, and publish workflows/scripts (`publish.sh`, CI/release yml, macOS bundle)
  include the `plugins/` folder in every artifact.

## 2. Built-in plugin loading (host)

- [ ] 2.1 `PluginManager`: load `<app dir>/plugins/` at startup as built-ins (`IsBuiltIn` on the
  descriptor); keep `%AppData%/Downloader/plugins` as removable user plugins. `RemovePlugin` refuses
  built-ins.
- [ ] 2.2 Persist per-plugin enabled state in `Config` (id → enabled, default true); apply on load; save on
  toggle.
- [ ] 2.3 Settings → Plugins UI: built-ins show enable/disable only (no Remove); user plugins unchanged.

## 3. SDK: post-download action (additive)

- [ ] 3.1 Add `IPostDownloadAction` (Label, `CanOffer(sourceUrl, filePath)`, `ExecuteAsync`) +
  `IPluginContext.RegisterPostDownloadAction` to Abstractions; no changes to existing contracts.
- [ ] 3.2 Record `ResolverPluginId` on `DownloadItem` when a resolver claims a link (persisted).
- [ ] 3.3 Host surfacing: on completion, offer the resolving plugin's applicable action as a button on the
  completion notification and on the item (context menu / details); run on click only; failures show as
  friendly item errors.

## 4. Ollama plugin (`Downloader.Desktop.Plugins.Ollama`, id `com.bezzad.ollama-models`)

- [ ] 4.1 Claim logic: `ollama.com/library/<model>[:tag]` URLs + bare `name[:tag]` pattern; no network;
  unit-test matrix (URLs, bare names, `:latest` default, rejects).
- [ ] 4.2 Registry client behind an interface (stub-able): fetch/parse manifest, pick the
  `…image.model` layer, build blob URLs; clear not-found/unreachable errors.
- [ ] 4.3 `ResolveAsync`: manifest → model blob URL + expected size + suggested `model-tag.gguf` name;
  stash the manifest (item metadata or re-fetch on action) for the installer.
- [ ] 4.4 "Add to Ollama" action: sha256 verify (streamed), fetch tiny metadata layers, hard-link-or-copy
  blobs, write manifest last; store root from `OLLAMA_MODELS` else per-OS `~/.ollama/models`; clear errors
  (mismatch, store missing); never touch the user's downloaded file.

## 5. Add flow: non-URL input

- [ ] 5.1 When input isn't an absolute URL, consult enabled resolvers; claimed → proceed via plugin,
  unclaimed → today's rejection. Multi-line input: apply per line.

## 6. Tests

- [ ] 6.1 Claim matrix + registry-client parsing tests (canned manifest JSON fixtures).
- [ ] 6.2 Resolve tests against a loopback fake registry (manifest → blob URL/size/name; 404 → clear error).
- [ ] 6.3 Installer tests into a temp store dir: happy path (blobs + manifest layout, hard-link/copy,
  original file intact), digest mismatch → no manifest written, missing store → clear error.
- [ ] 6.4 Built-in loading tests: app-dir plugins load flagged non-removable; enabled-state persists;
  `RemovePlugin` refuses built-ins but still removes user plugins.
- [ ] 6.5 Post-download action tests: offered only on the resolving plugin's completed items; not run
  without click; failure surfaces message.
- [ ] 6.6 Add-flow tests: bare name accepted when claimed, rejected when plugin disabled/unclaimed.

## 7. Docs & wrap-up

- [ ] 7.1 Update CLAUDE.md layout section + plugin docs (`docs/plugins-architecture.md`,
  `docs/writing-plugins.md`): built-in plugins home, post-download action contract, samples/ removal.
- [ ] 7.2 Refresh Settings/Plugins screenshots if the UI changed (standing routine).
- [ ] 7.3 Manual end-to-end check: type `gemma3:1b` (small!) → downloads → "Add to Ollama" → `ollama list`
  shows it; note the result here before archiving.
