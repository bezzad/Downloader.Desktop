<p>
  <img src="docs/banner.svg" alt="Downloader — fast multi-connection download manager" width="100%">
</p>

[![Build (Windows/Linux/macOS)](https://img.shields.io/github/actions/workflow/status/bezzad/Downloader.Desktop/dotnet-desktop.yml?branch=develop&label=build%20(windows%2Flinux%2FmacOS))](https://github.com/bezzad/Downloader.Desktop/actions/workflows/dotnet-desktop.yml)
[![Release Pipeline](https://img.shields.io/github/actions/workflow/status/bezzad/Downloader.Desktop/release.yml?label=release)](https://github.com/bezzad/Downloader.Desktop/actions/workflows/release.yml)
[![Latest Release](https://img.shields.io/github/v/release/bezzad/Downloader.Desktop?display_name=tag)](https://github.com/bezzad/Downloader.Desktop/releases)
[![Downloads](https://img.shields.io/github/downloads/bezzad/Downloader.Desktop/total)](https://github.com/bezzad/Downloader.Desktop/releases)
[![Coverage (planned)](https://img.shields.io/badge/coverage-coming%20soon-lightgrey)](https://github.com/bezzad/Downloader.Desktop/actions/workflows/dotnet-desktop.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-2ea44f)](https://github.com/bezzad/Downloader.Desktop/releases)

[![downloader](https://snapcraft.io/downloader/badge.svg)](https://snapcraft.io/downloader)
[![downloader](https://snapcraft.io/downloader/trending.svg?name=0)](https://snapcraft.io/downloader)

<a href="https://snapcraft.io/downloader">
   <picture>
      <source media="(prefers-color-scheme: dark)" srcset="https://snapcraft.io/en/dark/install.svg">
      <img alt="Get it from the Snap Store" src="https://snapcraft.io/en/light/install.svg" />
   </picture>
</a>

A fast, reliable, cross-platform **download manager** with a clean desktop UI for **Windows, macOS and Linux**. It splits each file into multiple connections for maximum speed, lets you pause and resume any time, and organizes your downloads with queues and a scheduler — all in a simple interface anyone can use.

Built with [Avalonia UI](https://avaloniaui.net/) on .NET and powered by the [Downloader](https://github.com/bezzad/downloader) engine.

<!-- Theme-aware: GitHub shows the dark shot in dark mode, the light shot otherwise. -->
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/home-dark.png">
  <img alt="Downloader" src="docs/screenshots/home-light.png">
</picture>

## Features
- **Multi-connection downloads** — each file is split into several parts and downloaded in parallel for higher speed.
- **Pause / resume / stop** any download, any time. Incomplete downloads resume after you restart the app.
- **Add one or many links** — paste a single URL or many at once (one per line) and send them all to the same folder.
- **Clipboard suggestions** — copied a link? The Add dialog offers it automatically; press **Enter** to use it, or just type over it.
- **Automatic file names** — leave the name blank and the app detects it from the link or server.
- **Queues** — group downloads and control how many run at the same time.
- **Scheduler** — start and stop a queue automatically at set times (e.g. download overnight).
- **File-type icons** at a glance — video, audio, image, document, archive, app, disc.
- **Clear status** — live progress and speed, a friendly reason when something fails, and a details view with per-connection progress.
- **Light & dark themes** with a modern ocean-blue look.
- **Desktop notifications** when a download completes or fails (uses your OS's native notifications where available).
- **Dynamic Island (notch)** — an optional slim pill at the top-center of your screen showing the clock and live download speed; hover it to peek at active downloads without opening the app (Settings → Dynamic Island, off by default).
- **System tray** — closing the window keeps downloads running in the background; reopen, mute notifications, or quit from the tray menu. Optionally **launch at startup** (hidden in the tray).
- **Automatic update check** — get an in-app prompt when a new version ships, and update with one click.
- **Extensible via plugins** — a plugin can turn a link the engine can't grab directly into a real download: the app downloads all the parts and assembles them into one file for you (e.g. **HLS `.m3u8`** streams → a single playable video via the HLS plugin). Multi-part downloads show live "Part 3/10" progress and an "Assembling…" step, and resume where they left off after a restart.
- **Download Ollama models** — paste an `ollama.com` link or just type a model name like `gemma3:12b`, download it at full multi-connection speed, then click **Add to Ollama** to install it into your local Ollama in one step (checksum-verified). Ships built-in, together with the **GitHub Releases** plugin (paste `github.com/owner/repo` → get the latest release for your OS).
- **Automation-friendly** — add and manage downloads from scripts or the terminal via a local API and CLI (see [Automation](#automation-local-api--command-line)).
- **Multi-language UI** — English, فارسی (Persian), Español, Français, العربية (Arabic), Esperanto — with full right-to-left layout for Persian/Arabic. Switch under **Settings → App language**.
- **No installation, no dependencies** — fully self-contained. You do **not** need to install .NET, FFmpeg, or anything else; just download and run.
- **Your settings, your way** — sensible defaults out of the box, saved the moment you change them, with every engine option available under Settings → Advanced. Dialogs are resizable and reopen at the size you left them.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/settings-dark.png">
  <img alt="Settings" src="docs/screenshots/settings-light.png">
</picture>

## Install
The app is **fully self-contained** — every release ships with everything it needs bundled in, so there are **no prerequisites to install** (no .NET runtime, no FFmpeg, no extra libraries).

### Quick install

**macOS** ([Homebrew](https://brew.sh)):
```bash
brew tap bezzad/tap && brew install --cask downloader
```
This installs **Downloader.app** into your Applications folder — open it from **Launchpad**, **Spotlight** (⌘-Space → "Downloader"), or Finder like any other app.
> - Recent Homebrew versions ask you to trust a third-party tap before installing. If you see that prompt, run `brew trust bezzad/tap` (or set `HOMEBREW_NO_REQUIRE_TAP_TRUST=1`) and re-run the install.
> - The app isn't notarized yet. The Homebrew cask **removes the quarantine flag automatically on install**, so it should launch normally — you do **not** need to run `xattr` by hand. If macOS still blocks the first launch, right-click **Downloader** in Applications → **Open**, or run `xattr -dr com.apple.quarantine "/Applications/Downloader.app"`.

**Linux (Snap)** — recommended; auto-updates via the Snap Store:
```bash
sudo snap install downloader
```
> Pending first publish to the Snap Store. Until it's live, use the install script below or [Manual download](#manual-download).

**Linux (script)** — downloads the latest release and adds a menu entry + icon:
```bash
curl -fsSL https://raw.githubusercontent.com/bezzad/Downloader.Desktop/main/scripts/install.sh | bash
```

**Windows** ([winget](https://learn.microsoft.com/windows/package-manager/winget/)):
```powershell
winget install bezzad.Downloader
```
This installs the portable app and puts `Downloader` on your PATH — launch it from the Start menu or a terminal. Update later with `winget upgrade bezzad.Downloader`.
> The short form `winget install downloader` also resolves to this package via its moniker (if another package matches first, use the full id above).

### Manual download
1. Download the build for your operating system from the [Releases](https://github.com/bezzad/Downloader.Desktop/releases) page.
2. Unpack it:
   - **macOS** — open `Downloader-osx-*.tar.gz` and drag **Downloader.app** into your **Applications** folder, then launch it from Launchpad/Spotlight.
   - **Windows / Linux** — unzip anywhere and run the `Downloader` executable. That's it.

> The version number is shown under **Settings → About** and increases automatically with every release. With **Settings → Check for updates automatically** on, the app downloads a newer release in the background, then shows an **“Update Downloader”** button at the bottom of the left menu plus a system notification. Click it (or just close the app) to install — it restarts into the new version. _Snap builds update through the Snap Store instead, so the in-app updater is disabled there._

> First launch on an unsigned build: **Windows** SmartScreen → *More info → Run anyway*; **macOS** Gatekeeper → right-click → *Open* (or `xattr -dr com.apple.quarantine <app>`).

## Using the app
1. **Add a download** — paste a link into the top bar and click **Add** (or press `Ctrl+N`). In the dialog you can choose the save folder, optionally set a name, and pick a queue. To add several at once, paste multiple links (one per line).
2. **Control downloads** — each row has pause/resume/stop and, when finished, an *open-folder* button. Tick the checkboxes and use the toolbar to **Start / Pause / Stop / Remove** several at once.
3. **See details** — double-click a row to open the details window: overall progress, speed, the failure reason (if any), a live speed limit, and a per-connection progress strip that updates live as connections come and go (press `Esc` to close).
4. **Filter** — the left sidebar filters by **All / Active / Completed / Failed**. Collapse the sidebar to icons with the ☰ button.
5. **Queues & Scheduler** — under **Manage**, create queues with a concurrency limit (only that many downloads run at once; the rest wait their turn) and schedules that run them at chosen times.
6. **Settings** — set your default save folder, connections per download, speed limit and theme; everything else lives under **Advanced**.

Your downloads list and settings are saved automatically. Config file location:
- **Linux:** `~/.config/Downloader/config.json`
- **macOS:** `~/Library/Application Support/Downloader/config.json`
- **Windows:** `%APPDATA%\Downloader\config.json`

## Browser extension

A companion **browser extension** (Chrome, Edge, Firefox) sends links straight to the app:

- Right-click a link/image/video/audio → **“Download with Downloader.”**
- A popup to paste a link, scan the page for links, or grab detected **video / audio / HLS (`.m3u8`)** media.
- Captured links go straight into the app and **start downloading — no dialog**. Prefer to review each link first? Untick **Add silently** in the extension popup.
- Links are sent only to the desktop app on your own machine (**Settings → Browser extension & local API**, on by default). DRM/encrypted sites like YouTube aren’t supported.

**Install** (store listings pending review — see [`src/browser-extension`](src/browser-extension)):
- _Chrome Web Store / Edge Add-ons / Firefox AMO_ — links added here once published.
- **Load it now (developer mode):** see [`src/browser-extension/README.md`](src/browser-extension/README.md) to load the unpacked extension.

## Automation: local API & command line

Other programs on your computer can add and manage downloads — handy for batch jobs and scripts
(the most-requested developer feature, [#2](https://github.com/bezzad/Downloader.Desktop/issues/2)):

```bash
# From a terminal / script: add a download (starts the app if it isn't running)
Downloader add --url https://example.com/file.zip --path ~/Downloads

# Or over the local HTTP API from any language (Node.js, Bun, Python, curl, …)
curl -X POST http://127.0.0.1:15151/api/add -d '{"url":"https://example.com/file.zip"}'
```

You can also `list` all downloads (with progress and status) and `pause / resume / cancel / retry /
remove` each one. Everything is **local-only** (loopback, never exposed to the network) and gated by
the same **Settings → Browser extension & local API** toggle, on by default. If another program is
already using port `15151`, the app automatically switches to the next port in `15151`–`15155` — the
extension and CLI find it on their own, and Settings shows the current address.

📖 **Full reference with examples:** [`docs/local-api.md`](docs/local-api.md)

---

## Reporting bugs & requesting features

Please report bugs and request features through **[GitHub Issues](https://github.com/bezzad/Downloader.Desktop/issues)** — not by Telegram or email. Issues are tracked, searchable, and handled through an automated workflow, so this is the **fastest** way to get a response (and it lets others find the same fix). Before opening one, search existing issues to avoid duplicates.

## Build from source
```shell
git clone https://github.com/bezzad/Downloader.Desktop.git
cd Downloader.Desktop/src
dotnet run --project Downloader.Desktop/Downloader.Desktop.csproj
```
Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). Full developer docs — publishing,
packaging, releasing and macOS bundling — are in [CONTRIBUTING.md](CONTRIBUTING.md).

## License
[MIT](LICENSE)
