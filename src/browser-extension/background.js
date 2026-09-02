// Background service worker / event page.
// - Adds "Download with Downloader" context menus.
// - Sniffs video/audio/HLS responses per tab and badges the toolbar icon (DASH is not surfaced —
//   see common.js's MEDIA_EXTENSIONS note).
// - Forwards captured URLs to the desktop app's local listener.
// Chrome loads shared helpers via importScripts (service worker); Firefox loads common.js first
// through the manifest "scripts" array, so importScripts is absent there — guard it.
if (typeof importScripts === "function") importScripts("common.js");

// tabId -> Map(url -> { url, type, group, capturedAt })   detected media for the current page
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

// ---------------- Download interception (issue #9) ----------------
// Take a download the BROWSER started and give it to the app instead.
//
// The ordering here is the whole safety property, so it is worth stating plainly: decide → hand off →
// and only cancel the browser's download once the app has accepted it. Cancel first and one failed
// fetch means the user has lost the file with nothing to show for it. Every failure path below
// therefore does nothing at all, which leaves the browser downloading exactly as it would have.
//
// Both browsers are driven from `downloads.onCreated`, which fires once the browser has committed to
// downloading rather than navigating. Chromium leaves `filename` empty there, so the file type is
// recovered from the URL and MIME instead — see `resolveDownloadExt`, and `registerInterception` for
// why the Chromium-only `onDeterminingFilename` is not used.

const interceptedIds = new Set(); // download ids we cancelled, so onChanged doesn't re-report them
let takeoverCount = 0;

function downloadsApi() {
  // Absent when the permission was declined, or in a context without the API. Never assume it.
  return api?.downloads?.onCreated ? api.downloads : null;
}

// The referer the browser itself would have sent. `DownloadItem.referrer` is the right answer when
// present; when it is empty (a direct navigation, some redirect chains) the originating tab's URL is
// the honest approximation.
async function refererFor(item) {
  if (item?.referrer && isHttp(item.referrer)) return item.referrer;
  try {
    // A DownloadItem carries no tab id in either browser, so the originating tab has to be inferred.
    // The active tab is the honest approximation: a download the user just started came from the page
    // they are looking at. Only used when the browser gave us no referrer at all.
    if (!api.tabs?.query) return "";
    const tabs = await api.tabs.query({ active: true, currentWindow: true });
    const url = tabs?.[0]?.url;
    return url && isHttp(url) ? url : "";
  } catch {
    return ""; // context is best-effort; never let this stop the hand-off
  }
}

async function onDownloadCreated(item) {
  try {
    const settings = await getInterceptSettings();
    // What the response for this download actually said. `downloads.onCreated` leaves `filename`
    // empty on Chromium and often reports a generic MIME, so for a signed CDN link this header is
    // frequently the only thing that names the file (issue #9).
    const seen = recallResponseHeaders(item?.finalUrl) || recallResponseHeaders(item?.url);
    const decision = shouldIntercept({
      url: item?.finalUrl || item?.url,
      filename: item?.filename,
      contentDisposition: seen?.contentDisposition,
      mime: item?.mime || seen?.contentType,
      size: item?.fileSize ?? item?.totalBytes,
      referrer: item?.referrer
    }, settings);
    if (!decision.intercept) return; // the browser keeps it — including whenever interception is off

    // Both addresses the browser had, in the order the app should try them (see handOffUrls).
    const { url, mirrors } = handOffUrls(item);
    if (!url) return;
    const referer = await refererFor(item);
    const headers = referer ? { Referer: referer } : null;
    const result = await handOffToApp(url, suggestedNameOf(item), { referer, headers, mirrors });

    // The app didn't take it. Say nothing and change nothing: the browser download is still running,
    // which is the outcome the user already had.
    if (!result.ok) return;

    // Accepting is NOT fetching. `/api/add` answers 201 as soon as the item is queued, before the app
    // has contacted the server at all, so cancelling here used to lose the file whenever the app then
    // could not fetch the link — a spent single-use token, or a server refusing the app's request
    // (issue #9, Softpedia "Secure Download"). Wait for proof that bytes are actually coming.
    const confirmed = await confirmAppFetching(appBase(result.port), result.id);
    if (!confirmed.ok) {
      // The browser's own download is still running and must stay that way — the user keeps the file.
      notify(
        "Downloader could not take this over",
        confirmed.reason === "failed"
          ? "The app could not fetch this link, so your browser is still downloading it."
          : "The app didn't start fetching in time, so your browser is still downloading it.");
      return;
    }

    // Only now is cancelling safe.
    const cancelled = await cancelBrowserDownload(item.id);
    if (!cancelled) {
      // We could not stop the browser's copy, so the file may now be downloading twice. Tell the
      // user rather than let them find two copies and no explanation.
      notify("Downloading twice", "Downloader took this over but the browser's own download could not be stopped.");
      return;
    }

    interceptedIds.add(item.id);
    takeoverCount++;
    updateTakeoverBadge();
    notify(
      result.reason === "context-dropped" ? "Sent to Downloader (without page context)" : "Sent to Downloader",
      result.reason === "context-dropped"
        ? "The app didn't accept this page's sign-in details, so a restricted file may fail."
        : (suggestedNameOf(item) || url)
    );
  } catch {
    // An unexpected failure must leave the browser download exactly as it was.
  }
}

