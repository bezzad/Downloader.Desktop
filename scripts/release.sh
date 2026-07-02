#!/usr/bin/env bash
# Downloader Desktop — one-shot release routine.
#
# What it does, in order:
#   1. Asks you for the new version (or takes it as the first argument).
#   2. Bumps VersionPrefix (csproj) + snap/local/VERSION on `develop` and pushes.
#   3. Merges `develop` -> `main` (the default branch) and pushes `main`.
#   4. Tags `vX.Y.Z` on `main` and pushes the tag. The tag push triggers:
#        - .github/workflows/release.yml  -> builds win/linux/macOS x2, creates the
#          GitHub Release and attaches the archives (the curl installer reads "latest",
#          so it starts serving the new version automatically).
#        - .github/workflows/snap.yml     -> builds the .snap and publishes it to the
#          Snap Store stable channel (needs the SNAPCRAFT_STORE_CREDENTIALS repo secret).
#   5. Waits for the Release build to finish, then updates Homebrew: rewrites
#      bezzad/homebrew-tap `Casks/downloader.rb` (version + the two macOS sha256) and
#      syncs the in-repo mirror `Casks/downloader.rb` on `develop`.
#
# So a single run fans the release out to: GitHub Releases, the Linux curl installer,
# the Snap Store, and Homebrew.
#
# Usage:
#   scripts/release.sh                # prompts for the version (suggests next patch)
#   scripts/release.sh 1.3.2          # non-interactive version
#   scripts/release.sh 1.3.2 --yes    # also skip the confirmation prompt
#
# Requirements: git, gh (authenticated: `gh auth status`), and shasum or sha256sum.
set -euo pipefail

REPO="bezzad/Downloader.Desktop"
TAP_REPO="bezzad/homebrew-tap"
MAIN_BRANCH="main"          # the repo's default/release branch (the "master" branch)
DEV_BRANCH="develop"
CSPROJ="src/Downloader.Desktop/Downloader.Desktop.csproj"
SNAP_VERSION_FILE="snap/local/VERSION"
CASK_MIRROR="Casks/downloader.rb"
WINGET_DIR="packaging/winget"          # in-repo winget manifest mirror
WINGET_UPSTREAM="microsoft/winget-pkgs"

# --- helpers ---------------------------------------------------------------
c_blue=$'\033[1;34m'; c_green=$'\033[1;32m'; c_red=$'\033[1;31m'; c_yellow=$'\033[1;33m'; c_off=$'\033[0m'
step() { echo "${c_blue}==>${c_off} $*"; }
ok()   { echo "${c_green}  ✓${c_off} $*"; }
warn() { echo "${c_yellow}  !${c_off} $*"; }
die()  { echo "${c_red}error:${c_off} $*" >&2; exit 1; }

sha256_of() { # stream a file on stdin -> hex digest
  if command -v sha256sum >/dev/null 2>&1; then sha256sum | awk '{print $1}';
  else shasum -a 256 | awk '{print $1}'; fi
}

# Portable in-place sed: GNU (Linux) takes -i with no arg, BSD (macOS) needs -i ''.
sedi() {
  if sed --version >/dev/null 2>&1; then sed -i "$@"; else sed -i '' "$@"; fi
}

# Portable single-line base64 of a file (GNU has -w0; BSD/macOS does not).
b64_file() { base64 < "$1" | tr -d '\n'; }

# --- args ------------------------------------------------------------------
# Usage: release.sh [VERSION] [--yes] [--notes-file PATH]
VERSION=""; ASSUME_YES="no"; NOTES_FILE=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --yes|-y) ASSUME_YES="yes" ;;
    --notes-file) shift; NOTES_FILE="${1:-}" ;;
    -*) die "unknown flag: $1" ;;
    *) [[ -z "$VERSION" ]] && VERSION="$1" || die "unexpected argument: $1" ;;
  esac
  shift
done

# Move to the repo root so relative paths work regardless of where it's invoked.
cd "$(git rev-parse --show-toplevel)" || die "not inside a git repository"

