// Shared helpers for the Downloader browser extension (Chrome/Edge/Firefox, Manifest V3).
// `api` resolves to the WebExtensions namespace on every browser.
const api = globalThis.browser || globalThis.chrome;

// The desktop app's local loopback listener (see Services/LocalApiService.cs). The app binds the
// first free port in this declared range (15151 preferred); the manifests' host_permissions cover
// exactly these origins (MV3 requires them to be static), so discovery must stay inside the range.
const APP_PORT_RANGE = [15151, 15152, 15153, 15154, 15155];
const APP_HOST = "http://127.0.0.1";

// Ports to probe, last-known-good first (mirrors the app's own preference order). Pure/testable.
function candidatePorts(cachedPort, range = APP_PORT_RANGE) {
  const ports = range.includes(cachedPort) ? [cachedPort] : [];
  for (const p of range) if (p !== cachedPort) ports.push(p);
  return ports;
}

async function getCachedPort() {
  try {
    const r = await api.storage.local.get({ appPort: APP_PORT_RANGE[0] });
    return r.appPort;
  } catch {
    return APP_PORT_RANGE[0];
  }
}

function setCachedPort(port) {
  try { api.storage.local.set({ appPort: port }); } catch { /* optional */ }
}

// Finds the port the app is currently listening on by probing /ping across the declared range,
// starting from the cached last-known-good port. Returns the port (and refreshes the cache) or
// null when the app isn't reachable on any of them. `probe` is injectable for tests.
async function discoverAppPort(probe = pingPort, cachedPort = null) {
  const cached = cachedPort ?? await getCachedPort();
  for (const port of candidatePorts(cached)) {
    if (await probe(port)) {
      setCachedPort(port);
      return port;
    }
  }
  return null;
}

// Does the app answer /ping on this port? Never throws.
async function pingPort(port) {
  try {
    const res = await fetch(`${APP_HOST}:${port}/ping`, { method: "GET" });
    return res.status > 0;
  } catch {
    return false;
  }
}

function appBase(port) {
  return `${APP_HOST}:${port}`;
}

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
// Map one browser cookie (chrome.cookies.getAll shape) to the app's /api/add cookie shape.
// Session cookies (no expirationDate) omit `expires`; the app writes them with expiry 0.
function mapCookie(c) {
  return {
    name: c.name,
    value: c.value,
    domain: c.domain,
    path: c.path || "/",
    secure: !!c.secure,
    expires: c.session ? undefined
      : (Number.isFinite(c.expirationDate) ? Math.floor(c.expirationDate) : undefined)
  };
}

// Sites whose session cookies live on a DIFFERENT domain than the link itself. A youtu.be short link
// carries no youtube.com cookies, so capturing only the link's own domain hands the app an empty jar
// and YouTube's "Sign in to confirm you're not a bot" check can't be passed. Each entry lists the
// extra origins whose cookies belong to the same session.
const COOKIE_SIBLING_ORIGINS = {
  "youtu.be": ["https://www.youtube.com/"],
  "youtube.com": ["https://www.youtube.com/"],
  "m.youtube.com": ["https://www.youtube.com/"],
  "music.youtube.com": ["https://www.youtube.com/"],
  "x.com": ["https://twitter.com/"],
  "twitter.com": ["https://x.com/"],
  "fb.watch": ["https://www.facebook.com/"]
};

// Every origin whose cookies should be captured for this link: the link itself first, then any
// sibling origin sharing its session. Pure (no browser APIs) so it is unit-testable.
function cookieUrlsFor(url) {
  const urls = [url];
  try {
    let host = new URL(url).hostname.toLowerCase();
    if (host.startsWith("www.")) host = host.slice(4);
    for (const extra of COOKIE_SIBLING_ORIGINS[host] || []) {
      if (!urls.includes(extra)) urls.push(extra);
    }
  } catch { /* not a parseable URL — just use it as-is */ }
  return urls;
}

