## Why

Pulling a big LLM for Ollama today means a terminal command (`ollama pull gemma3:12b`) with a single-stream
download, no queueing, no scheduling, and a CLI non-technical users don't want. Ollama's registry is plain
HTTP (an OCI-style manifest + CDN blobs that honor Range requests), which is exactly what our multipart
engine is best at. Users should paste an ollama.com link — or just a model name — and get the model through
the app, then add it to their local Ollama with one click.

At the same time, the author wants **first-party plugins bundled inside the main repo/app** (not the
separate `Downloader.Plugins` repo): the existing GitHub-Releases sample becomes a real built-in plugin,
and the new Ollama plugin joins it. Built-ins ship with the app, can be disabled but not removed;
user-installed plugins (e.g. HLS) keep today's removable behavior.

## What Changes

- **Restructure bundled plugins**: move/rename `samples/Downloader.Desktop.SamplePlugin` into a new
  `src/Downloader.Desktop.Plugins/` home with one project per plugin:
  `Downloader.Desktop.Plugins.GitHub` (the GitHub Releases plugin, `com.bezzad.github-releases`) and the new
  `Downloader.Desktop.Plugins.Ollama`. Both build into the app's install-dir `plugins/` folder and ship in
  every publish.
- **Built-in plugin loading**: at startup the host loads `<app dir>/plugins/` as **built-in** plugins —
  structurally non-removable (Settings shows enable/disable only; no Remove button), with per-plugin
  enabled state persisted in `Config`. `%AppData%/Downloader/plugins` remains the home of user-installed,
  removable plugins.
- **New Ollama plugin** (`com.bezzad.ollama-models`): claims `https://ollama.com/library/<model>[:tag]`
  URLs **and bare model names** (`gemma3:12b`); resolves the registry manifest
  (`registry.ollama.ai/v2/library/<model>/manifests/<tag>`) into the real GGUF blob URL with known size;
  the engine downloads it to the user's normal save folder (multipart/pause/resume/queue all apply).
- **SDK addition — post-download action** (additive, no breaking change): a plugin can offer a named action
  for downloads it resolved; the host surfaces it on the completed row/notification. The Ollama plugin's
  action is **"Add to Ollama"**: verify sha256, fetch the manifest's small metadata layers, place
  blobs + manifest into the local Ollama store (`OLLAMA_MODELS` env or `~/.ollama/models`) — the model then
  appears in `ollama list` with no command typed. The app stays a *downloader*; installing is an explicit
  user choice.
- **Add-flow change**: non-URL input is accepted when an enabled plugin's resolver claims it (bare Ollama
  names); unclaimed non-URL input is rejected as today.

## Capabilities

### New Capabilities
- `ollama-model-download`: claim Ollama model links/names, resolve the registry manifest to a real
  downloadable blob, and offer an explicit "Add to Ollama" post-download action that installs the model
  into the local Ollama store.

### Modified Capabilities
- `plugins`: built-in (bundled, non-removable, disable-only) vs user-installed (removable) plugins;
  new SDK post-download action contract; the bundled-sample requirement is superseded by built-ins.
- `add-download`: non-URL input is accepted when a plugin resolver claims it.

## Impact

- **Repo**: `Downloader.Desktop` only (the separate `Downloader.Plugins` repo is NOT touched; HLS stays
  there as a user-installed plugin).
- **Projects**: new `src/Downloader.Desktop.Plugins/{GitHub,Ollama}` projects; `samples/` project removed
  from the solution; publish scripts/workflows bundle the plugins folder.
- **SDK**: one additive interface (post-download action) in `Downloader.Desktop.Plugins.Abstractions`;
  existing plugins remain source/binary compatible.
- **Host**: `PluginManager` built-in loading + non-removable descriptor flag; Settings Plugins UI
  (hide Remove for built-ins); Add flow non-URL claim path; completed-download action surfacing.
- **No Phase-2 dependency**: v1 resolves to a single blob URL (today's resolver flow already downloads a
  claimed link's resolved URL); the manifest's tiny metadata layers are fetched by the action at
  install-click time, not by the engine.
- **Network/privacy**: talks to ollama.com/registry.ollama.ai only when resolving/installing; no telemetry.
