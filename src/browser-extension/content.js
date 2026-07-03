// Content script: tracks which <video>/<audio> element the user is most likely looking at (the
// largest one that is both visible and playing) and reports it to the background worker, which
// correlates that timing with sniffed network requests to split "Main media" from "Other detected"
// in the popup (see background.js's activeHint + design.md Decisions 8-9). Best-effort signal
// only — a wrong guess just fails safe into "Other detected," never hides anything.
(() => {
  const api = globalThis.browser || globalThis.chrome;
  const tracked = new Map(); // element -> { visible }
  let lastSentAt = 0;
  const THROTTLE_MS = 1500; // keeps the hint fresher than background's ~3s correlation window

  function report() {
    const now = Date.now();
    if (now - lastSentAt < THROTTLE_MS) return;
    let best = null, bestArea = 0;
    for (const [el, info] of tracked) {
      if (el.paused || !info.visible) continue;
      const rect = el.getBoundingClientRect();
      const area = rect.width * rect.height;
      if (area > bestArea) { bestArea = area; best = el; }
    }
    if (!best) return;
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
})();