// Capture the live session cookies for this URL (and any sibling origin sharing its session), so a
// site that needs a signed-in session (e.g. YouTube) can be resolved by the app. Reads the browser's
// live cookie jar via the extension `cookies` API — never an on-disk store. ALWAYS resolves to an
// array; any failure (no permission, no API, an exception) yields [] so sending the URL is never
// blocked (task 4.3).
async function captureCookies(url) {
  try {
    const cookiesApi = api && api.cookies;
    if (!cookiesApi || !cookiesApi.getAll) return [];
    const getAll = (u) => new Promise((resolve) => {
      try {
        const maybe = cookiesApi.getAll({ url: u }, (c) => resolve(c || [])); // MV2 callback style
        if (maybe && typeof maybe.then === "function") maybe.then((c) => resolve(c || []), () => resolve([]));
      } catch { resolve([]); }
    });

    const seen = new Set();
    const out = [];
    for (const u of cookieUrlsFor(url)) {
      for (const c of await getAll(u)) {
        const key = `${c.domain}|${c.path || "/"}|${c.name}`;
        if (seen.has(key)) continue; // the same cookie can match several origins
        seen.add(key);
        out.push(mapCookie(c));
      }
    }
    return out;
  } catch {
    return [];
  }
}

// the Add dialog instead, which still captures the link), "fallback" (endpoint unknown, retry
// the legacy dialog endpoint) or "fail".
async function sendToAppSilently(base, url, filename, cookies) {
  // When we captured live cookies, POST JSON so the cookie list + metadata travel intact (a GET query
  // can't carry them). Otherwise keep the original URL-only GET path unchanged.
  if (cookies && cookies.length) {
    const body = { url, cookies };
    if (filename) body.filename = filename;
    try {
      const res = await fetch(`${base}/api/add`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      });
      if (res.ok) return "ok";
      if (res.status === 404) return "fallback";
      return "fail";
    } catch {
      return "fail";
    }
  }
  let endpoint = `${base}/api/add?url=${encodeURIComponent(url)}`;
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
// Discovers the app's effective port first (the app may have fallen back within the declared
// range if 15151 was taken by another process). Returns true on success.
async function sendToApp(url, filename) {
  if (!isHttp(url)) return false;
  const port = await discoverAppPort();
  if (port == null) return false;
  const base = appBase(port);
  if (await getAddMode() === "silent") {
    // Best-effort: capture live cookies for this exact URL (never blocks the send if it fails).
    const cookies = await captureCookies(url);
    const silent = await sendToAppSilently(base, url, filename, cookies);
    if (silent === "ok") return true;
    if (silent === "fail") return false;
    // "fallback": retry through the dialog endpoint below so older apps still capture the link.
  }
  try {
    const res = await fetch(`${base}/add?url=${encodeURIComponent(url)}`, { method: "GET" });
    return res.ok;
  } catch {
    return false;
  }
}