// The browser's suggested name, without the directory part.
function suggestedNameOf(item) {
  const name = item?.filename || "";
  const cut = Math.max(name.lastIndexOf("/"), name.lastIndexOf("\\"));
  return cut >= 0 ? name.slice(cut + 1) : name;
}

function cancelBrowserDownload(id) {
  return new Promise(resolve => {
    try {
      const downloads = downloadsApi();
      if (!downloads?.cancel) return resolve(false);
      const maybe = downloads.cancel(id, () => resolve(!api.runtime?.lastError));
      if (maybe && typeof maybe.then === "function") maybe.then(() => resolve(true), () => resolve(false));
    } catch {
      resolve(false);
    }
  });
}

// A takeover removes the download from the browser's own list, so it needs to be visible somewhere or
// the file just seems to vanish. The badge is the ambient half; the notification is the explicit one.
function updateTakeoverBadge() {
  try {
    api.action.setBadgeBackgroundColor({ color: "#0E8FB3" });
    api.action.setBadgeText({ text: takeoverCount > 0 ? String(takeoverCount) : "" });
  } catch { /* badge is optional */ }
}

// Chromium and Firefox are driven from the SAME event, `downloads.onCreated`, which exists on both.
//
// `onDeterminingFilename` is Chromium-only and, unlike `onCreated`, is the one event that knows the
// browser's suggested filename — `DownloadItem.filename` is still empty at `onCreated` in Chromium
// (verified directly). It is nonetheless NOT used, for two reasons found the hard way:
//
//   1. Chromium permits only ONE `onDeterminingFilename` listener per extension ("Too many
//      listeners" otherwise), making it a scarce, un-shareable slot.
//   2. It does not fire at all when something else has set the browser's download behaviour over
//      CDP — which is exactly what an automated browser does — so the path is untestable in our
//      e2e suite and would ship unverified.
//
// It is not needed: `resolveDownloadExt` recovers the file type from the URL's content-disposition
// parameters and the MIME type, which is where the name actually lives for the signed CDN links this
// was failing on (issue #9 follow-up). The residual gap is a download named ONLY by the browser's
// suggestion — no extension in the path, no content-disposition, an unidentifiable MIME — which is
// left to the browser as before.
//
// `downloadsApi()` capability-checks, so a browser without the API (or with the permission declined)
// simply never intercepts instead of throwing on load.
function registerInterception() {
  const downloads = downloadsApi();
  if (!downloads) return false;
  downloads.onCreated.addListener(item => { onDownloadCreated(item); });
  return true;
}

registerInterception();

// ---------------- Media sniffing ----------------
api.webRequest.onHeadersReceived.addListener(
  details => {
    const header = name => (details.responseHeaders || [])
      .find(h => h.name.toLowerCase() === name)?.value;
    const ct = header("content-type");
    // Record what the response said about the file BEFORE the tab check: a download can be started
    // from a request with no tab of its own, and interception needs this answer either way.
    rememberResponseHeaders(details.url, {
      contentDisposition: header("content-disposition"),
      contentType: ct
    });
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
    updateBadge(tabId, 0);
  }
});
api.tabs.onRemoved.addListener(tabId => {
  tabMedia.delete(tabId);
});

// ---------------- Messages from the popup + content script ----------------
api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  (async () => {
    if (msg.type === "getMedia") {
      // Every item, in one flat list. There is no "main media" promotion any more: it depended on a
      // visibility hint from a content script being fresh at the exact moment the popup asked, which
      // on a feed page (x.com) it routinely was not — so the page's own video was demoted into a
      // collapsed section. The popup orders by media type instead (common.js sortDetectedGroups).
      const map = tabMedia.get(msg.tabId);
      sendResponse({ media: map ? [...map.values()] : [] });
    } else if (msg.type === "probeMedia") {
      sendResponse({ results: await probeMediaForTab(msg.tabId) });
    } else if (msg.type === "send") {
      sendResponse({ ok: await sendToApp(msg.url, msg.filename, { variantId: msg.variantId }) });
    } else if (msg.type === "ping") {
      sendResponse({ ok: await pingApp() });
    } else if (msg.type === "canHandlePage") {
      // What THIS install can do decides the popup's unsupported-site message — the answer depends on
      // which plugins the user has enabled, so only the app can give it.
      sendResponse(await askAppCanHandlePage(msg.url));
    } else if (msg.type === "pageVariants") {
      // The qualities the app can get off this page. Asked from here, not the popup, because the
      // capture of the page's session cookies lives on this side.
      sendResponse(await askAppPageVariants(msg.url));
    } else {
      sendResponse({});
    }
  })();
  return true; // keep the channel open for the async response
});
