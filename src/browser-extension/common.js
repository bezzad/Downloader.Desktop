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

// What to tell the user when the app answers on none of its ports. Names the ports actually probed —
// the whole declared range, which is all the extension is allowed to reach (MV3 host_permissions are
// static) — so "it didn't detect the app" can be diagnosed instead of guessed at (issue #9).
function appNotFoundMessage(range = APP_PORT_RANGE) {
  return `Downloader was not found on 127.0.0.1 ports ${range[0]}–${range[range.length - 1]}. `
    + "Start the app, and check Settings → Browser integration is on.";
}

function appBase(port) {
  return `${APP_HOST}:${port}`;
}

// Media we can hand to the engine: direct HTTP(S) files + adaptive-streaming manifests. (YouTube and
// other encrypted/DRM streaming sites are NOT supported — they don't expose a direct, fetchable URL.)
const MEDIA_EXTENSIONS = [
  "mp4", "mkv", "webm", "mov", "avi", "flv", "m4v", "mpg", "mpeg", "ts",
  "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "wma",
  "m3u8", // HLS playlist
  "mpd"   // MPEG-DASH manifest
];
const MEDIA_CONTENT_TYPES = [
  "video/", "audio/",
  "application/vnd.apple.mpegurl", "application/x-mpegurl", "application/mpegurl",
  "application/dash+xml"
];

// A manifest describes a stream rather than being one downloadable file, so it is never grouped or
// size-probed like a plain media URL — the app expands it after the link is sent over.
const MANIFEST_EXTENSIONS = ["m3u8", "mpd"];

