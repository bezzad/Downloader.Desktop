## MODIFIED Requirements

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
