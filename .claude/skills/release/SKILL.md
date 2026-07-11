---
name: release
description: Publish a new Downloader Desktop version end-to-end (GitHub Release + macOS/Homebrew + Linux/Snap/curl + Windows/winget). Use whenever the author says "release", "deploy", "publish", or "go next version vX.Y.Z" — the whole routine is standing, pre-authorized, and must run WITHOUT asking again.
---

# Release Downloader Desktop (vX.Y.Z)

The author triggers this with a single sentence like **"go next version 1.7.0"**. Everything below is
pre-authorized (see CLAUDE.md → "Release routine") — do ALL of it without pausing for permission.
A release is only "done" when every channel in the checklist at the bottom is verified.

## TL;DR — the happy path

```bash
# 1. Write pretty Markdown highlights (MANDATORY — never ship a noteless release) to a temp file.
# 2. From the repo root, on a CLEAN tree:
./scripts/release.sh X.Y.Z --yes --notes-file /path/to/notes.md
# 3. Verify every channel (checklist below), then record the release in OpenSpec.
```

`scripts/release.sh` is the source of truth and does, in order:
1. **Preflight** — gh authenticated, clean tree, tag doesn't exist, develop ahead of main.
2. **Bump** `VersionPrefix` (csproj) + `snap/local/VERSION` on `develop`, commit + push.
3. **Merge `develop` → `main`** (`--no-ff`, message `release: vX.Y.Z (merge develop)`), push main.
4. **Tag `vX.Y.Z` on main** and push the tag. The tag push triggers CI:
   - `.github/workflows/release.yml` — builds win-x64 / linux-x64 / osx-x64 / osx-arm64
     (`Downloader-<rid>.tar.gz` / `.zip`), creates the GitHub Release and attaches them.
     The Linux `curl | bash` installer reads "latest", so it serves the new version automatically.
   - `.github/workflows/snap.yml` — builds the `.snap` and publishes it to the Snap Store
     stable channel (repo secret `SNAPCRAFT_STORE_CREDENTIALS` is set).
5. **Waits (≤30 min)** for both macOS archives to attach, then:
6. **Release notes** — sets the body to your Highlights + GitHub's auto "What's Changed".
7. **Homebrew** — updates `bezzad/homebrew-tap` `Casks/downloader.rb` (version + arm64/x64 sha256)
   and syncs the in-repo mirror `Casks/downloader.rb` on develop.
8. **winget** — bumps the in-repo mirror `packaging/winget/*.yaml` (PackageVersion ×3 +
   InstallerUrl/InstallerSha256 of `Downloader-win-x64.zip`) on develop, then opens a PR to
   `microsoft/winget-pkgs` under `manifests/b/bezzad/Downloader/X.Y.Z/` (dedup-checked: it skips
   if an open PR for that version exists).

The script is **portable (Linux + macOS)** — it uses `sedi`/`b64_file`/sed-based version parsing
instead of GNU-only `sed -i` / `base64 -w0` / `grep -P`. Keep it that way.

## Release notes — format rules (HIGH priority)

Write them BEFORE running the script, save to a file, pass via `--notes-file`. Never plain text:
- One-line summary sentence first.
- Short emoji-grouped sections: `### ✨ New` / `### 🐛 Fixes` / `### 🔧 Under the hood`,
  a few concise **end-user** bullets each (no commit hashes, no internal jargon).
- Optionally end with a thin divider + an install hint.
- Derive content from `git log --no-merges --pretty='- %s' v<prev>..develop` but rewrite for users.
- Good examples on GitHub: v1.0.0 / v1.1.0 / v1.2.0.

## Preconditions to check first

- `gh auth status` OK; working tree clean (`git status --porcelain` empty); on any branch (script
  checks out what it needs); `develop` pushed and green (CI + `dotnet test` from `src/`).
- The version must be three-part `X.Y.Z` and match what you'll tell users ("v1.6" ⇒ `1.6.0`).
- Never re-tag: re-running a version needs the tag AND the GitHub Release deleted first.

## Running unattended (AI session pattern)

The asset wait can take ~10–30 min, longer than a foreground Bash timeout. Run it in the
background and poll:

```bash
export GH_TOKEN="$(gh auth token)"   # REQUIRED in background runs — see gotcha below
./scripts/release.sh X.Y.Z --yes --notes-file notes.md   # run_in_background
# poll: gh release view vX.Y.Z --json assets --jq '.assets[].name'   (expect 6 binary assets incl. the two extension zips)
# and:  gh run list --limit 3   (release.yml + snap.yml conclusions)
```

**macOS keyring gotcha (bit the v1.6.0 run):** `gh` stores its token in the macOS Keychain, which a
*detached background process cannot read* — `gh auth status` fails there with "not authenticated"
even though it works interactively. Always `export GH_TOKEN="$(gh auth token)"` (resolved in the
foreground) as part of the background command.

## Verification checklist (all channels — release isn't done until these pass)

