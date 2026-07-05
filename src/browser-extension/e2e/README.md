# Extension end-to-end tests

Loads the **real unpacked extension** (`src/browser-extension`) into an actual Chromium browser
via Playwright and drives real pages — the only way to catch bugs that only show up with a real
`webRequest` sniffer, a real service worker, and a real DOM (the plain `node --test` suite in
`../common.test.js` only covers pure logic, mocked `fetch`).

These tests exist because manual testing on real sites (x.com, youtube.com) found real bugs that
unit tests couldn't: the relevance heuristic never promoting a paused-after-autoplay video, the
known-unsupported-site message being masked by incidental detections, and HLS variant
playlists/segments doubling up as redundant top-level cards. Each bug has a regression test here.

## Setup (one-time)

```bash
cd src/browser-extension/e2e
npm install
npx playwright install chromium
```

## Run

```bash
npm test
```

This starts a local static file server (`server.js`, serves `fixtures/`) automatically, launches
Chromium **headed** (`headless: false` — MV3 extensions need a real browser session; on a
display-less Linux CI box, wrap with `xvfb-run npm test`), and runs every `tests/*.spec.js`.

## How it works

- `fixtures.js` launches a persistent Chromium context with `--load-extension` pointed at
  `src/browser-extension`, and exposes the extension id (parsed from its service worker URL — the
  standard way to identify an MV3 extension in Playwright, since there's no public API to trigger
  a real toolbar-icon click).
- **Cold-start quirk (test-harness only):** a freshly launched profile's extension `webRequest`
  listener isn't reliably wired up until *after* the very first navigation — confirmed by direct
  reproduction, it silently misses every request on that first navigation regardless of
  destination. `fixtures.js`'s `context` fixture does a throwaway `about:blank` warm-up navigation
  before returning control, so every test's real navigation is the "second" one. This never
  affects real users — their extension has been running for a while before they visit any page.
- **Testing the popup's `activeTab()` dependency:** the popup's `getMedia` call needs to know
  which tab to inspect. A real toolbar popup gets this from `chrome.tabs.query({active:true})`,
  but there's no public Playwright API to click the toolbar icon — the only way to load
  `popup.html` is as a normal tab, which then makes *itself* "active" instead of the page under
  test. `popup.js` has a tiny, harmless test-only override: a `?__testTabId=` query param it reads
  before falling back to the real `chrome.tabs.query` call. A real toolbar popup URL never carries
  that param, so normal usage is completely unaffected. `openPopupFor()` in `fixtures.js` resolves
  the target page's real tab id (via the service worker's own `chrome.tabs.query`) and passes it.
- `fixtures/*.html` + `server.js` (plain Node `http`, Range-request support) serve real, valid
  media: `main_video.mp4` and real HLS output (`master.m3u8` + `low/`/`high/` variants and
  segments), all generated with `ffmpeg` — a fake/garbage "video" file won't fire real
  `play`/`readyState` events, which the relevance tests depend on. Regenerate with:
  ```bash
  cd fixtures
  ffmpeg -y -f lavfi -i testsrc=size=320x240:rate=10:duration=2 -pix_fmt yuv420p -movflags +faststart main_video.mp4
  ffmpeg -y -f lavfi -i testsrc=size=320x240:rate=10:duration=2 -pix_fmt yuv420p -c:v libx264 -hls_time 2 -hls_playlist_type vod -hls_segment_filename low/seg%d.ts low/index.m3u8
  ffmpeg -y -f lavfi -i testsrc=size=640x480:rate=10:duration=2 -pix_fmt yuv420p -c:v libx264 -hls_time 2 -hls_playlist_type vod -hls_segment_filename high/seg%d.ts high/index.m3u8
  ```
- `youtube.com` in `unsupported-site.spec.js` is mocked entirely at the network layer
  (`context.route(...)`) — no real request ever leaves the machine, so the test is deterministic
  and doesn't depend on YouTube's actual page or network availability.