# --- preflight -------------------------------------------------------------
step "Preflight checks"
command -v gh  >/dev/null 2>&1 || die "the GitHub CLI 'gh' is required (https://cli.github.com)"
gh auth status >/dev/null 2>&1 || die "gh is not authenticated — run: gh auth login"
{ command -v sha256sum >/dev/null 2>&1 || command -v shasum >/dev/null 2>&1; } \
  || die "need sha256sum or shasum to checksum the macOS archives"
[[ -z "$(git status --porcelain)" ]] || die "working tree is dirty — commit or stash first"
ok "tools present, gh authenticated, tree clean"

git fetch origin --tags --quiet
# (portable — grep -P is GNU-only and absent on macOS)
CUR_VERSION="$(sed -n 's|.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*|\1|p' "$CSPROJ" | head -1)"
[[ -n "$CUR_VERSION" ]] || die "cannot read VersionPrefix from $CSPROJ"
ok "current version: $CUR_VERSION"

# Suggest the next patch version.
IFS='.' read -r MA MI PA <<<"$CUR_VERSION"
SUGGEST="$MA.$MI.$((PA + 1))"

if [[ -z "$VERSION" ]]; then
  read -r -p "New version to release [$SUGGEST]: " VERSION
  VERSION="${VERSION:-$SUGGEST}"
fi
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "version must be MAJOR.MINOR.PATCH, got '$VERSION'"
TAG="v$VERSION"
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && die "tag $TAG already exists locally"
git ls-remote --exit-code --tags origin "$TAG" >/dev/null 2>&1 && die "tag $TAG already exists on origin"

# --- make sure develop is current and actually has something to ship -------
step "Syncing $DEV_BRANCH"
git checkout "$DEV_BRANCH" >/dev/null 2>&1 || die "cannot checkout $DEV_BRANCH"
git pull --ff-only origin "$DEV_BRANCH"
if git rev-parse -q --verify "origin/$MAIN_BRANCH" >/dev/null; then
  AHEAD="$(git rev-list --count "origin/$MAIN_BRANCH..$DEV_BRANCH")"
  [[ "$AHEAD" -gt 0 ]] || die "$DEV_BRANCH has no commits beyond $MAIN_BRANCH — nothing to release"
  ok "$DEV_BRANCH is $AHEAD commit(s) ahead of $MAIN_BRANCH"
fi

# --- release notes (mandatory: every release must say what changed) --------
# Captured up front so the author can write them, then walk away. Sources, in order:
#   --notes-file PATH  →  prompt (interactive)  →  the commit subjects since the last tag (fallback).
# A human "Highlights" block is set as the release body; GitHub's auto-generated "What's Changed"
# changelog is appended after (the release.yml/snap.yml `generate_release_notes` is the bare-tag safety net).
NOTES_BODY=""
if [[ -n "$NOTES_FILE" ]]; then
  [[ -f "$NOTES_FILE" ]] || die "--notes-file not found: $NOTES_FILE"
  NOTES_BODY="$(cat "$NOTES_FILE")"
elif [[ "$ASSUME_YES" != "yes" ]]; then
  echo
  echo "  Write the release notes / highlights for $TAG (what changed for users)."
  echo "  Enter lines; finish with a single '.' on its own line:"
  while IFS= read -r line; do [[ "$line" == "." ]] && break; NOTES_BODY+="$line"$'\n'; done
fi
if [[ -z "${NOTES_BODY// /}" ]]; then
  warn "no notes given — falling back to commit subjects since the last tag"
  NOTES_BODY="$(git log --no-merges --pretty='- %s' "v$CUR_VERSION..$DEV_BRANCH" 2>/dev/null | head -40)"
fi
[[ -n "${NOTES_BODY// /}" ]] || die "release notes are required and could not be derived — re-run with --notes-file"

