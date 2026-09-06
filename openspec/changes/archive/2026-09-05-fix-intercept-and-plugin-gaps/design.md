## Context

See `proposal.md` — Why. Facts established while diagnosing, which the implementation should not re-derive:

- **The APKPure bug is reproduced.** Running the shipped `common.js` in Node:
  `d.apkpure.com/b/XAPK/com.instagram.android?version=latest` → `resolveDownloadExt` = `"android"`,
  decision `{intercept: false, reason: "type-not-allowed"}`; `d.apkpure.com/b/APK/com.whatsapp` →
  `"whatsapp"`. `resolveDownloadExt` (`common.js:734`) is a four-term `||` chain, so the first non-empty
  answer wins — and `extOf` happily returns the tail of a dotted *package name* in the path. The correct
  MIME (`application/vnd.android.package-archive`, already in `MIME_EXTENSIONS`) is never consulted.
- **The hand-off sends the spent link.** `background.js:121` — `const url = item.finalUrl || item.url`.
  The app then re-requests a signed single-use address, gets 401/403/410, and
  `DownloadManager.LooksLikeExpiredLinkError` maps it to `Error_LinkExpiredRefresh`.
  `TryAutoRefreshLink` declines because `vm.GetItem().Downloaded <= 0`.
- **`/api/add` already accepts `mirrors`** (`LocalApiService.cs:402-403` merges them into `DownloadItem.Urls`),
  and `DownloadManager.Start` always re-resolves `Urls[0]`, which is the whole mechanism issue #6 relies on.
  Nothing new is needed on the app side to accept a primary + fallback pair.
- **Nothing in extension 1.5.0 requires app 2.7.0.** `/ping` has existed since 2.5.0 and the `id` field in
  the `/api/add` response since v1.6.0; the app diff v2.6.1→v2.7.0 touches only `UpdateService`/`UpdateFlow`.
  The reporter's fourth point is therefore an environment/diagnosis problem, not a compatibility one.
- **Softpedia and APKPure both answer this machine with a Cloudflare challenge (403)**, so neither can be
  end-to-end verified here. The fixes must be verifiable by unit/e2e tests against recorded URL shapes, and
  the live confirmation comes from the reporter.
- **yt-dlp was deliberately removed in HLS 2.0.0** ("no third-party executables, no browser-cookie
  reading"), following the Bitdefender quarantine (issue #4). Any YouTube work must not walk that back for
  the main app.
- **The Ollama offer path**: `DownloadManager.OfferPostDownloadAction` → `PostDownloadActionLabel` →
  `PluginManager.FindPostDownloadAction(item.ResolverPluginId, item.Url, item.FilePath)` →
  `AddToOllamaAction.CanOffer`, which requires `OllamaModelRef.TryParse(sourceUrl)` and `File.Exists(path)`.
  It is called from three places (`DownloadManager.cs:1359`, `Plans.cs:104`, `Transfers.cs:60`). The
  regression is somewhere on that chain and must be **reproduced by a failing test first**.

## Goals / Non-Goals

**Goals:**
- Fix the two interception defects with pure, unit-testable decision logic, and prove them with the exact
  URL shapes from issue #9.
- Keep every fix inside the existing permission set of the extension, so no store re-review is triggered by
  a permission change.
- Give YouTube a real path without the main app running third-party binaries or reading browser data.
- Make the Ollama plugin useful for the model source people actually browse (HuggingFace) and restore its
  install offer.

**Non-Goals:**
- Guessing at Softpedia's secure-mirror mechanics beyond the one change we can justify (send the resolvable
  link, keep the signed one as a fallback). If it still fails, the outcome is a clear message, not a
  workaround for one site.
- Site extraction beyond what the chosen tool supports; no DRM, no per-site scrapers of our own.
- Changing the main app's plugin tiering or the "no shell spawn" rules.

## Decisions

### 1. Decide the type from all candidates, not the first non-empty one

