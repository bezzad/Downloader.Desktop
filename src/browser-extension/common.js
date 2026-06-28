// Shared helpers for the Downloader browser extension (Chrome/Edge/Firefox, Manifest V3).
// `api` resolves to the WebExtensions namespace on every browser.
const api = globalThis.browser || globalThis.chrome;

// The desktop app's local loopback listener (see Services/BrowserIntegrationService.cs).
const APP_PORT = 15151;
const APP_BASE = `http://127.0.0.1:${APP_PORT}`;

// Media we can hand to the engine: direct HTTP(S) files + HLS playlists. (YouTube and other
// encrypted/DRM streaming sites are NOT supported — they don't expose a direct, fetchable URL.)
// NOTE: ".ts"/".m4s" are deliberately NOT here — they are HLS *segments*, not standalone files, and on
// sites like X they flood the list. Segments are filtered out by isHlsSegment() below.
const MEDIA_EXTENSIONS = [
  "mp4", "mkv", "webm", "mov", "avi", "flv", "m4v", "mpg", "mpeg",
  "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "wma",
  "m3u8" // HLS playlist
];
const MEDIA_CONTENT_TYPES = [
  "video/", "audio/",
  "application/vnd.apple.mpegurl", "application/x-mpegurl", "application/mpegurl"
];

function isM3u8(url) {
  if (extOf(url) === "m3u8") return true;
  // X/Twitter and some CDNs serve playlists without a .m3u8 suffix; "/pl/" or "tag=" hints help, but the
  // content-type check in the caller is the real signal. Keep this to the obvious suffix.
  return false;
}

// HLS segments / init fragments — part of a stream, never a standalone candidate. These are what flood
// the capture list on streaming sites, so they're dropped before anything else.
function isHlsSegment(url) {
  if (!isHttp(url)) return false;
  const e = extOf(url);
  if (e === "ts" || e === "m4s") return true;
  // Common fMP4 init / numbered-segment patterns (".../init.mp4", ".../seg-12.m4s", ".../1080/3.ts").
  // Restricted so a numeric-named progressive ".mp4" is NOT mistaken for a segment.
  return /\/(init\.(mp4|m4s)|(seg(ment)?[-_]?\d+|frag[-_]?\d+|\d+)\.(m4s|ts|aac))(\?|$)/i.test(url);
}

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

// Parse an HLS playlist body. Returns:
//   { kind: "master", variants: [{ url, bandwidth, resolution }] }  — a master playlist (the real video)
//   { kind: "media" }                                               — a media/variant playlist (segments)
//   { kind: "unknown" }                                             — not an HLS playlist
// Variant URIs are resolved to absolute against baseUrl so they can be suppressed elsewhere.
function parsePlaylist(text, baseUrl) {
  if (!text || !/#EXTM3U/i.test(text)) return { kind: "unknown" };
  if (/#EXT-X-STREAM-INF/i.test(text)) {
    const lines = text.split(/\r?\n/);
    const variants = [];
    let attrs = null;
    for (const raw of lines) {
      const line = raw.trim();
      if (/^#EXT-X-STREAM-INF:/i.test(line)) {
        attrs = line.slice(line.indexOf(":") + 1);
      } else if (line && !line.startsWith("#") && attrs != null) {
        const bw = /BANDWIDTH=(\d+)/i.exec(attrs);
        const res = /RESOLUTION=([0-9x]+)/i.exec(attrs);
        let abs = line;
        try { abs = new URL(line, baseUrl).href; } catch { /* keep raw */ }
        variants.push({ url: abs, bandwidth: bw ? +bw[1] : 0, resolution: res ? res[1] : null });
        attrs = null;
      }
    }
    return { kind: "master", variants };
  }
  if (/#EXTINF/i.test(text)) return { kind: "media" };
  return { kind: "unknown" };
}

// Fetch an .m3u8 and classify it (master vs media). Network/parse failures => "unknown".
async function classifyM3u8(url) {
  try {
    const res = await fetch(url, { method: "GET" });
    if (!res.ok) return { kind: "unknown" };
    const text = await res.text();
    return parsePlaylist(text, url);
  } catch {
    return { kind: "unknown" };
  }
}

// Send a single URL to the desktop app. Returns true on success.
async function sendToApp(url) {
  if (!isHttp(url)) return false;
  const endpoint = `${APP_BASE}/add?url=${encodeURIComponent(url)}`;
  try {
    const res = await fetch(endpoint, { method: "GET" });
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
  module.exports = {
    extOf, isHttp, looksLikeMedia, isMediaContentType, MEDIA_EXTENSIONS,
    isM3u8, isHlsSegment, parsePlaylist, classifyM3u8
  };
}
