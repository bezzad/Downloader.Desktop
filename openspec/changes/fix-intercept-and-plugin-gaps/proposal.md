## Why

The reporter on [issue #9](https://github.com/bezzad/Downloader.Desktop/issues/9) retested v2.7.0 and found
two of the three interception cases still broken (APKPure never intercepts; Softpedia's "Secure Download"
is intercepted but the app cannot fetch the link), plus a claim that the extension only found the app after
upgrading it — which the code says it should never have needed. In the same round the author raised four
further gaps that are unrelated to interception but block real use: YouTube is invisible to the extension
and unusable by hand, HuggingFace model links are not claimed by the Ollama plugin, and the "Add to Ollama"
offer no longer appears after a model download finishes.

Two of these are already diagnosed to a specific line and reproduced locally, so the cost of fixing them is
known and small; the rest need a reproduction before a fix, which is itself part of this change.

## What Changes

**Interception (issue #9 follow-up)**

- **APKPure is never intercepted because a package name is parsed as a file extension.** For
  `https://d.apkpure.com/b/XAPK/com.instagram.android?version=latest`, `resolveDownloadExt` returns
  `"android"` — the last dot of the *path segment*, which is a package name, not a file name. That bogus
  value both fails the allow-list and, being non-empty, short-circuits the `||` chain so the MIME source is
  never consulted — even when the server correctly sends `application/vnd.android.package-archive`. Adding
  `xapk` to the type list in v2.7.0 could not have helped for this reason. Fix: judge the download on *all*
  the candidate names it has, not the first non-empty one, and reject an implausible path "extension".
- **The real file name is read from the response headers.** `Content-Disposition` is where APKPure, and
  every other signed-CDN download, actually names the file, and no MIME type identifies `.xapk` at all. The
  extension already listens on `webRequest.onHeadersReceived` for `<all_urls>`, so the headers are cached
  there and consulted at `downloads.onCreated`. **No new permission**, so no extra store review.
- **An intercepted download hands over the link the browser was asked to fetch, not the one it ended on.**
  Today the extension sends `item.finalUrl` — the signed, frequently single-use end of the redirect chain
  that the browser has already spent. The app re-requesting it gets 401/403/410 and shows "This link is no
  longer valid". The original URL is re-resolvable (that is exactly what `DownloadManager.Start` relies on
  for issue #6), so it becomes the primary URL and `finalUrl` is passed as a **mirror** — `/api/add`
  already accepts `mirrors`.
- **A first-attempt expired-link failure on an extension hand-off gets one automatic retry.** Auto-refresh
  is currently gated on `Downloaded > 0`, which a link that fails on its very first request never satisfies.
- **The failure the user sees is honest.** When the browser is still downloading the file (the v2.7.0 safety
  net working as designed), the app must not show a red "link expired — paste a fresh one" banner for a
  download the user has not lost.
- **The extension says when it cannot find the app**, naming the ports it probed, instead of silently doing
  nothing — the diagnostic that would have answered the reporter's fourth point in one screenshot.

**YouTube**

- A new **optional** plugin (catalog tier, installed deliberately from Settings → Plugins, sha256-verified
  before load) extracts YouTube and other site links via yt-dlp. Cookies come only from our own browser
  extension — never from the browser's profile — preserving the issue #4 rules. The main app still spawns
  no third-party binary.
- The extension stops declaring YouTube unconditionally unsupported: with the plugin installed and
  interception on, a YouTube page offers its video to the app, carrying the signed-in session's cookies.
  Without the plugin the popup keeps today's explanatory state, and the misleading "you must be signed in
  in the browser" wording is replaced by one that names the real cause.

**Ollama plugin**

- HuggingFace model repos (`https://huggingface.co/<owner>/<repo>`) are claimed: the repo's GGUF files are
  listed, offered as selectable variants (quantisations), downloaded, and installable into the local Ollama
  store by the same explicit "Add to Ollama" action.
- The lost "Add to Ollama" offer after a completed model download is reproduced and fixed.

Every item above ships with tests — unit for the pure decisions, headless/UI or Playwright e2e for the
paths a user actually walks. A task is not done without them.

## Capabilities

### New Capabilities
- `site-media-extraction`: an optional, explicitly installed plugin that turns a site page URL (YouTube and
  the like) into downloadable media, using cookies supplied by our extension and never read from a browser
  profile.
- `huggingface-model-download`: claiming HuggingFace model repos, choosing a GGUF variant, downloading it
  and adding it to the local Ollama store.

### Modified Capabilities
- `browser-download-interception`: the file type of a download is decided from every source that names it
  (including response headers), never from a single first-hit source; an intercepted hand-off carries the
  original URL as primary and the final URL as a mirror; the extension reports an unreachable app.
- `link-refresh`: an expired-link failure on the first attempt of an extension hand-off is retried once,
  and the "paste a fresh link" wording is not shown when the browser still holds the file.
- `extension-media-relevance`: the known-unsupported state is conditional on whether the app can actually
  handle the site, not hard-coded.
- `ollama-model-download`: the post-download "Add to Ollama" offer is guaranteed to appear on a completed
  model download.

## Impact

- `src/browser-extension/`: `common.js` (`resolveDownloadExt`, `shouldIntercept`, hand-off body),
  `background.js` (header cache, `onDownloadCreated`), `popup.js` (app-unreachable state), `manifest.json`
  version bump. No permission changes.
- `src/Downloader.Desktop/Services/DownloadManager.cs` (auto-refresh gate, failure wording),
  `LocalApiService.cs` (hand-off fields, if the mirror path needs it), `Assets/i18n/*.json` (16 packs).
- `src/Downloader.Desktop.Plugins/Downloader.Desktop.Plugins.Ollama/` (HuggingFace resolver, offer fix) and
  a new optional plugin project for site extraction, plus `packaging/plugins/optional-plugins.json` and
  `scripts/build-plugins.sh`.
- Tests: `src/Downloader.Desktop.Tests/{Unit,Plugins,UI}/`, `src/browser-extension/common.test.js`, and the
  Playwright suite in `src/browser-extension/e2e/`.
- Two items (Softpedia's secure mirror, the reporter's app-detection claim) cannot be reproduced from this
  machine — Softpedia and APKPure both answer a datacenter IP with a Cloudflare challenge — so they close on
  the reporter's confirmation, not ours.