echo
echo "  Release plan:"
echo "    version : $CUR_VERSION -> $VERSION   (tag $TAG)"
echo "    merge   : $DEV_BRANCH -> $MAIN_BRANCH"
echo "    publish : GitHub Releases · Linux curl · Snap Store · Homebrew tap ($TAP_REPO) · winget ($WINGET_UPSTREAM)"
echo "    notes   : $(printf '%s' "$NOTES_BODY" | head -1)…"
echo
if [[ "$ASSUME_YES" != "yes" ]]; then
  read -r -p "Proceed? [y/N] " reply
  [[ "$reply" =~ ^[Yy]$ ]] || die "aborted"
fi

# --- 1. bump the version on develop ----------------------------------------
step "Bumping version to $VERSION on $DEV_BRANCH"
if [[ "$CUR_VERSION" != "$VERSION" ]]; then
  sedi "s|<VersionPrefix>$CUR_VERSION</VersionPrefix>|<VersionPrefix>$VERSION</VersionPrefix>|" "$CSPROJ"
  grep -q "<VersionPrefix>$VERSION</VersionPrefix>" "$CSPROJ" || die "failed to bump VersionPrefix in $CSPROJ"
  [[ -f "$SNAP_VERSION_FILE" ]] && printf '%s' "$VERSION" > "$SNAP_VERSION_FILE"
  git add "$CSPROJ" "$SNAP_VERSION_FILE"
  git commit -q -m "chore(release): bump version to $VERSION"
  git push origin "$DEV_BRANCH"
  ok "committed + pushed version bump"
else
  warn "VersionPrefix already $VERSION — no bump commit"
fi

# --- 2. merge develop -> main ----------------------------------------------
step "Merging $DEV_BRANCH -> $MAIN_BRANCH"
git checkout "$MAIN_BRANCH"
git pull --ff-only origin "$MAIN_BRANCH" || true
git merge --no-ff "$DEV_BRANCH" -m "release: $TAG (merge $DEV_BRANCH)"
git push origin "$MAIN_BRANCH"
ok "$MAIN_BRANCH updated"

# --- 3. tag + push (triggers release.yml + snap.yml) -----------------------
step "Tagging $TAG"
git tag -a "$TAG" -m "Downloader Desktop $TAG"
git push origin "$TAG"
ok "pushed $TAG — release.yml (GitHub assets + curl) and snap.yml (Snap Store) are now building"

# --- 4. wait for the GitHub Release assets ---------------------------------
step "Waiting for the Release build to attach the macOS archives"
ARM_URL="https://github.com/$REPO/releases/download/$TAG/Downloader-osx-arm64.tar.gz"
X64_URL="https://github.com/$REPO/releases/download/$TAG/Downloader-osx-x64.tar.gz"
deadline=$(( $(date +%s) + 1800 ))   # up to 30 minutes
while :; do
  have_arm="no"; have_x64="no"
  assets="$(gh release view "$TAG" --repo "$REPO" --json assets --jq '.assets[].name' 2>/dev/null || true)"
  grep -q '^Downloader-osx-arm64.tar.gz$' <<<"$assets" && have_arm="yes"
  grep -q '^Downloader-osx-x64.tar.gz$'   <<<"$assets" && have_x64="yes"
  [[ "$have_arm" == "yes" && "$have_x64" == "yes" ]] && { ok "both macOS archives are attached"; break; }
  [[ $(date +%s) -ge $deadline ]] && die "timed out waiting for macOS archives — check: gh run list --repo $REPO"
  echo "    ...still building (arm64=$have_arm x64=$have_x64); rechecking in 30s"
  sleep 30
done

# --- 4b. set the release notes (curated highlights + auto-generated changelog) ---
step "Writing release notes for $TAG"
AUTO_NOTES="$(gh api -X POST "repos/$REPO/releases/generate-notes" \
  -f tag_name="$TAG" -f previous_tag_name="v$CUR_VERSION" --jq '.body' 2>/dev/null || true)"
NOTES_TMP="$(mktemp)"
{
  printf '## Highlights\n\n%s\n' "$NOTES_BODY"
  [[ -n "$AUTO_NOTES" ]] && printf '\n%s\n' "$AUTO_NOTES"
} > "$NOTES_TMP"
if gh release edit "$TAG" --repo "$REPO" --notes-file "$NOTES_TMP" >/dev/null 2>&1; then
  ok "release notes set"
