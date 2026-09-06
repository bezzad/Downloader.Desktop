## Why

Pasting a GitHub release page into the app looks like it finds nothing. The author pasted
`https://github.com/bezzad/Downloader.Desktop/releases#release-v2.10.0`: the dialog showed the
"Handled by GitHub Releases" badge, no file name, "Unknown size", and — under "Choose what to download" —
a single unrelated offer, *"Offline copy (.zip)"*, from the Website fallback plugin.

Probed against the live API, the resolver does in fact return `Downloader-linux-x64.tar.gz`. The problem is
that **nothing in the Add window ever says so**: `GetVariantsAsync` returns null for every GitHub link, so
the choices slot falls through to the fallback plugin, and the name/size preview probes the HTML page rather
than asking the plugin. A user cannot tell a working download from a broken one before pressing Download.

The same probe found two defects with worse consequences than a confusing dialog:

| Pasted link | Downloaded today |
|---|---|
| `…/releases/tag/v2.9.0` | v2.10.0's asset — a different release, silently |
| `…/releases/download/v2.9.0/Downloader-linux-x64.tar.gz` | v2.10.0's asset — a direct file link fetches another file |
| `…/issues/14` | the app tarball |
| `…/blob/main/README.md` | the app tarball |

`CanResolve` claims any `github.com/<owner>/<repo>/…` path, and `ResolveAsync` always asks for
`releases/latest`, so every one of those links is answered with the latest release's asset.

## What Changes

- **The release's assets become choices.** A claimed link lists one variant per asset (name + size), with
  the asset matching the running OS pre-selected, so the Linux build is visible in the dialog and the user
  can pick a different one (a checksum file, another platform, the extension zip).
- **The link's own release is the one downloaded.** A `/releases/tag/<tag>` path or a `#release-<tag>`
  anchor resolves that release; only a repo root or a bare `/releases` means "latest".
- **Links that are not a release are left alone.** A direct `…/releases/download/…` asset URL, an issue, a
  pull request, a discussion, a wiki or a tree page is no longer claimed, so it downloads as itself.
- **A file link resolves to the file.** `…/blob/<ref>/<path>` (and `…/raw/…`) resolves to the raw file
  rather than the page that displays it.
- **A release with no assets says so** instead of failing with an internal error.

## Capabilities

### New Capabilities
- `github-release-download`: what a github.com link means to the app — which links the GitHub plugin
  claims, which release and asset a claimed link resolves to, and what the Add window offers for it.

### Modified Capabilities
<!-- none: the plugin/variant capabilities already describe the machinery this uses -->

## Impact

- `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.GitHub/GitHubReleasesPlugin.cs` — `CanResolve`,
  `ResolveAsync`, a new `GetVariantsAsync`, and URL parsing shared by all three.
- The plugin's `Version` string (a built-in plugin: its version lives in code, not a catalog csproj).
- `src/Downloader.Desktop.Tests/Plugins/` — new tests for the URL classification (pure, offline) and the
  asset choice; the existing live-network test stays gated behind `DLDESKTOP_NET=1`.
- No app-side change: variants, the Add picker and `VariantId` round-tripping already exist and are spec'd.
