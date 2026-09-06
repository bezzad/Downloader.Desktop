# ollama-model-download Specification

## Purpose
TBD - created by archiving change add-ollama-model-downloader. Update Purpose after archive.
## Requirements
### Requirement: Ollama model links and bare names are claimed

The Ollama plugin's resolver SHALL claim `https://ollama.com/library/<model>[:tag]` URLs and bare model
names matching a conservative `name[:tag]` pattern (lowercase letters/digits/`._-`, no spaces or slashes).
The check SHALL be fast and SHALL NOT perform network I/O. Input that is neither an ollama.com library URL
nor a bare-name match SHALL NOT be claimed.

#### Scenario: An ollama.com library URL is claimed
- **WHEN** `CanResolve` is called with `https://ollama.com/library/gemma3:12b`
- **THEN** it returns true

#### Scenario: A bare model name is claimed
- **WHEN** `CanResolve` is called with `gemma3:12b` (or `gemma3`, meaning `:latest`)
- **THEN** it returns true
- **AND** no network request was made

#### Scenario: Unrelated input is not claimed
- **WHEN** `CanResolve` is called with `https://example.com/file.zip` or `my report.pdf`
- **THEN** it returns false

### Requirement: A claimed model resolves to the real model blob

`ResolveAsync` SHALL fetch the model's registry manifest
(`registry.ollama.ai/v2/library/<model>/manifests/<tag>`), select the model-weights layer
(`application/vnd.ollama.image.model`), and resolve to that blob's download URL with the layer's known
size and a suggested file name derived from the model name and tag (e.g. `gemma3-12b.gguf`). The engine —
not the plugin — downloads the blob, into the user's normal save folder.

#### Scenario: A model resolves to a downloadable blob with known size
- **WHEN** `ResolveAsync` is called for a claimed model reference
- **THEN** the resolved asset URL is the manifest's model-layer blob URL
- **AND** the expected size equals the manifest layer's size
- **AND** the suggested file name contains the model name and tag

#### Scenario: An unknown model or tag fails clearly
- **WHEN** the registry returns not-found for the model or tag
- **THEN** `ResolveAsync` throws an error saying the model/tag was not found on ollama.com

### Requirement: A downloaded model can be added to local Ollama by explicit action

The plugin SHALL offer an "Add to Ollama" post-download action for downloads it resolved. Executing it
SHALL verify the file's sha256 against the manifest digest, fetch the manifest's small metadata layers,
place blobs (hard-link the model file when possible, else copy — never moving/deleting the user's
download) and finally the manifest into the local Ollama store (`OLLAMA_MODELS` env, else the per-OS
`~/.ollama/models`), such that the model appears in `ollama list` without any command or Ollama restart.

The offer SHALL appear whenever such a download completes — on the completed row and as a notification —
without the user having to search for it, and SHALL survive a restart of the app. It SHALL be offered
for every route by which a model download can complete, including a download the app resolved through a
multi-part plan, and it SHALL NOT depend on the resolved blob address, which is not the address the user
supplied.

#### Scenario: One click installs the model
- **WHEN** the user triggers "Add to Ollama" on a completed model download
- **THEN** the blobs and manifest exist in the local Ollama store with content-addressed names
- **AND** the user's downloaded file remains in their save folder
- **AND** `ollama list` shows the model

#### Scenario: The offer appears when the download finishes
- **WHEN** a model download the plugin resolved completes
- **THEN** the completed row shows the "Add to Ollama" action
- **AND** the user is notified that it is available

#### Scenario: The offer survives a restart
- **WHEN** the app is restarted with a completed model download in the list
- **THEN** that row still shows the "Add to Ollama" action

#### Scenario: A corrupted download never installs
- **WHEN** the downloaded file's sha256 does not match the manifest digest
- **THEN** the action fails with a clear message and writes no manifest into the store

#### Scenario: Missing Ollama store fails clearly
- **WHEN** no Ollama store directory can be found or created
- **THEN** the action fails with a message explaining Ollama was not found and where it looked

### Requirement: The plugin is built-in

The Ollama plugin SHALL ship with the application as a built-in plugin: present and enabled after first
install, listed in Settings → Plugins with an enable/disable toggle and no Remove option.

#### Scenario: Present on a fresh install
- **WHEN** the app runs for the first time
- **THEN** the Ollama plugin appears in Settings → Plugins, enabled

#### Scenario: Disabling stops claiming
- **WHEN** the user disables the Ollama plugin and pastes an ollama.com model link
- **THEN** the link is treated like any ordinary URL (no manifest resolution)

