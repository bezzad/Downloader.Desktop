// Background service worker / event page.
// - Adds "Download with Downloader" context menus.
// - Sniffs video/audio/HLS responses per tab and badges the toolbar icon.
// - Forwards captured URLs to the desktop app's local listener.
// Chrome loads shared helpers via importScripts (service worker); Firefox loads common.js first
// through the manifest "scripts" array, so importScripts is absent there — guard it.
if (typeof importScripts === "function") importScripts("common.js");

// tabId -> Map(url -> { url, type, group, capturedAt })   detected media for the current page
const tabMedia = new Map();

// tabId -> { atMs } — latest "user is looking at this" signal from content.js. Refreshed
// continuously while a video plays or sits paused-but-visible (content.js re-sends on
// play/pause/timeupdate and a periodic re-check, throttled), so its freshness window naturally
// covers the whole time the content stays on screen, not just the start.
const activeHint = new Map();

// ---------------- Context menus ----------------
api.runtime.onInstalled.addListener(() => {
  api.contextMenus.removeAll(() => {
    api.contextMenus.create({
      id: "dl-link",
      title: "Download with Downloader",
      contexts: ["link", "video", "audio", "image"]
    });
    api.contextMenus.create({
      id: "dl-selection",
      title: "Download selected links with Downloader",
      contexts: ["selection"]
    });
    api.contextMenus.create({
      id: "dl-page",
      title: "Capture media on this page",
      contexts: ["page"]
    });
  });
});

api.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === "dl-link") {
    const url = info.linkUrl || info.srcUrl;
    if (url) await capture(url);
  } else if (info.menuItemId === "dl-selection") {
    for (const url of extractUrls(info.selectionText)) await capture(url);
  } else if (info.menuItemId === "dl-page") {
    if (tab?.id != null) api.action.openPopup?.();
  }
});

function extractUrls(text) {
  if (!text) return [];
  return (text.match(/https?:\/\/[^\s"'<>]+/g) || []);
}

async function capture(url) {
  const ok = await sendToApp(url);
  notify(ok ? "Sent to Downloader" : "Downloader app not reachable",
         ok ? url : "Is the desktop app running with browser integration enabled?");
}

function notify(title, message) {
  try {
    api.notifications?.create({
      type: "basic",
      iconUrl: "icons/icon128.png",
      title,
      message: (message || "").slice(0, 200)
    });
  } catch { /* notifications are optional */ }
}

// ---------------- Media sniffing ----------------
api.webRequest.onHeadersReceived.addListener(
  details => {
    const ct = (details.responseHeaders || [])
      .find(h => h.name.toLowerCase() === "content-type")?.value;
    if (details.tabId < 0) return;
    if (looksLikeMedia(details.url) || isMediaContentType(ct)) {
      addMedia(details.tabId, details.url, ct);
    }
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders"]
);

function addMedia(tabId, url, type) {
  if (!isHttp(url)) return;
  // Skip obvious non-downloadable streaming (blob:, DRM) — already filtered by isHttp.
  let map = tabMedia.get(tabId);
  if (!map) { map = new Map(); tabMedia.set(tabId, map); }
  if (map.has(url)) return;
  map.set(url, { url, type: type || extOf(url), group: groupKey(url), capturedAt: Date.now() });
  updateBadge(tabId, map.size);
}

// Runs the size/resolution probes for every item currently known for a tab. Kept separate from
// getMedia so the popup can render the plain list immediately, then request this and upgrade rows
// in place (see design.md Decision 1/6).
async function probeMediaForTab(tabId) {
  const map = tabMedia.get(tabId);
  if (!map) return [];
  const items = [...map.values()];
  const tasks = items.map(item => async signal => {
    if (extOf(item.url) === "m3u8") {
      const variants = await parseHlsMaster(item.url, { signal });
      if (variants.length === 0)
        return { url: item.url, kind: "direct", size: await probeSize(item.url, { signal }) };
      const sized = await Promise.all(variants.map(async v => {
        const est = await estimateHlsSize(v.uri, { signal });
        return { ...v, size: est.size, segmentUrls: est.segmentUrls };
      }));
      return { url: item.url, kind: "hls", variants: sized };
    }
    return { url: item.url, kind: "direct", size: await probeSize(item.url, { signal }) };
  });
  return runProbesBounded(tasks, { concurrency: 4, timeoutMs: 2500 });
}

function updateBadge(tabId, count) {
  try {
    api.action.setBadgeBackgroundColor({ color: "#0E8FB3" });
    api.action.setBadgeText({ tabId, text: count > 0 ? String(count) : "" });
  } catch { /* badge is optional */ }
}

// Reset a tab's findings when it navigates.
api.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.status === "loading" && changeInfo.url) {
    tabMedia.delete(tabId);
    activeHint.delete(tabId);
    updateBadge(tabId, 0);
  }
});
api.tabs.onRemoved.addListener(tabId => {
  tabMedia.delete(tabId);
  activeHint.delete(tabId);
});

// ---------------- Messages from the popup + content script ----------------
api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    if (msg.type === "getMedia") {
      const map = tabMedia.get(msg.tabId);
      const items = map ? [...map.values()] : [];
      const mainGroups = computeMainGroups(items, activeHint.get(msg.tabId), Date.now());
      sendResponse({ media: items.map(item => ({ ...item, main: mainGroups.has(item.group) })) });
    } else if (msg.type === "probeMedia") {
      sendResponse({ results: await probeMediaForTab(msg.tabId) });
    } else if (msg.type === "activeMediaHint") {
      // Sent by content.js — the tab id comes from the content script's own sender context.
      const tabId = sender.tab?.id;
      if (tabId != null) activeHint.set(tabId, { atMs: Date.now() });
      sendResponse({});
    } else if (msg.type === "send") {
      sendResponse({ ok: await sendToApp(msg.url, msg.filename) });
    } else if (msg.type === "ping") {
      sendResponse({ ok: await pingApp() });
    } else {
      sendResponse({});
    }
  })();
  return true; // keep the channel open for the async response
});
