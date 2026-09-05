## Why

v2.8.0 made interception worse for the reporter of
[issue #9](https://github.com/bezzad/Downloader.Desktop/issues/9), and their report says exactly why.

Softpedia's **External Mirror** and **ZIP** downloads worked in v2.7.0 and now fail; APKMirror is now
intercepted (the v2.8.0 type fix working) but its download then fails too. All of them fail the same way,
and the reporter spotted the tell: *"the Source URL shown in these failed tasks is the webpage URL, rather
than the actual download URL."*

That is this project's own change, and the reasoning behind it was wrong on one point. v2.8.0 started
handing the app the link the browser was **asked** to fetch as the download's primary URL, keeping the
redirect chain's end as a **mirror** — on the belief that the mirror would be tried if the primary failed.
It is not: mirrors in the engine are load spreading, not failover. Each chunk is pinned to one request
instance and the file-info probe only ever reads the first URL. So on every site where the clicked link is
a page or a handler rather than the file itself, the app now re-requests a page, fails, and the second URL
— the one that would have worked, and did work in v2.7.0 — is never tried.

The same report also explains the **original** Secure Mirror failure, which was never an expired link at
all. Given the direct download URL by hand, that mirror succeeds at 1, 2 or 3 connections and returns 403
at 4 or more, reproducibly. Nine other download managers succeed against the same URL with default maxima
of 6–8, because they do not apply a global maximum to every download as a quota. Ours does, and then
reports the resulting 403 as *"This link is no longer valid — it expired or was withdrawn"*, which sends
the user hunting for a fresh link they never needed.

## What Changes

- **A download with more than one URL actually falls back.** When the primary URL fails in a way that a
  different address could plausibly fix, the next URL is promoted and the attempt is retried, once per
  URL. This is what the v2.8.0 hand-off already assumed existed, and it makes the app strictly better than
  either version: whichever of the two links the site prefers, the download works.
- **The hand-off leads with the link most likely to be fetchable** — the end of the browser's redirect
  chain, as v2.7.0 did and as the reporter confirms works — while the clicked link travels as the
  fallback and as the address the expired-link recovery re-resolves. The ordering stops being a bet: with
  failover in place, being wrong about it costs one request instead of the download.
- **A server that refuses concurrency gets fewer connections instead of a wrong error.** A failure that
  looks like a concurrency refusal (403 while several chunks are in flight) is retried once with a single
  connection before the download is failed, and it is never described as an expired link. The global
  maximum becomes a ceiling, not a quota every download must spend.
- **The failure messages tell the two cases apart** — a link that is genuinely gone, and a server that
  refused this particular request — because the user's next action differs.

## Capabilities

### Modified Capabilities
- `browser-download-interception`: the intercepted hand-off leads with the link the app is most likely to
  be able to fetch, and every URL it hands over is actually attempted.
- `link-refresh`: a download with fallback URLs tries them before it is failed; a server that refuses
  concurrent requests is retried with one connection; and a refusal that is not an expired link is not
  reported as one.

## Impact

- `src/browser-extension/background.js` (which URL leads the hand-off), `common.js` if the choice moves
  into a tested pure helper, and `common.test.js`.
- `src/Downloader.Desktop/Services/DownloadManager.cs` — failover across `DownloadItem.Urls`, the
  concurrency-refusal retry, and the failure wording; `Models/DownloadSettings.cs` only if the retry needs
  a per-attempt chunk count.
- `Assets/i18n/*.json` (16 packs) for any new message.
- Tests: `src/Downloader.Desktop.Tests/{Unit,Integration}/`, `src/browser-extension/common.test.js`, and
  the Playwright suite.
- Cannot be verified from this machine against the real sites (Softpedia and APKMirror both answer it with
  a Cloudflare challenge), so every behaviour here ships with a loopback or unit test that reproduces the
  reported shape, and the live confirmation comes from the reporter.
