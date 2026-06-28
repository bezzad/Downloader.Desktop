// Background service worker / event page.
// - Adds "Download with Downloader" context menus.
// - Sniffs video/audio/HLS responses per tab and badges the toolbar icon.
// - Forwards captured URLs to the desktop app's local listener.
// Chrome loads shared helpers via importScripts (service worker); Firefox loads common.js first
// through the manifest "scripts" array, so importScripts is absent there — guard it.
if (typeof importScripts === "function") importScripts("common.js");

// tabId -> { items: Map(url -> {url,type,kind,label}), variants: Set(url), seen: Set(url) }
// `items` are the surfaced candidates; `variants` are HLS variant playlists to suppress (so each video
// collapses to its single master); `seen` guards against re-classifying the same playlist.
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
function tabState(tabId) {
  let st = tabMedia.get(tabId);
  if (!st) { st = { items: new Map(), variants: new Set(), seen: new Set() }; tabMedia.set(tabId, st); }
  return st;
}

api.webRequest.onHeadersReceived.addListener(
  details => {
    if (details.tabId < 0) return;            // ignore our own (tab-less) classification fetches
    const url = details.url;
    if (!isHttp(url)) return;
    if (isHlsSegment(url)) return;            // drop .ts/.m4s/init fragments — the main source of noise

    const ct = (details.responseHeaders || [])
      .find(h => h.name.toLowerCase() === "content-type")?.value;
    const isHls = isM3u8(url) || (ct || "").toLowerCase().includes("mpegurl");

    if (isHls) {
      handleHls(details.tabId, url);
    } else if (looksLikeMedia(url) || isMediaContentType(ct)) {
      addCandidate(details.tabId, { url, type: ct || extOf(url), kind: "file", label: fileLabel(url, ct) });
    }
  },
  { urls: ["<all_urls>"] },
  ["responseHeaders"]
);

// Classify an HLS playlist once. A MASTER becomes a single "video" candidate and its variant playlists are
// suppressed; a lone MEDIA playlist (a site with no master) is kept until/unless a master claims it.
async function handleHls(tabId, url) {
  const st = tabState(tabId);
  if (st.seen.has(url) || st.variants.has(url)) return;
  st.seen.add(url);

  const info = await classifyM3u8(url);
  if (info.kind === "master") {
    for (const v of info.variants) {
      st.variants.add(v.url);
      st.items.delete(v.url);                // remove any variant we already listed
    }
    const q = info.variants.length;
    addCandidate(tabId, {
      url, type: "application/x-mpegurl", kind: "master",
      label: q ? `HLS video — ${q} qualit${q === 1 ? "y" : "ies"}` : "HLS video"
    });
  } else if (!st.variants.has(url)) {
    // media or unknown body that still looked like a playlist — low-priority candidate
    addCandidate(tabId, { url, type: "application/x-mpegurl", kind: "media", label: "HLS stream" });
  }
}

function addCandidate(tabId, cand) {
  const st = tabState(tabId);
  if (st.variants.has(cand.url) || st.items.has(cand.url)) return;
  st.items.set(cand.url, cand);
  updateBadge(tabId, primaryCount(st));
}

// Surfacing priority: master > direct file > lone media playlist.
const KIND_RANK = { master: 0, file: 1, media: 2 };
function prioritized(st) {
  return [...st.items.values()]
    .filter(c => !st.variants.has(c.url))
    .sort((a, b) => (KIND_RANK[a.kind] ?? 9) - (KIND_RANK[b.kind] ?? 9));
}
function primaryCount(st) { return prioritized(st).length; }

function fileLabel(url, ct) {
  const e = extOf(url);
  if (e) return e.toUpperCase() + " file";
  return ct || "media";
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
      const st = tabMedia.get(msg.tabId);
      sendResponse({ media: st ? prioritized(st) : [] });
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