`resolveDownloadExt` keeps existing as "the single best name for display", but the interception decision
moves to a `candidateExts(item)` that returns every type the sources name, in confidence order
(browser filename → response content-disposition → URL-query content-disposition → URL path → MIME).
`shouldIntercept` then matches the user's list against the whole set: allow-mode intercepts if **any**
candidate is listed; deny-mode declines if **any** candidate is listed (the safe direction for both).

*Why:* it fixes the class of bug, not the instance. A single lookup order can always be poisoned by a
source that is confidently wrong; a set cannot.

*Alternative rejected:* just reordering MIME above path. It fixes APK and breaks the next site where the
path is right and the MIME is `application/octet-stream`.

### 2. Reject implausible path "extensions"

`extOf` gains a plausibility filter for the *path* source only: 1–8 characters, letters/digits only, and
not a known TLD-ish/host-ish token (`com`, `org`, `io`, `android`, `net`…). A path segment failing it
contributes no candidate.

*Why:* `com.instagram.android` and `example.co.uk` are not file names. Keeping the filter on the path
source alone means a real `Content-Disposition: filename=x.somethingweird` is still honoured.

### 3. Read the response headers we are already receiving

`background.js` adds a small LRU cache (say 200 entries, ~2 minutes) filled from the existing
`webRequest.onHeadersReceived` listener: for any response carrying `Content-Disposition` or a non-HTML
`Content-Type`, store `{filename, contentType}` keyed by URL. `onDownloadCreated` looks up
`item.finalUrl` then `item.url` and feeds the result into the decision.

*Why:* the header is where APKPure/XAPK actually names the file, no MIME identifies `.xapk`, and the
listener and `<all_urls>` permission already exist — **no manifest change, no new review surface**.

*Alternative rejected:* `downloads.onDeterminingFilename`. Already documented in `background.js:199-215` as
a scarce single-listener slot that does not fire under CDP, i.e. untestable in our Playwright suite.

### 4. Hand over the clicked link as primary, the final link as a mirror

`onDownloadCreated` sends `{ url: item.url, mirrors: [item.finalUrl] }` when the two differ. Cookies are
captured for the primary link's host (as the browser would send them).

*Why:* the app's whole expired-link recovery is built on re-resolving the original URL; handing it the end
of the chain removes the only thing that can recover. The signed address is still tried as a fallback, so a
chain that genuinely cannot be walked twice is no worse off than today.

*Risk accepted:* on a site where the clicked link is a one-shot redirector, the primary now fails and the
mirror carries it — one extra request, same outcome.

### 5. One retry from zero bytes, but only for extension hand-offs

`DownloadItem` gains a flag meaning "this came from the extension" (set by `/api/add` when the request
carried extension context). `TryAutoRefreshLink` allows one attempt with `Downloaded == 0` when that flag
is set, still bounded by `MaxAutoLinkRefreshAttempts`.

*Why:* the existing `Downloaded > 0` gate encodes "a link that never worked is a bad link" — true for a
pasted link, false for one the browser was fetching a second ago.

### 6. Failure wording depends on whether the user still has the file

The extension already knows: when `confirmAppFetching` fails it keeps the browser's download. The app,
however, shows its own row as Failed with "paste a fresh link". Fix on the app side with a distinct message
for a download flagged as an extension hand-off that failed on its first attempt, and on the extension side
by keeping today's notification. New i18n key across all 16 packs.

### 7. YouTube: an optional plugin, cookies only from our extension

Chosen by the author over "just fix the messaging" and over "extract inside the extension". A new optional
catalog plugin (`com.bezzad.site-media`) owns the third-party tool, downloaded and sha256-verified on first
use exactly as `FfmpegBinary`/`BinaryFile` already do, started from an absolute path, never via a shell.
Cookies arrive through the existing `ResolveOptions` cookie-file path (`CookieFile.cs`), which is fed by the
extension's `/api/add` context — never `--cookies-from-browser`.

