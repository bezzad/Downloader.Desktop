# Released in v2.14.1 (2026-09-26)

A patch release with a single user-visible fix: YouTube downloads work again through the optional
**Video sites** plugin (`com.bezzad.site-media` 1.4.3). No app-behaviour change beyond the plugin and
its error wording, so patch rather than minor.

## What shipped

- **Issue #18 — "YouTube videos cannot be downloaded"** (`80d4bda`, reviewed in `034c5d4`). The
  site-media extractor preferred an HLS copy of a quality over the direct video+audio pair. Current
  YouTube extractions always carry HLS copies, so *every* YouTube link was refused with "adaptive
  stream only" — the plugin was rejecting the only formats it could actually download. Now:
  - a direct stream wins over an HLS copy for every pick, and each direct pick is proved
    "directly fetchable" by its own test;
  - HLS-only qualities are not offered in the quality picker, so the list cannot contain an entry
    that must fail;
  - an HLS-only answer triggers re-extraction (no session, then the other clients) before giving up;
  - the failure message no longer tells the user to install a plugin they already have.
- Docs: README documents the Video sites plugin and corrects the YouTube note (`1c70393`).

## Release mechanics (note for the next one)

The release was **cut in two halves by two different sessions**. A cloud session ran the version bump
and the `develop` → `main` merge but could not push tags, so it stopped with `main` merged and no tag.
`release.sh 2.14.1` was then re-run locally and took the resume path exactly as designed: it detected
`develop == main` with `VersionPrefix` already 2.14.1 ("bump and merge are already done"), skipped both,
tagged `main`, and finished every remaining channel. Nothing had to be undone by hand — the resume
behaviour added post-v2.0.0 covers a half-finished release from a *different machine*, not just a
mid-run death on the same one.

## Coordinates

| | |
|---|---|
| tag | `v2.14.1` (on `main`) |
| release merge | `9ebe85b` (`release: v2.14.1 (merge develop)`, pushed by the cloud session) |
| develop at cut | `6f21f20` (`chore(release): bump version to 2.14.1`), CI green |
| release run | 36234736254 (all 10 jobs success), Snap run 36234736262 success |
| plugin | `com.bezzad.site-media` 1.4.3 in the release's `plugins-catalog.json` |
| Homebrew tap | `bezzad/homebrew-tap@a5eb995` (mirror `44f2c62` on develop) |
| winget | PR [microsoft/winget-pkgs#441641](https://github.com/microsoft/winget-pkgs/pull/441641), open, awaiting a moderator (mirror `1eab6fb`) |
| AUR | `downloader-bin` 2.14.1-1, published by CI (mirror `6be17c2`) |
| Snap | `latest/stable` 2.14.1, revision 32 |

## Verified

Checked per channel rather than trusting the script's report: 13 assets attached (four platform
archives, both extension zips, three optional-plugin zips, both catalogs, the `.deb`, the `.snap`);
release body carries the curated Highlights; `Release` and `Snap` workflows green; tap cask at 2.14.1
with both macOS checksums; exactly one open winget PR (no duplicate stacked on #438835's version);
`snap info` reports 2.14.1 (rev 32); AUR RPC reports 2.14.1-1; the released `plugins-catalog.json`
lists `com.bezzad.site-media` at 1.4.3 — which is the path by which a user actually receives the #18
fix, since the plugin is not bundled.

The AUR RPC endpoint failed its first request with a TLS `unexpected eof`; a retry answered correctly.
Transient, unrelated to the release.
