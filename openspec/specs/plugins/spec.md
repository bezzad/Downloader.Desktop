# plugins Specification

## Purpose

Loading expectations for the bundled sample plugin against the current plugin SDK.
## Requirements
### Requirement: The download flow resolves links through enabled plugins
Before downloading, the application SHALL offer the pasted link to the enabled plugins' link resolvers;
when a resolver claims the link, the application SHALL execute the resolver's **entire** `DownloadPlan`:
every part is downloaded through the engine (honoring each part's request headers), a plan with a
post-process step is assembled into one final file by the matching plugin post-processor, and the
resolver's suggested file name is used when the user did not provide one. A resolver failure SHALL NOT
break the download — the original link is used as-is.

#### Scenario: A multi-part plan produces one final file
- **WHEN** a resolver returns a plan with multiple parts and a post-process recipe (e.g. HLS segments +
  Concat)
- **THEN** all parts are downloaded to a temporary parts location
- **AND** the plugin post-processor assembles them into the final file in the user's save folder
- **AND** the temporary parts are removed after success

#### Scenario: Single-part plain plans behave as before
- **WHEN** a resolver returns one part with no post-process
- **THEN** the download behaves exactly like a normal engine download of that URL (no parts folder)

#### Scenario: Part headers are honored
- **WHEN** a plan part carries request headers (cookies/referer)
- **THEN** the engine sends those headers when downloading that part

#### Scenario: Missing post-processor fails clearly
- **WHEN** all parts finish but no enabled plugin can process the plan's post-process step
- **THEN** the item is marked Failed with a message naming the missing processing capability

### Requirement: A plugin can be removed
User-installed plugins (loaded from the user plugins folder) SHALL be removable from Settings → Plugins,
unloading them and deleting their files. Built-in plugins SHALL NOT be removable (see "Built-in plugins
ship with the app").

#### Scenario: Removing a user-installed plugin deletes it
- **WHEN** the user removes a user-installed plugin
- **THEN** it disappears from the list and its DLL is deleted from the user plugins folder

#### Scenario: Built-ins offer no removal
- **WHEN** the user views a built-in plugin
- **THEN** no Remove action is available

### Requirement: Multi-part downloads report one aggregate progress and obey controls

A running plan SHALL show a single aggregate progress on the row (byte-weighted when part sizes are
known, otherwise completed-parts of total, with a reserved tail while assembling) and SHALL respond to
the standard controls: pause stops at the current part and resume continues from it; cancel stops and
removes the temporary parts; the plan run SHALL occupy one queue slot like any other download.

#### Scenario: Pause and resume mid-plan
- **WHEN** the user pauses a running multi-part download and later resumes it
- **THEN** completed parts are not re-downloaded and the run continues from where it stopped

#### Scenario: Status reflects the phase
- **WHEN** a multi-part plan is downloading or assembling
- **THEN** the row's status text distinguishes part progress (e.g. current part of total) from the
  assembling phase

### Requirement: Multi-part plans survive an app restart

The resolved plan SHALL be persisted with the download item; after an app restart, resuming the item SHALL
continue from the first incomplete part instead of restarting the whole plan or falling back to the
original link.

#### Scenario: Restart resumes the plan
- **WHEN** the app is closed while a multi-part download is paused partway and then reopened
- **THEN** resuming downloads only the remaining parts and assembles normally

#### Scenario: Retry re-resolves a stale plan
- **WHEN** a multi-part download failed (e.g. expired segment URLs) and the user retries
- **THEN** the original link is re-resolved and the download proceeds with the fresh plan

### Requirement: Plan execution downloads segment parts efficiently
When executing a resolved multi-part plan, the app SHALL download parts marked as segments (or parts
smaller than a small-part threshold) with a single connection/chunk instead of the user's full
multipart configuration. Large non-segment parts keep the normal multipart behavior.

#### Scenario: HLS segments are single-chunk
- **WHEN** a plan with `PartKind.Segment` parts (e.g. an HLS stream's `.ts` segments) runs
- **THEN** each segment downloads with one chunk / one connection
- **AND** the per-segment overhead of multipart chunking (multiple range requests per tiny file) does not occur

### Requirement: Assembled output carries a standard media extension
The plan runner SHALL hand post-processors a temporary output path whose file extension is a standard
media extension (extension last), and SHALL normalize a playlist-derived final name (`.m3u8`/`.m3u`)
to a media container extension when the plan includes a post-process step.

#### Scenario: ffmpeg can choose a muxer for the temp output
- **WHEN** a Mux/Concat post-processor (ffmpeg-based) receives the temp output path
- **THEN** the path ends in a standard media extension (e.g. `video.assembling.mp4`), never in a bare `.assembling`

#### Scenario: Playlist name becomes a media name
- **WHEN** the chosen final name ends in `.m3u8`/`.m3u` and the plan has a post-process step
- **THEN** the assembled file is saved with a `.mp4` extension (or the plugin's suggested media extension)

### Requirement: Built-in plugins ship with the app

The application SHALL bundle first-party plugins (GitHub Releases, Ollama Models) in its install
directory's `plugins/` folder and load them at startup as **built-in** plugins. Built-in plugins SHALL be
present and enabled after first install, SHALL be updated with the app, and SHALL NOT be removable —
Settings offers only an enable/disable toggle for them. Per-plugin enabled state SHALL persist in the app
config across restarts and app updates.

#### Scenario: Fresh install has the built-ins
- **WHEN** the app runs for the first time
- **THEN** GitHub Releases and Ollama Models appear in Settings → Plugins, enabled

#### Scenario: Built-ins cannot be removed, only disabled
- **WHEN** the user opens a built-in plugin's entry in Settings → Plugins
- **THEN** there is no Remove action, only an enable/disable toggle
- **AND** a disabled built-in stays disabled after restarting and after updating the app

#### Scenario: User-installed plugins keep removable behavior
- **WHEN** the user installs a plugin DLL into the user plugins folder (e.g. the HLS plugin)
- **THEN** it can still be removed from Settings → Plugins as before

### Requirement: A plugin can offer a post-download action

The plugin SDK SHALL let a plugin register a named post-download action (label + can-offer check +
execute). For a completed download that was resolved by that plugin, the host SHALL surface the action to
the user (on the completion notification and on the item), SHALL run it only when the user triggers it,
and SHALL show the action's failure message on error. Which plugin resolved a download SHALL be recorded
on the persisted item so offers survive restarts.

#### Scenario: Action appears only on that plugin's completed downloads
- **WHEN** a download resolved by a plugin with a registered post-download action completes
- **THEN** the action's label is offered on the completed item
- **AND** downloads not resolved by that plugin do not show the action

#### Scenario: Action runs only on user click
- **WHEN** the download completes and the user does nothing
- **THEN** the action is not executed

#### Scenario: Action failure is shown, download stays intact
- **WHEN** an executed action throws
- **THEN** its message is shown like other friendly item errors
- **AND** the downloaded file and item state are unchanged