// Is the desktop app reachable on any port in the declared range?
async function pingApp() {
  return await discoverAppPort() != null;
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
// exact size isn't knowable without fetching every segment. `size` is null (never a guess) if the
// variant playlist or its first segment can't be measured. Also returns the resolved segment
// URLs: the browser fetches each one as its own network response, so without excluding them a
// video's individual .ts segments would show up as separate, redundant top-level items (real-
// world regression alongside the sibling variant-playlist dedup — see popup.js's buildGroups).
async function estimateHlsSize(variantUrl, { signal } = {}) {
  let text;
  try {
    const res = await fetch(variantUrl, { signal });
    if (!res.ok) return { size: null, segmentUrls: [] };
    text = await res.text();
  } catch {
    return { size: null, segmentUrls: [] };
  }
  const segmentUrls = [];
  for (const line of text.split(/\r?\n/)) {
    if (!line || line.startsWith("#")) continue;
    try { segmentUrls.push(new URL(line.trim(), variantUrl).href); } catch { /* skip a bad line */ }
  }
  if (segmentUrls.length === 0) return { size: null, segmentUrls: [] };
  const firstSize = await probeSize(segmentUrls[0], { signal });
  return { size: firstSize ? firstSize * segmentUrls.length : null, segmentUrls };
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
// docs/plugins-architecture.md and this file's MEDIA_EXTENSIONS comment). On a matching hostname
// the popup ALWAYS shows the explanatory message and suppresses the detected list, even if
// unrelated resources were incidentally sniffed (e.g. YouTube's own UI sound effects) — none of
// what's found there is ever the protected video content the user actually wants (real-world
// fix: v1.2.0 only suppressed when zero items were found, so YouTube's notification-sound mp3s
// were shown as if downloadable). An incomplete list just means the generic empty state shows.
const KNOWN_UNSUPPORTED_HOSTS = ["youtube.com", "netflix.com", "disneyplus.com", "primevideo.com"];

function isKnownUnsupportedHost(hostname) {
  if (!hostname) return false;
  const h = hostname.toLowerCase();
  return KNOWN_UNSUPPORTED_HOSTS.some(host => h === host || h.endsWith("." + host));
}

// Below this size, a "detected" item is almost certainly a tracking beacon, empty init segment,
// or other non-content response — not something a user would ever want to download. Real-world
// fix: v1.2.0 listed every sniffed response regardless of size, surfacing sub-1KB junk (897 B,
// 988 B, 786 B) alongside real media. Only applied AFTER a probe confirms the size — an
// unprobed item's size is `null`, which always passes (never rejected before it's measured).
const MIN_MEDIA_BYTES = 8192;

function isPlausibleMediaSize(size) {
  return size == null || size >= MIN_MEDIA_BYTES;
}

// Decides which group(s) count as "Main media" for a tab, given its captured items and the
// latest visibility/playing hint from content.js. Pure and testable — see design.md Decisions 8-9.
//
// Blob: URLs mean we can't map a DOM element directly to the network URLs it caused, so this is a
// best-effort proxy: when the content script confirms something is CURRENTLY visible/loaded on
// the page (a "fresh" hint), promote whichever group(s) had the most recent network activity —
// real playback keeps fetching segments close to "now"; a static, already-loaded, possibly PAUSED
// video (v1.2.0's exact bug: a paused video was never promoted because the old logic required
// "currently playing" AND matched the item's original, possibly stale, capture time) is still the
// most recently active group relative to older/unrelated page noise.
// Shared "how close together counts as the same moment" window for the hint-freshness check and
// the group-activity-recency check below.
const MAIN_WINDOW_MS = 3000;

function computeMainGroups(items, hint, nowMs, windowMs = MAIN_WINDOW_MS) {
  const hintFresh = !!hint && (nowMs - hint.atMs) <= windowMs;
  if (!hintFresh || items.length === 0) return new Set();
  const lastActivityByGroup = new Map();
  for (const item of items) {
    const prior = lastActivityByGroup.get(item.group) ?? 0;
    if (item.capturedAt > prior) lastActivityByGroup.set(item.group, item.capturedAt);
  }
  const latest = Math.max(...lastActivityByGroup.values());
  const mainGroups = new Set();
  for (const [group, t] of lastActivityByGroup) if (latest - t <= windowMs) mainGroups.add(group);
  return mainGroups;
}

if (typeof module !== "undefined") {
  module.exports = {
    extOf, isHttp, looksLikeMedia, isMediaContentType, MEDIA_EXTENSIONS,
    formatBytes, probeSize, parseHlsMaster, estimateHlsSize,
    groupKey, extractQualityToken, runProbesBounded,
    isKnownUnsupportedHost, KNOWN_UNSUPPORTED_HOSTS,
    isPlausibleMediaSize, MIN_MEDIA_BYTES,
    computeMainGroups, MAIN_WINDOW_MS,
    candidatePorts, discoverAppPort, APP_PORT_RANGE,
    captureCookies, mapCookie, sendToAppSilently, cookieUrlsFor
  };
}