else
  warn "could not set release notes (the release may not be created yet) — set them manually: gh release edit $TAG"
fi
rm -f "$NOTES_TMP"

# --- 5. update Homebrew (tap repo + in-repo mirror) ------------------------
step "Updating Homebrew cask in $TAP_REPO"
ARM_SHA="$(curl -fsSL "$ARM_URL" | sha256_of)"; [[ -n "$ARM_SHA" ]] || die "failed to checksum arm64 archive"
X64_SHA="$(curl -fsSL "$X64_URL" | sha256_of)"; [[ -n "$X64_SHA" ]] || die "failed to checksum x64 archive"
ok "arm64 sha256 = $ARM_SHA"
ok "x64   sha256 = $X64_SHA"

# Rewrite a cask file in place: version + the two sha256 (arm appears before intel).
rewrite_cask() {
  local f="$1"
  sedi -E "s|^([[:space:]]*)version \"[^\"]+\"|\1version \"$VERSION\"|" "$f"
  # First sha256 = on_arm, second = on_intel. Replace them positionally with awk.
  awk -v arm="$ARM_SHA" -v x64="$X64_SHA" '
    /sha256 "/ { n++; if (n==1) sub(/sha256 "[^"]*"/, "sha256 \"" arm "\"");
                  else if (n==2) sub(/sha256 "[^"]*"/, "sha256 \"" x64 "\"") }
    { print }
  ' "$f" > "$f.tmp" && mv "$f.tmp" "$f"
}

