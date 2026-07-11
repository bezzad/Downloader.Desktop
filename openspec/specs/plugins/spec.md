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

### Requirement: Optional plugins are discoverable from an in-app catalog

The application SHALL fetch a `plugins-catalog.json` manifest from the latest GitHub Release (the same
release-lookup used for the app's own update check) and SHALL list every catalog entry not already
installed in Settings → Plugins, visually de-emphasized relative to installed plugins, with an **Add**
action. The catalog fetch failing (offline, rate-limited, no release found) SHALL NOT block or break the
Plugins page — it SHALL simply show no catalog entries.

#### Scenario: Catalog plugin appears before install
- **WHEN** the catalog lists a plugin id not present in the local plugins folder
- **THEN** it appears in Settings → Plugins in a de-emphasized state with an Add action and no
  Disable/Remove actions

#### Scenario: Catalog unavailable degrades gracefully
- **WHEN** the catalog fetch fails for any reason
- **THEN** Settings → Plugins still renders installed (built-in and user-installed) plugins normally, with
  no catalog section shown

### Requirement: Installing a catalog plugin verifies its integrity before loading

Clicking **Add** on a catalog entry SHALL download that entry's asset, compute its sha256, and compare it
to the catalog entry's `sha256` **before** extracting or loading any file from it. On a match, the
extracted plugin SHALL be placed in the user plugins folder and loaded through the existing plugin loader,
after which it behaves as a normal user-installed (removable, disableable, non-built-in) plugin. On a
mismatch, the application SHALL discard the download, leave the plugins folder untouched, and show a clear,
retryable error — it SHALL NOT load or extract the unverified content.

#### Scenario: Successful install
- **WHEN** the user clicks Add on a catalog plugin and the downloaded asset's sha256 matches the catalog
  entry
- **THEN** the plugin is extracted into the user plugins folder, loaded, and appears with Disable/Remove
  actions like any other user-installed plugin

#### Scenario: Checksum mismatch blocks install
- **WHEN** the downloaded asset's sha256 does not match the catalog entry's `sha256`
- **THEN** the application does not extract or load any file from the download
- **AND** the user sees a friendly, retryable error
- **AND** the plugins folder is unchanged

### Requirement: Installed catalog plugins are checked for updates and only updated with consent

For each installed plugin whose id also appears in the fetched catalog, the application SHALL compare the
installed `PluginDescriptor.Version` to the catalog's version for that id. When the catalog version is
newer, the application SHALL surface a notification offering the update and SHALL NOT download, verify, or
replace the plugin's files until the user explicitly accepts. On acceptance, the same download-and-verify
gate as install (see "Installing a catalog plugin verifies its integrity before loading") SHALL apply
before the existing plugin files are unloaded and replaced.

#### Scenario: Update offered, not applied automatically
- **WHEN** an installed catalog plugin's version is older than the catalog's version for that id
- **THEN** the user is notified an update is available
- **AND** no files are downloaded or replaced until the user accepts

#### Scenario: Accepted update swaps the plugin
- **WHEN** the user accepts an offered plugin update and the downloaded asset passes checksum verification
- **THEN** the old plugin is unloaded, its files are replaced with the new version's, and it is reloaded

#### Scenario: Declined or ignored update makes no changes
- **WHEN** the user does not accept an offered plugin update
- **THEN** the currently installed version continues to run unchanged

### Requirement: Fallback resolvers never shadow specific resolvers
The plugin SDK SHALL let an `ILinkResolver` declare itself a fallback via an `IsFallback` property (default-implemented to `false`, so existing and external plugins keep working unchanged). When selecting the resolver for a URL, the host SHALL consult non-fallback resolvers first and consider fallback resolvers only when no non-fallback resolver claims the URL. The same ordering SHALL apply when recording the resolving plugin id for a download.

#### Scenario: Specific plugin wins over a fallback
- **WHEN** a GitHub repository URL is claimed by both the GitHub resolver and a fallback resolver that claims generic web pages
- **THEN** the GitHub resolver performs the resolution

#### Scenario: Fallback handles otherwise-unclaimed pages
- **WHEN** a generic article URL is claimed only by a fallback resolver
- **THEN** that fallback resolver performs the resolution

### Requirement: The host runs plugin-provided transfers end-to-end
When an enabled plugin's `ITransferProvider` claims a download item's URL, the application SHALL run that download through the plugin's `ITransfer` instead of the core HTTP engine: the transfer's progress events drive the row's live progress/speed through the standard staging pipeline, Pause/Resume route to the transfer, Cancel stops it, the item obeys its queue's concurrency cap, and the path returned by the completed transfer becomes the item's final file (marked Completed with its on-disk size). A transfer failure SHALL mark the item Failed with the error message. Transfer selection SHALL happen before link resolution so a claimed scheme never round-trips through resolvers.

#### Scenario: Transfer-backed item completes
- **WHEN** a transfer provider claims an item's URL and its transfer finishes returning a file path
- **THEN** the row shows live progress while running and ends Completed pointing at that file

#### Scenario: Transfer honors pause, resume, and cancel
- **WHEN** the user pauses, resumes, then cancels a transfer-backed item
- **THEN** the transfer's Pause and Resume are invoked and cancel stops the transfer, leaving the row Stopped

#### Scenario: Transfer failure is reported
- **WHEN** a running transfer throws an error
- **THEN** the item is marked Failed and the row shows the error message