```bash
gh release view vX.Y.Z --repo bezzad/Downloader.Desktop        # body has Highlights; 6 binary assets:
#   Downloader-win-x64.zip, Downloader-linux-x64.tar.gz, Downloader-osx-{x64,arm64}.tar.gz,
#   downloader-extension-{chrome,firefox}.zip (attached by release.yml's post-matrix extension job)
gh run list --repo bezzad/Downloader.Desktop --limit 5         # release.yml + snap.yml green
gh api repos/bezzad/homebrew-tap/contents/Casks/downloader.rb --jq '.content' | base64 -d | grep version
gh pr list --repo microsoft/winget-pkgs --author @me           # X.Y.Z PR open (moderator merges later)
snap info downloader 2>/dev/null | grep latest/stable          # Snap Store (may lag a few minutes)
```

If snap CI failed to publish (check the run log): download its artifact and publish manually —
`gh run download <id> -n downloader-snap && snapcraft upload --release=stable downloader_X.Y.Z_amd64.snap`.

## After the release

- Record it in OpenSpec (a short note in the relevant change/archive: version, tag, commit hashes,
  tap commit, winget PR #) and commit on develop.
- The winget PR waits on a community moderator — nothing to do; check back with
  `gh pr list --repo microsoft/winget-pkgs --author @me`. Close stale older-version PRs instead of
  stacking duplicates.
- If the author later reports "update available" false alarms, see SKILL.md → versioning
  (`VersionPrefix` must be the full 3-part semver; release.yml stamps it from the tag).

## Known gotchas (learned the hard way)

- **Noteless releases are forbidden** — `release.yml` has a `notes` job that only fills the body if
  it's still empty, so it never clobbers curated notes; but curated notes are still mandatory.
- **Do NOT add `generate_release_notes: true` to the matrix steps** in release.yml — 4 concurrent
  release-creates race (`tag_name already_exists`) and an asset upload fails (bit v1.4.0).
- **macOS builds are NOT single-file** (compressed single-file crashes on Apple Silicon) — release.yml
  already handles this; don't "simplify" it.
- **winget identifier stays `bezzad.Downloader`** (Moniker `downloader`); installer = portable zip.
- The GitHub Release is created by the FIRST matrix job to finish; `gh release edit` before any asset
  exists can fail — the script retries via its wait loop.
- Script must stay **BSD/macOS-compatible**: use the `sedi`/`b64_file` helpers, never raw
  `sed -i`/`base64 -w0`/`grep -P`.

## Browser extension distribution (automated pieces)
- Every `v*` release also carries `downloader-extension-chrome.zip` + `downloader-extension-firefox.zip` (release.yml `extension` job, runs post-matrix — never let it create the release or set notes).
- **Firefox publishes itself**: `extension.yml` fires on pushes touching `src/browser-extension/**`, skips green when the manifest version already exists on AMO, else `web-ext sign --channel listed --approval-timeout 0` with secrets `AMO_JWT_ISSUER`/`AMO_JWT_SECRET`. Bumping the extension version (BOTH manifests) + pushing IS the Firefox release. Chrome/Edge stay manual: upload the chrome zip from the release page in their dashboards.

## release.sh is now machine-independent + RESUMABLE (2026-07-11, post-v2.0.0)
Five failure classes that previously needed manual rescue are fixed IN the script:
1. **Resume**: if the requested tag already exists, the run WARNS and resumes — skips bump/merge/tag and finishes asset-wait → notes → Homebrew → winget (all idempotent). A mid-run death is recovered by simply re-running the same command. Re-releasing a *different* dead version still requires deleting the tag+Release first.
2. **develop==main is OK** when the version differs — the bump commit becomes the release commit (pre-merging develop→main to verify CI no longer blocks).
3. **Git identity** is resolved (repo → global → gh account → fallback) and passed explicitly (`-c user.name/email`) on every commit incl. the fresh tap clone — no global git config needed.
4. **Token**: `GH_TOKEN` is resolved up front (`gh auth token`) and embedded in the tap clone URL — works detached/background (macOS keyring) and without `gh auth setup-git`.
5. **retry()** (5×, 10 s) wraps every push/pull/clone — transient GitHub connectivity drops don't kill the run.
Validated by re-running `release.sh 2.0.0` post-release: resume mode no-ops every channel cleanly.

## release.sh preflight asks-and-fixes; exit report (2026-07-11)
A bare `bash scripts/release.sh` is the intended entry point — it prompts for anything missing
instead of aborting: version (suggests next patch), release notes (multi-line, `.` to end),
`gh auth login` runs inline if unauthenticated, and a dirty tree offers to autostash (an EXIT
trap restores it on `develop` afterwards — success OR failure). Non-interactive runs (no TTY)
still die with exact instructions. The same EXIT trap prints a per-channel report table
(GitHub Release / notes / curl / Snap / Homebrew / winget) on ANY exit, plus a "re-run to
resume" hint on failure. Changelog baseline = `PREV_TAG` (newest `v*` tag excluding the tag
being published) — never `v$CUR_VERSION`, which on a resume equals the new tag itself and
yields an empty changelog.