# Rewrite the in-repo winget manifests for the new version (PackageVersion in all three +
# InstallerUrl/InstallerSha256 in the installer manifest). WIN_URL/WIN_SHA are set by the caller.
rewrite_winget() {
  sedi -E "s|^(PackageVersion:).*|\1 $VERSION|" "$WINGET_DIR"/*.yaml
  sedi -E "s|^([[:space:]]*InstallerUrl:).*|\1 $WIN_URL|"       "$WINGET_DIR/bezzad.Downloader.installer.yaml"
  sedi -E "s|^([[:space:]]*InstallerSha256:).*|\1 $WIN_SHA|"    "$WINGET_DIR/bezzad.Downloader.installer.yaml"
}

# Submit the manifests to microsoft/winget-pkgs as a PR (best-effort; never fails the release).
# Dedup-safe: skips if an open PR for this version already exists (avoids duplicate submissions).
submit_winget() {
  local user fork base br dir n b64 ok_all=1
  user="$(gh api user --jq .login 2>/dev/null)" || { warn "winget: gh user lookup failed — skipping PR"; return 0; }
  fork="$user/winget-pkgs"
  gh repo fork "$WINGET_UPSTREAM" --clone=false >/dev/null 2>&1 || true
  if gh api "search/issues?q=repo:$WINGET_UPSTREAM+is:pr+is:open+author:$user+bezzad.Downloader+$VERSION" \
       --jq '.items[].title' 2>/dev/null | grep -q "version $VERSION"; then
    warn "winget: an open PR for $VERSION already exists — skipping (no duplicate)"; return 0
  fi
  gh api -X POST "repos/$fork/merge-upstream" -f branch=master >/dev/null 2>&1 || true
  base="$(gh api "repos/$fork/git/refs/heads/master" --jq '.object.sha' 2>/dev/null)" \
    || { warn "winget: cannot read fork master — skipping PR"; return 0; }
  br="bezzad.Downloader-$VERSION"
  gh api -X POST "repos/$fork/git/refs" -f ref="refs/heads/$br" -f sha="$base" >/dev/null 2>&1 || true
  dir="manifests/b/bezzad/Downloader/$VERSION"
  for n in bezzad.Downloader.yaml bezzad.Downloader.installer.yaml bezzad.Downloader.locale.en-US.yaml; do
    b64="$(b64_file "$WINGET_DIR/$n")"
    gh api -X PUT "repos/$fork/contents/$dir/$n" \
      -f message="bezzad.Downloader $VERSION ($n)" -f branch="$br" -f content="$b64" >/dev/null 2>&1 || ok_all=0
  done
  [[ "$ok_all" == 1 ]] || { warn "winget: manifest upload failed — skipping PR"; return 0; }
  if gh pr create --repo "$WINGET_UPSTREAM" --head "$user:$br" --base master \
       --title "New version: bezzad.Downloader version $VERSION" \
       --body "Automated submission of bezzad.Downloader $VERSION (portable zip → \`downloader\`). Repo: https://github.com/$REPO" \
       >/dev/null 2>&1; then
    ok "winget: opened PR to $WINGET_UPSTREAM for $VERSION"
  else
    warn "winget: could not open PR (a branch/PR may already exist) — check manually"
  fi
}

TAP_DIR="$(mktemp -d)"
gh repo clone "$TAP_REPO" "$TAP_DIR" -- --quiet
rewrite_cask "$TAP_DIR/Casks/downloader.rb"
if [[ -n "$(git -C "$TAP_DIR" status --porcelain)" ]]; then
  git -C "$TAP_DIR" add Casks/downloader.rb
  git -C "$TAP_DIR" commit -q -m "downloader $VERSION"
  git -C "$TAP_DIR" push --quiet
  ok "pushed cask update to $TAP_REPO"
else
  warn "tap cask already up to date"
fi
rm -rf "$TAP_DIR"

# Sync the in-repo mirror so it matches the published cask.
step "Syncing in-repo mirror $CASK_MIRROR on $DEV_BRANCH"
git checkout "$DEV_BRANCH"
git pull --ff-only origin "$DEV_BRANCH" || true
if [[ -f "$CASK_MIRROR" ]]; then
  rewrite_cask "$CASK_MIRROR"
  if [[ -n "$(git status --porcelain "$CASK_MIRROR")" ]]; then
    git add "$CASK_MIRROR"
    git commit -q -m "chore(release): mirror cask to $VERSION"
    git push origin "$DEV_BRANCH"
    ok "mirror updated + pushed"
  else
    warn "mirror already matches"
  fi
fi

# --- 6. winget (in-repo mirror + winget-pkgs PR) ---------------------------
step "Updating winget manifests + submitting to $WINGET_UPSTREAM"
WIN_URL="https://github.com/$REPO/releases/download/$TAG/Downloader-win-x64.zip"
WIN_SHA="$(curl -fsSL "$WIN_URL" | sha256_of | tr '[:lower:]' '[:upper:]')"
if [[ -n "$WIN_SHA" && -d "$WINGET_DIR" ]]; then
  ok "win-x64 sha256 = $WIN_SHA"
  rewrite_winget
  if [[ -n "$(git status --porcelain "$WINGET_DIR")" ]]; then
    git add "$WINGET_DIR"
    git commit -q -m "chore(release): bump winget manifests to $VERSION"
    git push origin "$DEV_BRANCH"
    ok "winget mirror updated + pushed"
  else
    warn "winget mirror already matches"
  fi
  submit_winget   # best-effort, dedup-checked
else
  warn "winget: missing win-x64 sha or $WINGET_DIR — skipping"
fi

# --- done ------------------------------------------------------------------
echo
ok "Release $TAG is out."
echo
echo "  Verify:"
echo "    GitHub : gh release view $TAG --repo $REPO"
echo "    Snap   : gh run list --repo $REPO --workflow Snap   # then 'snap info downloader'"
echo "    Curl   : curl -fsSL https://raw.githubusercontent.com/$REPO/$MAIN_BRANCH/scripts/install.sh | bash"
echo "    Brew   : brew update && brew info --cask $TAP_REPO/downloader"
echo "    Winget : gh pr list --repo $WINGET_UPSTREAM --author @me   # awaits a moderator merge"
echo
echo "  Note: the Snap Store publish runs in CI (snap.yml). If the repo secret"
echo "  SNAPCRAFT_STORE_CREDENTIALS is unset, download the .snap from the run and run:"
echo "    snapcraft upload --release=stable downloader_${VERSION}_amd64.snap"