The extension side stops hard-coding `KNOWN_UNSUPPORTED_HOSTS` as the final word: it asks the app what it
can handle (a small `/api/capabilities`-style answer, or simply "does any resolver claim this URL") and
shows the unsupported state only when the answer is no.

*Why this shape:* it keeps the app's own binary clean (issue #4), makes the capability an explicit user
choice with a verified download, and reuses the plugin isolation the repo already enforces with
`PluginIsolationTests` and the `release.yml` publish grep.

*Alternative rejected:* extracting stream URLs inside the extension. Fragile against YouTube's changes and
a likely store-policy problem.

### 8. HuggingFace inside the existing Ollama plugin

A second resolver in `Downloader.Desktop.Plugins.Ollama` rather than a new plugin: the destination
("Add to Ollama") and the whole store-writing machinery in `OllamaInstaller` are already there, and a user
who wants HF GGUF models is by definition an Ollama user. Repo files come from HuggingFace's public API
(`/api/models/<owner>/<repo>`), GGUF files become link variants (the mechanism `link-variants` already
provides for tags), and the install writes a manifest for a locally-named model (`hf.co/<owner>/<repo>:<quant>`)
pointing at the downloaded blob. Integrity is checked against the size/oid HF publishes for the file, since
there is no Ollama manifest to check against.

*Bump the plugin's csproj `<Version>` in the same session* — standing rule; the catalog update check is the
only way users receive it.

### 9. The Ollama regression is fixed test-first

Write the failing test before touching code: a completed download whose `ResolverPluginId` is the Ollama
plugin must expose `PostDownloadActionLabel` and raise the offer, through each of the three completion
routes. The prime suspects, in order: `ResolverPluginId` not being persisted/restored on the row that
completes; `item.Url` no longer being the ollama.com reference by the time `CanOffer` sees it; the
plan-runner route completing without reaching `OfferPostDownloadAction`. Confirm which before fixing.

## Risks / Trade-offs

- **Softpedia may still fail after decision 4** → the user no longer loses the file and the message is
  honest; the issue can then be closed as "the site's mirror is single-use per session" with evidence.
- **A header cache adds per-response work to a listener on `<all_urls>`** → store only what is needed, cap
  the cache, and skip responses with no `Content-Disposition` and an HTML content type.
- **Decision 1 widens what gets intercepted** (any candidate matching is enough) → the size floor, the site
  exclusions and the off-by-default switch are unchanged, and every new candidate source is a *name the
  server itself gave the file*. Tests pin the negative cases too.
- **The site-extraction plugin re-introduces a third-party executable** → contained in an optional,
  user-installed, checksum-verified plugin; `NoShellSpawnTests` continues to guard the app and must be
  extended to cover the new plugin's source rather than exempt it.
- **HuggingFace repos are large and inconsistently laid out** (sharded GGUF, subdirectories) → v1 handles
  single-file GGUF and names sharded sets as unsupported with a clear message, rather than half-working.
- **This change is large.** It is four independent tracks; they are ordered in `tasks.md` so each is
  shippable on its own, and the issue-#9 track ships first.

## Migration Plan

No data migration. The extension's stored settings keep their shape (`INTERCEPT_DEFAULTS.version` stays 1);
the added `DownloadItem` flag defaults to false for existing records, which reproduces today's behaviour.
The site-extraction plugin is absent until installed, so a rollback is uninstalling it. Ship the
interception fixes as an extension release plus an app patch; the two YouTube/HF tracks may follow in a
later version without blocking it.

## Open Questions

- Which extraction tool version to pin, and where its checksums come from, is settled at implementation
  time from the tool's own release feed — it does not change the specs or the task breakdown.
- Whether the app should expose "can you handle this URL?" as a dedicated endpoint or reuse the existing
  resolve path is an implementation detail of task 3.4; both satisfy the spec.
