// Shared helpers for the Downloader browser extension (Chrome/Edge/Firefox, Manifest V3).
// `api` resolves to the WebExtensions namespace on every browser.
const api = globalThis.browser || globalThis.chrome;

// The desktop app's local loopback listener (see Services/BrowserIntegrationService.cs).
const APP_PORT = 15151;
const APP_BASE = `http://127.0.0.1:${APP_PORT}`;

// Media we can hand to the engine: direct HTTP(S) files + HLS playlists. (YouTube and other
// encrypted/DRM streaming sites are NOT supported — they don't expose a direct, fetchable URL.)
const MEDIA_EXTENSIONS = [
  "mp4", "mkv", "webm", "mov", "avi", "flv", "m4v", "mpg", "mpeg", "ts",
  "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "wma",
  "m3u8" // HLS playlist
];
const MEDIA_CONTENT_TYPES = [
  "video/", "audio/",
  "application/vnd.apple.mpegurl", "application/x-mpegurl", "application/mpegurl"
];

function extOf(url) {
  try {
    const path = new URL(url).pathname.toLowerCase();
    const dot = path.lastIndexOf(".");
    return dot >= 0 ? path.slice(dot + 1) : "";
  } catch {
    return "";
  }
}

function isHttp(url) {
  return typeof url === "string" && /^https?:\/\//i.test(url);
}

function looksLikeMedia(url) {
  return isHttp(url) && MEDIA_EXTENSIONS.includes(extOf(url));
}

function isMediaContentType(ct) {
  if (!ct) return false;
  const v = ct.toLowerCase();
  return MEDIA_CONTENT_TYPES.some(t => v.startsWith(t) || v.includes(t));
}

// How captures reach the app: "silent" adds + starts the download with no dialog (the app's
// /api/add endpoint), "dialog" opens the Add dialog pre-filled (the legacy /add endpoint).
async function getAddMode() {
  try {
    const r = await api.storage.local.get({ addMode: "silent" });
    return r.addMode === "dialog" ? "dialog" : "silent";
  } catch {
    return "silent";
  }
}

function setAddMode(mode) {
  try { api.storage.local.set({ addMode: mode === "dialog" ? "dialog" : "silent" }); } catch { /* optional */ }
}

// Silent add via the local API. Returns "ok" (added — on a pre-API app the same request opens
// the Add dialog instead, which still captures the link), "fallback" (endpoint unknown, retry
// the legacy dialog endpoint) or "fail".
async function sendToAppSilently(url, filename) {
  let endpoint = `${APP_BASE}/api/add?url=${encodeURIComponent(url)}`;
  if (filename) endpoint += `&filename=${encodeURIComponent(filename)}`;
  try {
    const res = await fetch(endpoint, { method: "GET" });
    if (res.ok) return "ok"; // 201 silent add; 200 = older app opened its dialog with the link
    if (res.status === 404) return "fallback";
    return "fail";
  } catch {
    return "fail";
  }
}

// Send a single URL to the desktop app, honoring the user's silent-vs-dialog choice.
// Returns true on success.
async function sendToApp(url, filename) {
  if (!isHttp(url)) return false;
  if (await getAddMode() === "silent") {
    const silent = await sendToAppSilently(url, filename);
    if (silent === "ok") return true;
    if (silent === "fail") return false;
    // "fallback": retry through the dialog endpoint below so older apps still capture the link.
  }
  try {
    const res = await fetch(`${APP_BASE}/add?url=${encodeURIComponent(url)}`, { method: "GET" });
    return res.ok;
  } catch {
    return false;
  }
}

// Is the desktop app reachable? (A failed /add with no url returns 400 — still proves it's up.)
async function pingApp() {
  try {
    const res = await fetch(`${APP_BASE}/ping`, { method: "GET" });
    return res.status > 0;
  } catch {
    return false;
  }
}

if (typeof module !== "undefined") {
  module.exports = { extOf, isHttp, looksLikeMedia, isMediaContentType, MEDIA_EXTENSIONS };
}
