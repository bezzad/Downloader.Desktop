// Content script: tracks which <video>/<audio> element the user is most likely looking at and
// reports it to the background worker, which correlates that timing with sniffed network
// requests to split "Main media" from "Other detected" in the popup (see background.js's
// activeHint + common.js's computeMainGroups). Best-effort signal only — a wrong guess just fails
// safe into "Other detected," never hides anything.
//
// Real-world fix: v1.2.0 only ever considered a CURRENTLY PLAYING element. On sites like X.com an
// inline video autoplays once and is left paused on its last frame — a common, expected state,
// not an edge case — so the old logic never promoted it and "Main media" stayed empty even while
// the user was looking straight at the video. A large, visible, already-loaded element now
// qualifies too (just ranked below an actively playing one).
(() => {
  const api = globalThis.browser || globalThis.chrome;
  const tracked = new Map(); // element -> { visible }
  let lastSentAt = 0;
  const THROTTLE_MS = 1500; // keeps the hint fresher than background's ~3s correlation window

  function mostRelevant() {
    let bestPlaying = null, bestPlayingArea = 0;
    let bestLoaded = null, bestLoadedArea = 0;
    for (const [el, info] of tracked) {
      if (!info.visible) continue;
      const rect = el.getBoundingClientRect();
      const area = rect.width * rect.height;
      if (!el.paused) {
        if (area > bestPlayingArea) { bestPlayingArea = area; bestPlaying = el; }
      } else if (el.readyState >= 1 || el.currentTime > 0) {
        // Paused but has loaded metadata / already started — e.g. an autoplay-once video left on
        // its last frame. Still a strong "this is the content" signal, just not as strong as
        // actively playing.
        if (area > bestLoadedArea) { bestLoadedArea = area; bestLoaded = el; }
      }
    }
    return bestPlaying || bestLoaded;
  }

  function report() {
    const now = Date.now();
    if (now - lastSentAt < THROTTLE_MS) return;
    if (!mostRelevant()) return;
    lastSentAt = now;
    try {
      api.runtime.sendMessage({ type: "activeMediaHint" });
    } catch {
      // Extension context can be gone mid-navigation — nothing to do.
    }
  }

  const io = new IntersectionObserver(entries => {
    for (const entry of entries)
      tracked.set(entry.target, { visible: entry.isIntersecting && entry.intersectionRatio > 0.5 });
    report();
  }, { threshold: [0, 0.5, 1] });

  function observe(el) {
    if (tracked.has(el)) return;
    tracked.set(el, { visible: false });
    io.observe(el);
    el.addEventListener("play", report);
    el.addEventListener("pause", report);
    el.addEventListener("timeupdate", report);
  }

  function scan() {
    document.querySelectorAll("video, audio").forEach(observe);
  }

  scan();
  // Feed/SPA pages (e.g. a social timeline) insert players as the user scrolls — keep watching
  // for new ones. Cheap: a childList mutation observer with no video/audio ever added costs
  // essentially nothing, so pages with no media at all are effectively a no-op beyond this.
  new MutationObserver(scan).observe(document.documentElement, { childList: true, subtree: true });

  // A statically visible, already-loaded-but-paused video never fires another play/pause/
  // timeupdate event, so without this the hint would go stale the moment autoplay finishes —
  // exactly the X.com scenario above. Re-check periodically so the hint stays fresh the whole
  // time something still qualifies (report() itself no-ops within THROTTLE_MS).
  setInterval(report, 2000);
})();
