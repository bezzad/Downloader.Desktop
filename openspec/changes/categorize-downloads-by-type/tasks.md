## 1. Engine: expose the content type

> Separate repo (`../Downloader`). Must reach NuGet before task 6. Everything in groups 2–5 is
> independent of it.

- [x] 1.1 Add a `ContentType` property to `RemoteFileInfo`, documented as the server's
      `Content-Type` header or null when the server did not send one
- [x] 1.2 Populate it in `SocketClient.GetFileInfoAsync` from the already-fetched `ResponseHeaders`
      dictionary using `HttpHeaderNames.ContentType` — no extra request
- [x] 1.3 Set it to null on the best-effort fallback path in `RemoteFileResolver.GetFileInfoAsync`
- [x] 1.4 Add engine tests: a server sending `Content-Type` surfaces it; a server not sending one
      yields null; the fallback path yields null without throwing
- [ ] 1.5 Release the engine package and note the version here

## 2. Category model and resolution

- [x] 2.1 Add a `DownloadCategory` model: identifier, name, icon key, color, extension list,
      position, built-in flag
- [x] 2.2 Add `Config.Categories` and bump `Config.SchemaVersion`; seed the eight built-in
      categories (Video, Audio, Image, Document, Archive, App, Disc, Other) with the exact extension
      sets from `DownloadItemViewModel.GetFileKind`, named in the app's current language
- [x] 2.3 Add a load-time migration: a configuration with no category list gets the built-ins; test
      that an existing config upgrades without losing settings, downloads, queues or schedules
- [x] 2.4 Add `DownloadItem.CategoryId` (nullable; null means detect) and persist it
- [x] 2.5 Add `DownloadItem.ContentType` to hold the MIME reported by the server or a client
- [x] 2.6 Add `Services/CategoryService`, registered in DI: holds the ordered list, resolves
      `Resolve(item)` as override → extension → content type → Other, and raises a change
      notification when the list changes
- [x] 2.7 Implement extension-conflict precedence: the earliest category in the order wins; test
      that reordering two categories claiming the same extension flips which one wins
- [x] 2.8 Delete `DownloadItemViewModel.GetFileKind` and route `FileKind` through the service;
      rewrite the `LogicTests` cases against the service, asserting the same extension mapping
- [x] 2.9 Test that a download with no name resolves to Other and re-resolves when the name arrives
- [x] 2.10 Test that creating a category claiming `epub` moves existing `.epub` downloads into it
      with no restart

## 3. Category management

- [x] 3.1 Add category create/edit: name, icon picked from the shipped icon set, color, extension
      list
- [x] 3.2 Enforce a non-empty name and refuse a duplicate name, showing why
- [x] 3.3 Store the name verbatim and never translate it; test that a Japanese name survives a
      switch to English and that built-in names do not follow the language
- [x] 3.4 Allow deleting a user-created category and forbid deleting a built-in one
- [x] 3.5 On delete, clear the `CategoryId` of every download overridden to it and re-resolve;
      test both the detected and the overridden case
- [x] 3.6 Allow moving a category up and down; persist the order
- [x] 3.7 Render an unrecognized icon key as the default icon while preserving the key in the
      configuration; test the round-trip

## 4. Sidebar

- [x] 4.1 Remove the nav-rail leftovers from `MainViewModel` (`SidebarWidth`'s two-value logic and
      the unused members) and replace the old width test
- [x] 4.2 Add the toggle button immediately left of the "Paste download link" box; hidden state by
      default, two states only
- [x] 4.3 Persist the shown/hidden state in the configuration and restore it on launch; test both
      directions
- [x] 4.4 Build the sidebar: an "All" entry, then every category in order with its icon, name and
      count, with a low-emphasis "Add" entry below the list
- [x] 4.5 Show empty categories de-emphasized rather than hiding them
- [x] 4.6 Make the counts respect the active status filter; test the Failed-plus-Video combination
- [x] 4.7 Mark the selected category visually
- [x] 4.8 Wire up create, edit and reorder from the sidebar

## 5. Filtering, the Type column and overrides

- [x] 5.1 Add the category dimension to `DownloadsViewModel.Matches`, combining with the status
      filter and the search text; test all three together
