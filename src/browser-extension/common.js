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

// ---------------- Media metadata probing (popup: size/resolution/quality) ----------------

// "12345" -> "1.2 KB"/"3.4 MB"/etc. Returns null for a non-positive/unknown size.
function formatBytes(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) return null;
  const units = ["B", "KB", "MB", "GB", "TB"];
  let n = bytes, i = 0;
  while (n >= 1024 && i < units.length - 1) { n /= 1024; i++; }
  return `${i === 0 || n >= 10 ? Math.round(n) : n.toFixed(1)} ${units[i]}`;
}

// HEAD first (reads Content-Length); falls back to a 1-byte ranged GET for CDNs that reject or
// omit it on HEAD (Content-Range's total). Never throws — returns null on any failure.
async function probeSize(url, { signal } = {}) {
  try {
    const head = await fetch(url, { method: "HEAD", signal });
    const len = head.headers.get("content-length");
    if (head.ok && len) return parseInt(len, 10);
  } catch { /* fall through to the ranged GET */ }
  try {
    const ranged = await fetch(url, { method: "GET", headers: { Range: "bytes=0-0" }, signal });
    const cr = ranged.headers.get("content-range"); // "bytes 0-0/12345"
    const total = cr && cr.split("/")[1];
    if (total && total !== "*") return parseInt(total, 10);
  } catch { /* best-effort */ }
  return null;
}

// Parses an HLS MASTER playlist's #EXT-X-STREAM-INF variants into [{ uri, resolution, bandwidth }].
// Returns [] when the URL isn't fetchable, or when it's actually a variant/media playlist (no
// #EXT-X-STREAM-INF entries) — callers fall back to treating it as one plain file.
async function parseHlsMaster(url, { signal } = {}) {
  let text;
  try {
    const res = await fetch(url, { signal });
    if (!res.ok) return [];
    text = await res.text();
  } catch {
    return [];
  }
  const lines = text.split(/\r?\n/);
  const variants = [];
  for (let i = 0; i < lines.length; i++) {
    if (!lines[i].startsWith("#EXT-X-STREAM-INF:")) continue;
    const uriLine = lines[i + 1];
    if (!uriLine || uriLine.startsWith("#")) continue;
    const resolution = lines[i].match(/RESOLUTION=(\d+x\d+)/)?.[1] ?? null;
    const bwMatch = lines[i].match(/BANDWIDTH=(\d+)/);
    let uri;
    try { uri = new URL(uriLine.trim(), url).href; } catch { continue; }
    variants.push({ uri, resolution, bandwidth: bwMatch ? parseInt(bwMatch[1], 10) : null });
  }
  return variants;
}

// Estimates an HLS variant playlist's total size as (segment count) x (first segment's size) —
// exact size isn't knowable without fetching every segment. Returns null (never a guess) if the
// variant playlist or its first segment can't be measured.
async function estimateHlsSize(variantUrl, { signal } = {}) {
  let text;
  try {
    const res = await fetch(variantUrl, { signal });
    if (!res.ok) return null;
    text = await res.text();
  } catch {
    return null;
  }
  const segments = text.split(/\r?\n/).filter(l => l && !l.startsWith("#"));
  if (segments.length === 0) return null;
  let firstUrl;
  try { firstUrl = new URL(segments[0].trim(), variantUrl).href; } catch { return null; }
  const firstSize = await probeSize(firstUrl, { signal });
  return firstSize ? firstSize * segments.length : null;
}

// Conservative "same video, different quality" grouping key: strips ONE trailing quality token
// (e.g. "_720p", "-1080", ".hd") from the basename. Anything that doesn't match a known token
// shape returns the full URL as its own unique key, so unrelated files are never merged.
// Every HLS URL (.m3u8) is its own group — a master playlist's variants come from parsing it
// (see parseHlsMaster), not from grouping with other sniffed URLs.
const QUALITY_TOKEN_RE = /[_.-](\d{3,4}p?|hd|sd|4k|low|med(?:ium)?|high)$/i;

function extractQualityToken(url) {
  try {
    const base = new URL(url).pathname.split("/").pop() || "";
    const dot = base.lastIndexOf(".");
    const stem = dot > 0 ? base.slice(0, dot) : base;
    const m = stem.match(QUALITY_TOKEN_RE);
    return m ? m[1] : null;
  } catch {
    return null;
  }
}

function groupKey(url) {
  try {
    const u = new URL(url);
    if (extOf(url) === "m3u8") return url; // HLS: the master URL itself is the group key.
    const dir = u.pathname.slice(0, u.pathname.lastIndexOf("/") + 1);
    const base = u.pathname.slice(u.pathname.lastIndexOf("/") + 1);
    const dot = base.lastIndexOf(".");
    const stem = dot > 0 ? base.slice(0, dot) : base;
    const ext = dot > 0 ? base.slice(dot) : "";
    const stripped = stem.replace(QUALITY_TOKEN_RE, "");
    if (stripped === stem) return url; // no quality token found -> unique key, never merged
    return `${u.origin}${dir}${stripped}${ext}`;
  } catch {
    return url;
  }
}

// Runs `tasks` (each a function receiving an AbortSignal and returning a promise) with a
// concurrency cap and a per-task timeout. Never rejects: a timed-out or throwing task resolves to
// null in the result array, in the same order as `tasks` — callers never need to catch.
async function runProbesBounded(tasks, { concurrency = 4, timeoutMs = 2500 } = {}) {
  const results = new Array(tasks.length).fill(null);
  let next = 0;
  async function worker() {
    while (next < tasks.length) {
      const i = next++;
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), timeoutMs);
      try {
        results[i] = await tasks[i](controller.signal);
      } catch {
        results[i] = null;
      } finally {
        clearTimeout(timer);
      }
    }
  }
  await Promise.all(Array.from({ length: Math.min(concurrency, tasks.length) }, worker));
  return results;
}

// Hostnames known to stream via MSE/DRM with no stable, single-file downloadable URL (see
// docs/plugins-architecture.md and this file's MEDIA_EXTENSIONS comment). This list only gates
// the popup's EXPLANATORY empty-state message — it never blocks detection itself, so an
// incomplete list just means the generic empty state shows instead of the explained one.
const KNOWN_UNSUPPORTED_HOSTS = ["youtube.com", "netflix.com", "disneyplus.com", "primevideo.com"];

function isKnownUnsupportedHost(hostname) {
  if (!hostname) return false;
  const h = hostname.toLowerCase();
  return KNOWN_UNSUPPORTED_HOSTS.some(host => h === host || h.endsWith("." + host));
}

if (typeof module !== "undefined") {
  module.exports = {
    extOf, isHttp, looksLikeMedia, isMediaContentType, MEDIA_EXTENSIONS,
    formatBytes, probeSize, parseHlsMaster, estimateHlsSize,
    groupKey, extractQualityToken, runProbesBounded,
    isKnownUnsupportedHost, KNOWN_UNSUPPORTED_HOSTS
  };
}
