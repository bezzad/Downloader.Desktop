## Why

The companion browser extension is the feature that makes the app useful on video and download-gated
sites, and today there is **no way to install it from inside the app**. The README's own install section
reads *"store listings pending review — links added here once published"* and then points at
`src/browser-extension/README.md` for developer-mode loading. In practice that means a non-technical user
— the stated audience of this app — cannot get the extension at all.

The author asked whether the app could detect installed browsers, ask the OS for elevation, and install
the extension itself. **It cannot, and elevation makes it worse.** No mainstream browser accepts a locally
installed unsigned extension into a normal profile:

- Chrome/Edge/Brave/Vivaldi: the external-install registry key and the `External Extensions` JSON both
  only accept an `update_url` pointing at the Web Store; local `.crx` sideloading was removed years ago.
- Firefox: an `.xpi` must be Mozilla-signed; an unsigned one in `distribution/extensions` is ignored on
  release builds.
- The only remaining force-install path is **enterprise policy**, which for Chrome still needs a
  store-hosted extension.

So the *only* thing administrator rights would buy is a write to a browser **policy** key — which is the
signature technique of browser hijackers and adware, weighted higher by antivirus heuristics precisely
*because* it is elevated. This repo already lost a release to that class of verdict (issue #4, Bitdefender
ATD), and `NoShellSpawnTests` exists to stop it recurring. Writing `ExtensionInstallForcelist` from an
unsigned `Downloader.exe` behind a UAC prompt would be a worse version of the same mistake.

What the app *can* do, with no elevation and no AV surface, is remove every step of the install a user
should not have to do by hand: find their browsers, fetch the right build, verify it, unpack it to a
stable folder, hand them the path and the three steps, and then **show them it worked**.

## What Changes

### 1. The app installs the extension files; the browser does the install

A new **Install browser extension** button in Settings → Browser extension & local API opens a dialog that:

- **lists the browsers actually installed on the machine** (read-only detection: registry keys on Windows,
  `.desktop`/PATH/known paths on Linux, `/Applications` bundles on macOS). Browser **profiles, cookie
  stores and saved passwords are never read** — only whether the browser exists and where its executable
  is. That boundary is the same one that made the HLS plugin drop `--cookies-from-browser`.
- lets the user pick one or more of them,
- for each pick, either **opens that browser at its store listing** (once a listing exists) or **downloads,
  verifies and unpacks the matching build** and shows the folder path plus the per-browser steps,
- shows a live **Connected ✓** marker per browser, so the user gets confirmation instead of guessing.

No elevation is ever requested. Nothing is written outside the app's own data directory.

### 2. The build is fetched from the GitHub release, not bundled in the app

Settled with the author, and the reasoning is worth recording because the obvious objection is the AV rule:

- **The AV rule is about download-then-*execute***. Nothing downloaded here is executed by this app or by
  the OS on its behalf — it is data files that a *different* program (the browser, under the user's own
  explicit action) later reads. That is categorically weaker than the yt-dlp/deno/ffmpeg case the rule was
  written for.
- **The precedent already ships**: `PluginCatalogService` + `PluginManager.InstallFromZipAsync` already
  download a zip from the latest GitHub release and **verify its sha256 before** anything is loaded. This
  reuses that shape exactly, with a lower-risk payload (no assembly is ever loaded).
- **The decisive benefit is update decoupling, not app size.** The zips are ~50 KB, so bundling would cost
  almost nothing in size; the real win is that a broken extractor or a store-policy fix can reach users
  **without shipping a new app release**.

The cost of decoupling is version skew: an extension newer than the app can call a local API the app does
not serve. Handled the same way the plugin catalog handles it — a `minAppVersion` on each catalog entry,
enforced before the build is offered.

### 3. The app can tell the user their extension is out of date

Today the extension never reports its version; the local API only sees `/api/add` and `/api/can-handle`.
It will now identify itself (version + browser label) on the requests it already makes, so the app can say
*"Chrome extension 1.6.1 — 1.7.0 available"* and offer the same install flow. This needs **no new browser
permission** and no polling.

### 4. Release plumbing and a version-drift fix

`scripts/build-extension.sh` gains an `extension-catalog.json` (version + sha256 per target, generated the
same way `build-plugins.sh` generates the plugin catalog), attached to the same release by the existing
`extension` job. And `manifest.json` (1.6.1) is realigned with `manifest.firefox.json` (1.7.0), with a
test so the two manifests can never drift again.

## Non-goals (deliberately out of scope)

- **Any form of automatic install into a browser.** Not via registry, not via enterprise policy, not via
  the `External Extensions` JSON, not with elevation. See Why.
- **Firefox self-hosted auto-update.** It is genuinely possible (AMO self-distribution signing + a
  `gecko.update_url` pointing at a GitHub-hosted JSON), but it needs a separate signing pipeline and only
  helps Firefox. Recorded in `design.md` as the follow-up it is.
- **Publishing to the Chrome Web Store / Edge Add-ons.** Author-gated (dev accounts, fees, review). This
  change makes the store path a one-line configuration switch for when those listings go live.
- **Reading anything from a browser profile.** Never.