- [x] 5.2 Make "All" clear only the category dimension, leaving the status filter and search intact
- [x] 5.3 Confirm the filter applies to every state (running, queued, paused, failed, completed),
      not just completed; add a test with a running download
- [x] 5.4 Add an empty-state message with an action that clears the filters
- [x] 5.5 Add the Type column to `DownloadsView`, immediately before Name: category icon in the
      category color, tooltip naming the category
- [x] 5.6 Remove the type icon from the Name cell
- [x] 5.7 Make the Type column sortable by the category's position, not alphabetically; test that
      the order does not change with the app's language
- [x] 5.8 Rescope the header select-all checkbox to the filtered view, including its
      checked/indeterminate state
- [x] 5.9 Show the selected count in the toolbar while a filter is active
- [x] 5.10 Test that select-all under a category filter selects only the visible rows and that a
      bulk remove leaves the filtered-out downloads untouched
- [x] 5.11 Add the category chooser to the Add-download dialog, with an automatic option that names
      the detected category
- [x] 5.12 Add the category chooser to the Details window; test that changing it mid-download does
      not interrupt the download
- [x] 5.13 Add a category submenu to the download row's right-click menu
- [x] 5.14 Test that an override persists across a restart and that choosing automatic restores
      detection

## 6. MIME detection

> Depends on task 1.5.

- [ ] 6.1 Bump the `Downloader` package reference to the version from task 1.5
- [ ] 6.2 Carry `RemoteFileInfo.ContentType` through `UrlResolver` onto the download item
- [x] 6.3 Accept an optional `mime` value on `/api/add` (POST body and GET query) and record it; an
      add without it behaves exactly as before
- [x] 6.4 Accept an optional category identifier on `/api/add`, applying it as an explicit choice and
      ignoring an unknown identifier; pre-select it in the dialog on the confirm path
- [x] 6.5 Test the precedence: extension wins over content type; content type is used when there is
      no usable extension; neither yields Other

## 7. Export and import

- [x] 7.1 Define the export format: a version stamp, the settings, the categories, and a reserved
      always-null `iconData` field per category
- [x] 7.2 Exclude download data, queues, schedules, the save folder, the resolved local API port and
      the remembered window sizes; test that none of them appear in an export
- [x] 7.3 Add the export action to Settings with a save-file picker; cancelling writes nothing and
      raises no error
- [x] 7.4 Add the import action to Settings with an open-file picker; apply without a restart
- [x] 7.5 Validate before applying: reject a corrupt file leaving the current state untouched;
      ignore unrecognized fields from a newer version; refuse a file with an empty category list
- [x] 7.6 Clear overrides pointing at categories that the imported set does not contain
- [x] 7.7 Test a cross-platform round trip: an export produced with Linux-style paths in the
      settings imports on Windows with every category name, icon, color, extension list and position
      intact
- [x] 7.8 Test that imported categories re-categorize existing downloads that have no override, and
      that downloads with an override keep it when the category still exists

## 8. Localization, polish and verification

- [x] 8.1 Add every new user-facing string to all 16 language packs (sidebar toggle tooltip,
      "All", "Add category", the category editor labels, the empty state, the selected count, the
      automatic category option, export/import labels and their error messages)
- [x] 8.2 Check the sidebar and the Type column in right-to-left mode (Persian, Arabic): the
      sidebar mirrors and the Type column stays adjacent to Name
- [x] 8.3 Bump the plugin versions of any plugin whose source changed (none expected — confirm)
- [x] 8.4 `dotnet build Downloader.Desktop.sln -t:Rebuild --nologo` ends with 0 warnings
- [x] 8.5 `dotnet test -v q --nologo` fully green, including the new tests
- [x] 8.6 Run the browser-extension suites (`node --test src/browser-extension/common.test.js` and
      the Playwright suite) — unchanged, but the standing rule requires them green
- [x] 8.7 Regenerate `docs/screenshots/` with the capture test, view the PNGs to confirm the sidebar
      and the Type column render correctly, and commit only what changed
- [x] 8.8 Update `CLAUDE.md` and `docs/codebase-index.md` with the new service, models and views
