<p align="center">
  <img src="docs/banner.svg" alt="Downloader — fast multi-connection download manager" width="100%">
</p>

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
- **Automatic file names** — leave the name blank and the app detects it from the link or server.
- **Queues** — group downloads and control how many run at the same time.
- **Scheduler** — start and stop a queue automatically at set times (e.g. download overnight).
- **File-type icons** at a glance — video, audio, image, document, archive, app, disc.
- **Clear status** — live progress and speed, a friendly reason when something fails, and a details view with per-connection progress.
- **Light & dark themes** with a modern ocean-blue look.
- **Desktop notifications** when a download completes or fails (uses your OS's native notifications where available).
- **System tray** — closing the window keeps downloads running in the background; reopen, mute notifications, or quit from the tray menu. Optionally **launch at startup** (hidden in the tray).
- **Automatic update check** — get an in-app prompt when a new version ships, and update with one click.
- **Multi-language UI** — English, فارسی (Persian), Español, Français, العربية (Arabic), Esperanto — with full right-to-left layout for Persian/Arabic. Switch under **Settings → App language**.
- **No installation, no dependencies** — fully self-contained. You do **not** need to install .NET, FFmpeg, or anything else; just download and run.
- **Your settings, your way** — sensible defaults out of the box, saved the moment you change them, with every engine option available under Settings → Advanced.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/settings-dark.png">
  <img alt="Settings" src="docs/screenshots/settings-light.png">
</picture>

## Install
The app is **fully self-contained** — every release ships with everything it needs bundled in, so there are **no prerequisites to install** (no .NET runtime, no FFmpeg, no extra libraries).

### Quick install

**Linux** — downloads the latest release and adds a menu entry + icon:
```bash
curl -fsSL https://raw.githubusercontent.com/bezzad/Downloader.Desktop/main/scripts/install.sh | bash
```

**Windows & macOS** — no package-manager listing yet (see note below); use [Manual download](#manual-download).

> **winget and Homebrew are not published yet.** `winget install downloader` and `brew tap bezzad/tap` will fail until those listings go live — the manifests are prepared and release-ready in [`packaging/winget`](packaging/winget) and [`Casks/`](Casks/), but winget requires a reviewed PR to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) and Homebrew requires a `bezzad/homebrew-tap` repo, neither of which exists yet. This section will be updated with working commands once both are live.

### Manual download
1. Download the build for your operating system from the [Releases](https://github.com/bezzad/Downloader.Desktop/releases) page.
2. Unzip it anywhere.
3. Run the `Downloader` executable. That's it.

> The version number is shown under **Settings → About** and increases automatically with every release. With **Settings → Check for updates automatically** on, the app tells you (via an in-app toast) when a newer release is available and can download & install it for you.

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

---

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
