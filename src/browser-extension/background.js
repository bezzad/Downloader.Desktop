// Background service worker / event page.
// - Adds "Download with Downloader" context menus.
// - Sniffs video/audio/HLS responses per tab and badges the toolbar icon.
// - Forwards captured URLs to the desktop app's local listener.
// Chrome loads shared helpers via importScripts (service worker); Firefox loads common.js first
// through the manifest "scripts" array, so importScripts is absent there — guard it.
if (typeof importScripts === "function") importScripts("common.js");

// tabId -> Map(url -> { url, type })   detected media for the current page
const tabMedia = new Map();

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
  map.set(url, { url, type: type || extOf(url) });
  updateBadge(tabId, map.size);
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
    updateBadge(tabId, 0);
  }
});
api.tabs.onRemoved.addListener(tabId => tabMedia.delete(tabId));

// ---------------- Messages from the popup ----------------
api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    if (msg.type === "getMedia") {
      const map = tabMedia.get(msg.tabId);
      sendResponse({ media: map ? [...map.values()] : [] });
    } else if (msg.type === "send") {
      sendResponse({ ok: await sendToApp(msg.url) });
    } else if (msg.type === "ping") {
      sendResponse({ ok: await pingApp() });
    } else {
      sendResponse({});
    }
  })();
  return true; // keep the channel open for the async response
});