function isManifest(url) {
  return MANIFEST_EXTENSIONS.includes(extOf(url));
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
// POST an add to the app and read back what it accepted. Since v2.5.0 the app answers a successful
// add with `{id, name, status, cookies, headers, referer}` — counts of the request context it
// actually took (never values). Older apps answer without those fields, which reads as "unknown"
// rather than "dropped".
async function postAdd(base, body) {
  try {
    const res = await fetch(`${base}/api/add`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    let json = null;
    try { json = await res.json?.(); } catch { /* an older app may answer with no/!JSON body */ }
    return { ok: !!res.ok, status: res.status, json };
  } catch {
    return { ok: false, status: 0, json: null };
  }
}

async function sendToAppSilently(base, url, filename, cookies, context) {
  // When we captured live cookies, POST JSON so the cookie list + metadata travel intact (a GET query
  // can't carry them). Otherwise keep the original URL-only GET path unchanged.
  const referer = context?.referer;
  const headers = context?.headers;
  const hasContext = (cookies && cookies.length) || referer || (headers && Object.keys(headers).length);
  if (hasContext) {
    const body = { url };
    if (cookies && cookies.length) body.cookies = cookies;
    if (filename) body.filename = filename;
    // The browser WOULD have sent a referer. If we take a download over and don't, we turn a working
    // download into a broken one — so for an intercepted download this is not an extra, it's a
    // precondition. The app applies it to that download only (issue #7).
    if (referer) body.referer = referer;
    if (headers && Object.keys(headers).length) body.headers = headers;
    const res = await postAdd(base, body);
    if (res.ok) return "ok";
    if (res.status === 404) return "fallback";
    return "fail";
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
async function sendToApp(url, filename, context) {
  if (!isHttp(url)) return false;
  const port = await discoverAppPort();
  if (port == null) return false;
  const base = appBase(port);
  if (await getAddMode() === "silent") {
    // Best-effort: capture live cookies for this exact URL (never blocks the send if it fails).
    const cookies = await captureCookies(url);
    const silent = await sendToAppSilently(base, url, filename, cookies, context);
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

/**
 * Hand an INTERCEPTED download to the app. Stricter than `sendToApp`, because the caller is about to
 * cancel the browser's own download on the strength of the answer:
 *  - always the POST form (a session cookie has no business in a URL),
 *  - reports what the app said it accepted, so a hand-off that silently lost its context is not
 *    counted as a win,
 *  - never throws; an unreachable app is a plain `{ ok: false }`, which the caller reads as
 *    "leave the browser download alone".
 *
 * Returns `{ ok, reason, accepted, contextSent, id }`.
 */
/**
 * Wait until the app's transfer has demonstrably REACHED THE SERVER, so the browser's own download
 * can be cancelled without risking the file.
 *
 * A 201 from /api/add is not that proof: the app answers it straight after queueing the item, before
 * a single packet leaves the machine. Cancelling on it is what made a link the app could not fetch
 * (a spent single-use token, a server refusing the app's request) lose the user's file outright.
 *
 * Confirmation is `downloaded > 0` or `size > 0` — the engine only learns a total size from a real
 * response, so either one means the link was fetchable. `status: "Running"` is deliberately NOT
 * enough: the app sets it synchronously before any network work, so it carries the same weakness as
 * the 201.
 *
 * Resolves `{ ok, reason }` and never throws. `reason` is one of:
 *   "confirmed" — bytes or a size arrived; the caller may cancel the browser's copy
 *   "failed"    — the app reported the download failed; keep the browser's copy
 *   "timeout"   — nothing was confirmed in time; keep the browser's copy
 */
async function confirmAppFetching(base, id, opts) {
  const timeoutMs = opts?.timeoutMs ?? 12000;
  const intervalMs = opts?.intervalMs ?? 400;
  const now = opts?.now || (() => Date.now());
  const sleep = opts?.sleep || (ms => new Promise(r => setTimeout(r, ms)));
  if (!id) return { ok: false, reason: "timeout" };

  const deadline = now() + timeoutMs;
  for (;;) {
    let rows = null;
    try {
      const res = await fetch(`${base}/api/list`);
      if (res?.ok) rows = await res.json?.();
    } catch { /* the app went away mid-wait — treated as "not confirmed" below */ }

    const row = Array.isArray(rows) ? rows.find(r => String(r?.id) === String(id)) : null;
    if (row) {
      const downloaded = Number(row.downloaded) || 0;
      const size = Number(row.size) || 0;
      if (downloaded > 0 || size > 0) return { ok: true, reason: "confirmed" };
      // A failure is final: there is nothing left to wait for.
      if (String(row.status).toLowerCase() === "failed") return { ok: false, reason: "failed" };
    }

    if (now() >= deadline) return { ok: false, reason: "timeout" };
    await sleep(intervalMs);
  }
}

// The browser's own User-Agent. `navigator` exists in an MV3 service worker and in a Firefox
// background script, but not in the plain-Node test context, so this must never assume it.
function browserUserAgent() {
  try {
    return typeof navigator !== "undefined" && navigator.userAgent ? String(navigator.userAgent) : "";
  } catch {
    return "";
  }
}

async function handOffToApp(url, filename, context) {
  if (!isHttp(url)) return { ok: false, reason: "not-http", accepted: null, contextSent: null };

  const port = await discoverAppPort();
  if (port == null) return { ok: false, reason: "app-unreachable", accepted: null, contextSent: null };

  // Best-effort throughout: a context we couldn't gather is worth less than the download itself.
  const cookies = await captureCookies(url);
  const referer = context?.referer || "";

  // The app's request should resemble the request the browser was about to make, or a server that
  // checks the client identity refuses it — a candidate cause of the Softpedia "Secure Download"
  // failures on issue #9. The app maps `user-agent` onto its request configuration already.
  const merged = { ...(context?.headers || {}) };
  const ua = browserUserAgent();
  if (ua && !Object.keys(merged).some(k => k.toLowerCase() === "user-agent")) merged["User-Agent"] = ua;
  const headers = Object.keys(merged).length ? merged : null;
  const contextSent = { cookies: cookies.length, headers: headers ? Object.keys(headers).length : 0, referer: !!referer };

  // Every caller of this function is the interception path, so the app is told the link came from a
  // download the browser had already started — which is what lets it read a first-request failure as
  // a spent single-use address rather than a bad link.
  const body = { url, fromBrowser: true };
  if (filename) body.filename = filename;
  // Fallback links for the same file — see the caller in background.js for why the redirect chain's
  // end travels as a mirror rather than as the download's own address. The app tries them in order.
  const mirrors = (context?.mirrors || []).filter(m => isHttp(m) && m !== url);
  if (mirrors.length) body.mirrors = mirrors;
  if (cookies.length) body.cookies = cookies;
  if (referer) body.referer = referer;
  if (headers) body.headers = headers;

  const res = await postAdd(appBase(port), body);
  if (!res.ok) {
    return {
      ok: false,
      reason: res.status ? `app-rejected-${res.status}` : "app-unreachable",
      accepted: null,
      contextSent
    };
  }

  // What the app says it took. Absent fields = an older app that doesn't report them; treat that as
  // unknown, not as a loss, or every pre-2.5.0 app would look like a failure.
  const j = res.json || {};
  const accepted = {
    cookies: Number.isFinite(j.cookies) ? j.cookies : null,
    headers: Number.isFinite(j.headers) ? j.headers : null,
    referer: typeof j.referer === "boolean" ? j.referer : null
  };
  const lost =
    (accepted.cookies === 0 && contextSent.cookies > 0) ||
    (accepted.headers === 0 && contextSent.headers > 0) ||
    (accepted.referer === false && contextSent.referer);

  // The add succeeded either way — the file is being fetched, so the caller may cancel the browser's
  // copy. `context-dropped` is reported so a gated download that will now fail is explainable
  // instead of mysterious.
  // `port` is returned so the caller can confirm the transfer actually reached the server before
  // cancelling the browser's copy — see `confirmAppFetching`.
  return {
    ok: true, reason: lost ? "context-dropped" : "ok",
    accepted, contextSent, id: j.id || null, port
  };
}

// Is the desktop app reachable on any port in the declared range?
async function pingApp() {
  return await discoverAppPort() != null;
}

// Does the running app claim this page? Discovers the port the same way every other call does.
async function askAppCanHandlePage(url) {
  const port = await discoverAppPort();
  return await appCanHandlePage(url, port);
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
// Every manifest URL (.m3u8, .mpd) is its own group — its variants come from parsing the manifest
// (see parseHlsMaster; DASH representations are expanded by the app), not from grouping with other
// sniffed URLs.
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
    if (isManifest(url)) return url; // HLS/DASH: the manifest URL itself is the group key.
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

// The name of the app plugin that turns pages on video sites into downloads. Named in the popup so a
// user on such a page is told what would make it work, instead of a dead end.
const SITE_MEDIA_PLUGIN_NAME = "Video sites (YouTube and others)";

// Does THIS install of the app claim this page? Asks the app's /api/can-handle, which answers from the
// plugins that are actually enabled. Never throws: an unreachable or older app (404) answers "no", which
// reproduces the behaviour from before this endpoint existed.
async function appCanHandlePage(url, port) {
  if (!url || port == null) return { handled: false, by: null };
  try {
    const res = await fetch(`${APP_HOST}:${port}/api/can-handle?url=${encodeURIComponent(url)}`);
    if (!res.ok) return { handled: false, by: null };
    const body = await res.json();
    return { handled: body?.handled === true, by: body?.by ?? null };
  } catch {
    return { handled: false, by: null };
  }
}

// What the popup shows for a page on a site whose video can't be sniffed off the network (MSE/DRM).
// Pure, so both branches are unit-tested: with a plugin that claims the page the page itself is
// offered to the app; without one the user is told which plugin would do it. Deliberately never "you
// must be signed in" — that was the old wording and it is wrong: the people who see it ARE signed in,
// and signing in again changes nothing (issue #9 follow-up).
function unsupportedSiteState({ hostUnsupported, appHandlesPage, handlerName }) {
  if (!hostUnsupported) return { mode: "normal", message: null };
  if (appHandlesPage) {
    return {
      mode: "offer",
      message: "This site's player hides the video file, but Downloader can fetch this page itself"
        + (handlerName ? ` (${handlerName})` : "") + ". Send the page to the app.",
    };
  }
  return {
    mode: "unsupported",
    message: "This site streams video in a format Downloader can't capture from the page. "
      + `Install the “${SITE_MEDIA_PLUGIN_NAME}” plugin in the app (Settings → Plugins) to download from here.`,
  };
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

// ---------------- Download interception (issue #9) ----------------

// Types worth handing to a download manager. An ALLOW list, so ordinary browsing is untouched by
// default: a PDF opening inline, an image, a page asset — none of these are in it, so none of them
// are taken over. Users who want more add to it on the options page, which is why that control is
// the most prominent one there.
const INTERCEPT_FILE_TYPES = [
  "7z", "apk", "apks", "appimage", "bin", "bz2", "deb", "dmg", "exe", "gz", "img", "iso", "jar",
  "msi", "obb", "pkg", "rar", "rpm", "run", "tar", "tgz", "txz", "xapk", "xz", "zip", "zst"
];

// One versioned object rather than scattered keys, so adding a rule later doesn't need a migration
// per key. `enabled: false` is deliberate: updating the extension must never change how someone's
// browser behaves without them asking for it.
const INTERCEPT_DEFAULTS = {
  version: 1,
  enabled: false,
  // 0 = no size floor. `shouldIntercept` still implements the rule, so a user can raise it.
  minSizeBytes: 0,
  fileTypes: { mode: "allow", list: INTERCEPT_FILE_TYPES.slice() },
  excludedSites: []
};

// Extension of a bare FILE NAME. `extOf` parses a URL, so it returns "" for "installer.msi" — and
// the browser's suggested filename (from Content-Disposition) is exactly a bare name, and is often
// the only place the real type appears when the URL is a signed, extensionless CDN link.
function extOfName(name) {
  const n = String(name || "").trim().toLowerCase();
  const base = n.slice(Math.max(n.lastIndexOf("/"), n.lastIndexOf("\\")) + 1);
  const dot = base.lastIndexOf(".");
  return dot > 0 ? base.slice(dot + 1) : "";
}

// The filename a content-disposition value names, or "". Handles the two shapes that actually turn
// up: `attachment; filename=thing.zip` (optionally quoted) and RFC 5987's
// `filename*=UTF-8''thing.zip`. `filename*` wins when both are present, since that is the encoded
// one the spec says to prefer.
//
// Hostile input is the norm here — this parses whatever a CDN put in a URL — so it must never throw.
// An unparseable value returns "" and the caller falls through to the next source.
function filenameFromContentDisposition(value) {
  let v = String(value || "");
  if (!v) return "";
  try {
    // Read through a value that is still wholly percent-encoded (`attachment%3B%20filename%3Dx.zip`).
    // `searchParams.get` decodes for us, so this only matters for a raw value, but decoding here
    // costs nothing and means the parser doesn't depend on how it was handed over.
    if (!/filename\s*\*?\s*=/i.test(v) && /%[0-9a-f]{2}/i.test(v)) {
      try { v = decodeURIComponent(v); } catch { /* keep the original; the tests below just fail */ }
    }
    const star = /filename\*\s*=\s*(?:UTF-8|ISO-8859-1)?''([^;]+)/i.exec(v);
    if (star) {
      const name = decodeURIComponent(star[1].trim());
      if (name) return baseNameOf(name);
    }
    const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/i.exec(v);
    if (plain) {
      // Group 2 is the quoted body; group 1 is the raw run when unquoted.
      let name = (plain[2] !== undefined ? plain[2] : plain[1]).trim();
      // A value that reached us through a query string may still be percent-encoded.
      try { name = decodeURIComponent(name); } catch { /* already decoded, or invalid escapes */ }
      if (name) return baseNameOf(name);
    }
  } catch { /* fall through — an unreadable value simply names nothing */ }
  return "";
}

// Strip any directory part a name arrived with. A content-disposition filename is supposed to be a
// bare name, but nothing stops a server sending a path, and only the last segment is the file.
function baseNameOf(name) {
  const n = String(name || "").trim();
  const cut = Math.max(n.lastIndexOf("/"), n.lastIndexOf("\\"));
  return cut >= 0 ? n.slice(cut + 1) : n;
}

// A signed CDN link carries the real filename in its query string rather than its path — GitHub's
// release assets use both `response-content-disposition` and the short `rscd`. Without this, such a
// URL looks typeless and is never intercepted (issue #9).
const CONTENT_DISPOSITION_PARAMS = ["response-content-disposition", "rscd"];

function filenameFromUrlQuery(url) {
  try {
    const params = new URL(url).searchParams;
    for (const key of CONTENT_DISPOSITION_PARAMS) {
      const name = filenameFromContentDisposition(params.get(key));
      if (name) return name;
    }
  } catch { /* unparseable URL — nothing to read */ }
  return "";
}

// MIME types that identify a file unambiguously. Deliberately small: it exists to catch a download
// nothing else names, not to classify the web. Generic containers are absent ON PURPOSE —
// `application/octet-stream` is what GitHub serves for every asset, so honouring it would intercept
// by MIME alone and take over files the user never asked for.
const MIME_EXTENSIONS = {
  "application/vnd.android.package-archive": "apk",
  "application/x-msdownload": "exe",
  "application/x-msi": "msi",
  "application/x-ms-installer": "msi",
  "application/zip": "zip",
  "application/x-zip-compressed": "zip",
  "application/x-7z-compressed": "7z",
  "application/x-rar-compressed": "rar",
  "application/vnd.rar": "rar",
  "application/x-tar": "tar",
  "application/gzip": "gz",
  "application/x-gzip": "gz",
  "application/x-bzip2": "bz2",
  "application/x-xz": "xz",
  "application/x-iso9660-image": "iso",
  "application/x-apple-diskimage": "dmg",
  "application/vnd.debian.binary-package": "deb",
  "application/x-debian-package": "deb",
  "application/x-redhat-package-manager": "rpm",
  "application/x-rpm": "rpm",
  "application/java-archive": "jar"
};

function extFromMime(mime) {
  const m = String(mime || "").toLowerCase().split(";")[0].trim();
  return MIME_EXTENSIONS[m] || "";
}

/**
 * The file type of a download, from the most trustworthy source that can name it.
 *
 * Order matters. The browser's suggested filename is what the file WILL be called, so it wins.
 * Content-disposition is the same answer straight from the server and beats the URL path, which is
 * frequently a signed opaque blob id. MIME comes last because the common value identifies nothing —
 * it must never override a real name.
 *
 * Returns "" only when no source identified a type, which the caller reports distinctly from "the
 * user does not want this type".
 */
function resolveDownloadExt(item) {
  return candidateExts(item)[0] || "";
}

// Trailing dotted runs that are NOT file extensions. A URL path's last segment is often a package
// name (`com.instagram.android`), a host-like token or a version — `extOf` cannot tell those from a
// real extension, and a wrong-but-non-empty answer is worse than none: it fails the user's type list
// AND hides every later source (issue #9, APKPure).
//
// Only the URL PATH is filtered. A name a server actually stated — the browser's suggestion or a
// content-disposition — is taken at its word however unusual its extension, because there the server
// is telling us what the file is called rather than us guessing from an address.
// Guessing which dotted runs are "not extensions" cannot be done by shape — `whatsapp` and `appimage`
// are the same shape. So the path is trusted only when it names a type something here RECOGNISES:
// a type the user listed, a type a MIME maps to, or a media extension. A path ending in an
// unrecognised token names nothing, which is the honest answer and the one that lets a later source
// (the response's content-disposition, the MIME) speak.
// ---- What the response itself said about the file ----
// `downloads.onCreated` gives Chromium no filename and often only a generic MIME, but the response
// that started the download DID carry a content-disposition — and for an .xapk that is the only
// source there is (no MIME identifies one). The extension already watches every response for media
// sniffing, so the answer is recorded there and looked up here (issue #9, APKPure).
//
// Bounded on purpose: this sees every response the browser makes. Entries are small, capped, and
// expire, so a long browsing session cannot grow it without limit.
const RESPONSE_HEADER_CACHE_MAX = 200;
const RESPONSE_HEADER_TTL_MS = 120000; // a download starts within seconds of its response

const responseHeaderCache = new Map(); // url -> { contentDisposition, contentType, atMs }

function rememberResponseHeaders(url, headers, now = Date.now()) {
  if (!isHttp(url)) return;
  const contentDisposition = String(headers?.contentDisposition || "");
  const contentType = String(headers?.contentType || "");
  // Nothing worth keeping: no name, and a content type that identifies nothing.
  if (!contentDisposition && !extFromMime(contentType)) return;
  responseHeaderCache.delete(url); // re-insert so Map iteration order is least-recently-set first
  responseHeaderCache.set(url, { contentDisposition, contentType, atMs: now });
  while (responseHeaderCache.size > RESPONSE_HEADER_CACHE_MAX)
    responseHeaderCache.delete(responseHeaderCache.keys().next().value);
}

// What the response for this URL said, or null. An entry past its TTL is dropped rather than
// returned: a stale name is worse than no name.
function recallResponseHeaders(url, now = Date.now()) {
  const hit = responseHeaderCache.get(url);
  if (!hit) return null;
  if (now - hit.atMs > RESPONSE_HEADER_TTL_MS) {
    responseHeaderCache.delete(url);
    return null;
  }
  return hit;
}

// Ordinary file types beyond the ones already listed elsewhere. They are NOT interception candidates
// by default (that is `INTERCEPT_FILE_TYPES`' job) — they are here so that a path naming one is
// reported as "a type you did not ask for" rather than "unidentifiable", which is the difference
// between a useful decision reason and a misleading one.
const COMMON_FILE_EXTS = [
  "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "ods", "rtf", "txt", "csv", "json",
  "xml", "epub", "mobi", "jpg", "jpeg", "png", "gif", "webp", "svg", "bmp", "ico", "psd", "ttf",
  "otf", "woff", "woff2", "torrent", "cab", "msu", "vhd", "ova", "sig", "asc", "sha256"
];

const KNOWN_PATH_EXTS = new Set([
  ...INTERCEPT_FILE_TYPES,
  ...MEDIA_EXTENSIONS,
  ...Object.values(MIME_EXTENSIONS),
  ...COMMON_FILE_EXTS
]);

function isPlausiblePathExt(ext, known = KNOWN_PATH_EXTS) {
  const e = String(ext || "").toLowerCase();
  if (!/^[a-z0-9]{1,8}$/.test(e)) return false;
  return known.has(e);
}

/**
 * EVERY file type this download's sources name, most trustworthy first, deduped.
 *
 * A set, not a single answer, because any one source can be confidently wrong: a signed CDN link's
 * path names a package, a generic MIME names nothing, and the browser has no suggestion yet at
 * `downloads.onCreated`. Deciding on the first non-empty source lets the wrong one veto the right
 * one — which is exactly the bug this replaced (issue #9).
 *
 * Order: the browser's suggested filename (what the file WILL be called) → the content-disposition
 * the response actually carried → the same, carried in the URL's query as signed CDN links do → the
 * URL path (filtered, see above) → the MIME type, last because the common value identifies nothing.
 */
function candidateExts(item, knownPathExts) {
  const url = item?.url || "";
  const pathExt = extOf(url);
  const found = [
    extOfName(item?.filename),
    extOfName(filenameFromContentDisposition(item?.contentDisposition)),
    extOfName(filenameFromUrlQuery(url)),
    isPlausiblePathExt(pathExt, knownPathExts) ? pathExt : "",
    extFromMime(item?.mime)
  ];
  return [...new Set(found.filter(Boolean))];
}

// Does `host` match a site-list entry? An entry covers the host itself and its subdomains, so
// "example.com" excludes "files.example.com" too — which is what a user typing a site expects.
function hostMatchesSite(host, entry) {
  if (!host || !entry) return false;
  const h = String(host).toLowerCase().replace(/^www\./, "");
  const e = String(entry).toLowerCase().trim().replace(/^www\./, "").replace(/^\.+/, "");
  if (!e) return false;
  return h === e || h.endsWith("." + e);
}

// Merge stored settings over the defaults so a partial or older stored object still yields a
// complete, usable one (and an unknown `mode` can't wedge interception).
function normalizeInterceptSettings(stored) {
  const s = stored && typeof stored === "object" ? stored : {};
  const types = s.fileTypes && typeof s.fileTypes === "object" ? s.fileTypes : {};
  const size = Number(s.minSizeBytes);
  return {
    version: INTERCEPT_DEFAULTS.version,
    enabled: s.enabled === true,
    minSizeBytes: Number.isFinite(size) && size > 0 ? Math.floor(size) : 0,
    fileTypes: {
      mode: types.mode === "deny" ? "deny" : "allow",
      list: Array.isArray(types.list)
        ? types.list.map(t => String(t).toLowerCase().trim().replace(/^\./, "")).filter(Boolean)
        : INTERCEPT_DEFAULTS.fileTypes.list.slice()
    },
    excludedSites: Array.isArray(s.excludedSites)
      ? s.excludedSites.map(x => String(x).toLowerCase().trim()).filter(Boolean)
      : []
  };
}

/**
 * Should the app take this browser download over? Pure — no browser APIs, no network — so the whole
 * rule set is unit-testable and the listener in background.js stays a thin shell around it.
 *
 * Returns `{ intercept, reason }`. The reason is not decoration: when a user reports "it didn't
 * intercept my file", the reason is the difference between a diagnosable report and a guess.
 */
function shouldIntercept(item, settings) {
  const s = normalizeInterceptSettings(settings);
  const url = item?.url || "";

  if (!s.enabled) return { intercept: false, reason: "disabled" };

  // blob:, data:, filesystem: and extension-internal downloads have no URL the app could re-fetch.
  // Taking one over would destroy the download outright, so they are never candidates.
  if (!isHttp(url)) return { intercept: false, reason: "not-http" };

  let host = "";
  try { host = new URL(url).hostname; } catch { /* unparseable — the site rule just can't match */ }
  if (s.excludedSites.some(entry => hostMatchesSite(host, entry)))
    return { intercept: false, reason: "excluded-site" };
  // A site exclusion is about the page you're on, so it applies to the referring page too.
  let refHost = "";
  try { refHost = item?.referrer ? new URL(item.referrer).hostname : ""; } catch { /* ignore */ }
  if (refHost && s.excludedSites.some(entry => hostMatchesSite(refHost, entry)))
    return { intercept: false, reason: "excluded-site" };

  // Not the URL path alone: a signed CDN link (GitHub releases, APKPure, Softpedia) has an opaque
  // path and names the file elsewhere. Every candidate is considered, not just the first — see
  // `candidateExts` for why that matters and for the source order.
  // The user's own list joins the recognised path extensions, so a type they added by hand is
  // trusted in a URL path exactly like a built-in one.
  const known = new Set([...KNOWN_PATH_EXTS, ...s.fileTypes.list]);
  const exts = candidateExts({
    url,
    filename: item?.filename,
    contentDisposition: item?.contentDisposition,
    mime: item?.mime
  }, known);
  const listed = exts.some(e => s.fileTypes.list.includes(e));
  if (s.fileTypes.mode === "allow" && !listed)
    return { intercept: false, reason: exts.length ? "type-not-allowed" : "type-unknown" };
  // Deny mode declines as soon as ANY candidate is listed: with several possible names, the safe
  // reading of "don't intercept this type" is that one match is enough to leave it alone.
  if (s.fileTypes.mode === "deny" && listed)
    return { intercept: false, reason: "type-denied" };

  // Unknown size must NOT read as "too small": downloads.onCreated routinely reports -1/0 because
  // the headers haven't landed yet, so treating that as below the floor would make interception
  // fail at random — exactly the class of bug this issue is about.
  const size = Number(item?.size);
  if (s.minSizeBytes > 0 && Number.isFinite(size) && size > 0 && size < s.minSizeBytes)
    return { intercept: false, reason: "too-small" };

  return { intercept: true, reason: "ok" };
}

// Read the interception settings. `sync` so a user's rules follow their profile, falling back to
// `local` (Firefox private windows and some enterprise policies leave sync unavailable).
async function getInterceptSettings() {
  const read = async (area) => {
    if (!api?.storage?.[area]) return null;
    const r = await api.storage[area].get({ intercept: null });
    return r?.intercept ?? null;
  };
  try {
    const synced = await read("sync");
    if (synced) return normalizeInterceptSettings(synced);
  } catch { /* fall through to local */ }
  try {
    const local = await read("local");
    if (local) return normalizeInterceptSettings(local);
  } catch { /* fall through to defaults */ }
  return normalizeInterceptSettings(null);
}

// Persist the settings to `sync`, mirroring to `local` so a later sync failure still reads them back.
async function setInterceptSettings(settings) {
  const value = normalizeInterceptSettings(settings);
  try { await api.storage.sync?.set({ intercept: value }); } catch { /* sync is optional */ }
  try { await api.storage.local?.set({ intercept: value }); } catch { /* local is optional */ }
  return value;
}

if (typeof module !== "undefined") {
  module.exports = {
    extOf, isHttp, looksLikeMedia, isMediaContentType, MEDIA_EXTENSIONS,
    isManifest, MANIFEST_EXTENSIONS,
    formatBytes, probeSize, parseHlsMaster, estimateHlsSize,
    groupKey, extractQualityToken, runProbesBounded,
    isKnownUnsupportedHost, KNOWN_UNSUPPORTED_HOSTS,
    unsupportedSiteState, appCanHandlePage, askAppCanHandlePage, SITE_MEDIA_PLUGIN_NAME,
    isPlausibleMediaSize, MIN_MEDIA_BYTES,
    computeMainGroups, MAIN_WINDOW_MS,
    candidatePorts, discoverAppPort, APP_PORT_RANGE,
    captureCookies, mapCookie, sendToAppSilently, cookieUrlsFor,
    confirmAppFetching, browserUserAgent, appBase, appNotFoundMessage,
    shouldIntercept, normalizeInterceptSettings, hostMatchesSite, extOfName,
    candidateExts, isPlausiblePathExt,
    rememberResponseHeaders, recallResponseHeaders,
    RESPONSE_HEADER_CACHE_MAX, RESPONSE_HEADER_TTL_MS,
    filenameFromContentDisposition, filenameFromUrlQuery, extFromMime, resolveDownloadExt,
    MIME_EXTENSIONS,
    INTERCEPT_DEFAULTS, INTERCEPT_FILE_TYPES,
    getInterceptSettings, setInterceptSettings,
    postAdd, handOffToApp
  };
}
