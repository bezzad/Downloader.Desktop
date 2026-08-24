// Node-runnable unit tests for the pure/mockable helpers in common.js.
// Run with:  node --test src/browser-extension/common.test.js
"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
// common.js binds `api = globalThis.browser || globalThis.chrome` at load — provide a mutable stub before
// requiring it so cookie tests can swap `chrome.cookies.getAll` per test (api holds this same object).
global.chrome = { cookies: {} };
const {
  groupKey, extractQualityToken, parseHlsMaster, probeSize,
  runProbesBounded, formatBytes, isKnownUnsupportedHost,
  isPlausibleMediaSize, MIN_MEDIA_BYTES, computeMainGroups, MAIN_WINDOW_MS,
  candidatePorts, discoverAppPort, APP_PORT_RANGE,
  captureCookies, mapCookie, sendToAppSilently, cookieUrlsFor,
  isManifest, MEDIA_EXTENSIONS, looksLikeMedia, isMediaContentType
} = require("./common.js");

function fakeHeaders(map) {
  return { get: k => (Object.prototype.hasOwnProperty.call(map, k.toLowerCase()) ? map[k.toLowerCase()] : null) };
}

test("groupKey merges same-basename quality variants", () => {
  const a = groupKey("https://cdn.example.com/videos/movie_720p.mp4");
  const b = groupKey("https://cdn.example.com/videos/movie_1080p.mp4");
  assert.equal(a, b);
});

test("groupKey never merges unrelated files", () => {
  assert.notEqual(
    groupKey("https://cdn.example.com/a.mp4"),
    groupKey("https://cdn.example.com/b.mp4")
  );
});

test("groupKey treats every .m3u8 URL as its own group", () => {
  const url = "https://cdn.example.com/stream/master.m3u8";
  assert.equal(groupKey(url), url);
});

test("extractQualityToken finds a trailing quality token", () => {
  assert.equal(extractQualityToken("https://x/video_720p.mp4"), "720p");
  assert.equal(extractQualityToken("https://x/video.hd.mp4"), "hd");
  assert.equal(extractQualityToken("https://x/video.mp4"), null);
});

test("parseHlsMaster extracts variants with resolution and bandwidth", async () => {
  const playlist = [
    "#EXTM3U",
    "#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360",
    "low/index.m3u8",
    "#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720",
    "high/index.m3u8"
  ].join("\n");
  global.fetch = async () => ({ ok: true, text: async () => playlist });
  const variants = await parseHlsMaster("https://cdn.example.com/stream/master.m3u8");
  assert.equal(variants.length, 2);
  assert.equal(variants[0].resolution, "640x360");
  assert.equal(variants[1].resolution, "1280x720");
  assert.ok(variants[1].uri.endsWith("/stream/high/index.m3u8"));
});

test("parseHlsMaster returns [] for a variant/media playlist (no STREAM-INF)", async () => {
  const playlist = ["#EXTM3U", "#EXTINF:10.0,", "seg0.ts", "#EXTINF:10.0,", "seg1.ts"].join("\n");
  global.fetch = async () => ({ ok: true, text: async () => playlist });
  const variants = await parseHlsMaster("https://cdn.example.com/stream/variant.m3u8");
  assert.deepEqual(variants, []);
});

test("parseHlsMaster returns [] when the fetch fails", async () => {
  global.fetch = async () => { throw new Error("network down"); };
  assert.deepEqual(await parseHlsMaster("https://cdn.example.com/x.m3u8"), []);
});

test("probeSize reads Content-Length from a HEAD response", async () => {
  global.fetch = async (url, opts) => {
    assert.equal(opts.method, "HEAD");
    return { ok: true, headers: fakeHeaders({ "content-length": "12345" }) };
  };
  assert.equal(await probeSize("https://cdn.example.com/a.mp4"), 12345);
});

test("probeSize falls back to a ranged GET when HEAD has no length", async () => {
  global.fetch = async (url, opts) => {
    if (opts.method === "HEAD") return { ok: true, headers: fakeHeaders({}) };
    assert.equal(opts.headers.Range, "bytes=0-0");
    return { ok: true, headers: fakeHeaders({ "content-range": "bytes 0-0/98765" }) };
  };
  assert.equal(await probeSize("https://cdn.example.com/a.mp4"), 98765);
});

test("probeSize returns null when both attempts fail", async () => {
  global.fetch = async () => { throw new Error("down"); };
  assert.equal(await probeSize("https://cdn.example.com/a.mp4"), null);
});

test("runProbesBounded resolves in order and tolerates throws/timeouts", async () => {
  const tasks = [
    async () => 1,
    async () => { throw new Error("boom"); },
    signal => new Promise((_, reject) => {
      signal.addEventListener("abort", () => reject(new Error("aborted")));
    })
  ];
  const results = await runProbesBounded(tasks, { concurrency: 2, timeoutMs: 30 });
  assert.deepEqual(results, [1, null, null]);
});

test("formatBytes renders human-readable sizes and rejects non-positive input", () => {
  assert.equal(formatBytes(500), "500 B");
  assert.equal(formatBytes(1536), "1.5 KB");
  assert.equal(formatBytes(0), null);
  assert.equal(formatBytes(-5), null);
  assert.equal(formatBytes(NaN), null);
});

test("isKnownUnsupportedHost matches known hosts and their subdomains", () => {
  assert.ok(isKnownUnsupportedHost("www.youtube.com"));
  assert.ok(isKnownUnsupportedHost("youtube.com"));
  assert.ok(isKnownUnsupportedHost("m.youtube.com"));
  assert.ok(!isKnownUnsupportedHost("example.com"));
  assert.ok(!isKnownUnsupportedHost(""));
  assert.ok(!isKnownUnsupportedHost(null));
});

test("isPlausibleMediaSize passes unprobed items and rejects only confirmed-tiny ones", () => {
  assert.ok(isPlausibleMediaSize(null));            // not probed yet — never pre-rejected
  assert.ok(isPlausibleMediaSize(MIN_MEDIA_BYTES));  // boundary is inclusive
  assert.ok(isPlausibleMediaSize(20 * 1024 * 1024)); // a real ~20MB stream
  assert.ok(!isPlausibleMediaSize(897));             // real junk size observed on x.com
  assert.ok(!isPlausibleMediaSize(0));
});

test("computeMainGroups promotes nothing without a fresh hint", () => {
  const items = [{ group: "a", capturedAt: 1000 }];
  assert.deepEqual(computeMainGroups(items, null, 2000), new Set());
  const staleHint = { atMs: 0 };
  assert.deepEqual(computeMainGroups(items, staleHint, 2000 + MAIN_WINDOW_MS + 1), new Set());
});

test("computeMainGroups promotes the group with the freshest activity", () => {
  const items = [
    { group: "old-ad", capturedAt: 1000 },
    { group: "the-video", capturedAt: 9000 }
  ];
  const hint = { atMs: 9200 }; // fresh relative to "now"
  const result = computeMainGroups(items, hint, 9500);
  assert.deepEqual(result, new Set(["the-video"]));
});

test("computeMainGroups promotes a paused-but-recently-loaded video (the x.com regression)", () => {
  // The video finished autoplaying and sits paused; its own last segment request is still the
  // most recent activity on the page — must be promoted even though nothing is "playing" right now.
  const items = [
    { group: "sidebar-ad.mp4", capturedAt: 500 },
    { group: "video-master.m3u8", capturedAt: 4800 },
    { group: "video-master.m3u8", capturedAt: 4950 } // a segment of the same group
  ];
  const hint = { atMs: 5100 }; // content.js's periodic re-check keeps this fresh while visible
  const result = computeMainGroups(items, hint, 5200);
  assert.deepEqual(result, new Set(["video-master.m3u8"]));
});

test("computeMainGroups can promote more than one near-simultaneous group", () => {
  const items = [
    { group: "a", capturedAt: 9000 },
    { group: "b", capturedAt: 9100 }
  ];
  const hint = { atMs: 9200 };
  const result = computeMainGroups(items, hint, 9300);
  assert.deepEqual(result, new Set(["a", "b"]));
});

// ---------------- App port discovery (range fallback) ----------------

test("candidatePorts puts the cached port first, then the rest of the range", () => {
  assert.deepEqual(candidatePorts(15153), [15153, 15151, 15152, 15154, 15155]);
});

test("candidatePorts ignores a cached port outside the declared range", () => {
  assert.deepEqual(candidatePorts(9999), APP_PORT_RANGE);
  assert.deepEqual(candidatePorts(null), APP_PORT_RANGE);
});

test("discoverAppPort returns the preferred port when it responds first", async () => {
  const probed = [];
  const probe = async port => { probed.push(port); return port === 15151; };
  const port = await discoverAppPort(probe, 15151);
  assert.equal(port, 15151);
  assert.deepEqual(probed, [15151]); // no extra probes once found
});

test("discoverAppPort finds a fallback port after the preferred fails", async () => {
  const probe = async port => port === 15153; // app fell back to 15153
  const port = await discoverAppPort(probe, 15151);
  assert.equal(port, 15153);
});

test("discoverAppPort tries the cached last-known-good port first", async () => {
  const probed = [];
  const probe = async port => { probed.push(port); return port === 15154; };
  const port = await discoverAppPort(probe, 15154);
  assert.equal(port, 15154);
  assert.deepEqual(probed, [15154]); // cache hit — single probe, no scan
});

test("discoverAppPort returns null when no port in the range answers", async () => {
  const probed = [];
  const probe = async port => { probed.push(port); return false; };
  const port = await discoverAppPort(probe, 15151);
  assert.equal(port, null);
  assert.deepEqual(probed, APP_PORT_RANGE); // scanned the whole declared range
});

// ---------------- Cookie hand-off (fix-hls-youtube-resolver §4) ----------------

test("mapCookie maps chrome shape and omits expires for session cookies", () => {
  const persistent = mapCookie({ name: "SID", value: "v", domain: ".youtube.com", path: "/", secure: true, expirationDate: 1893456000.7 });
  assert.deepEqual(persistent, { name: "SID", value: "v", domain: ".youtube.com", path: "/", secure: true, expires: 1893456000 });
  const session = mapCookie({ name: "PREF", value: "x", domain: "youtube.com", path: "/", secure: false, session: true });
  assert.equal(session.expires, undefined);
  assert.equal(session.secure, false);
});

test("captureCookies is attempted for the given URL and returns the mapped list", async () => {
  const asked = [];
  global.chrome.cookies.getAll = async ({ url }) => { asked.push(url); return [
    { name: "SID", value: "v", domain: ".youtube.com", path: "/", secure: true, expirationDate: 1893456000 }
  ]; };
  const cookies = await captureCookies("https://youtu.be/x");
  assert.equal(asked[0], "https://youtu.be/x"); // the link's own origin first
  assert.equal(cookies.length, 1);              // the same cookie from a sibling origin is deduped
  assert.equal(cookies[0].name, "SID");
});

test("cookieUrlsFor adds the session's sibling origin for short/alternate domains", () => {
  // A youtu.be link carries no youtube.com cookies — capturing only its own host hands the app an
  // empty jar and YouTube's bot check can never be passed.
  assert.deepEqual(cookieUrlsFor("https://youtu.be/8uiKr3U71RE"),
    ["https://youtu.be/8uiKr3U71RE", "https://www.youtube.com/"]);
  assert.deepEqual(cookieUrlsFor("https://www.youtube.com/watch?v=x"),
    ["https://www.youtube.com/watch?v=x", "https://www.youtube.com/"]);
  assert.deepEqual(cookieUrlsFor("https://x.com/u/status/1"),
    ["https://x.com/u/status/1", "https://twitter.com/"]);
});

test("cookieUrlsFor leaves an ordinary link alone", () => {
  assert.deepEqual(cookieUrlsFor("https://example.com/a.mp4"), ["https://example.com/a.mp4"]);
  assert.deepEqual(cookieUrlsFor("not a url"), ["not a url"]);
});

test("captureCookies merges cookies from the sibling origin without duplicating", async () => {
  global.chrome.cookies.getAll = async ({ url }) => url.includes("youtube.com")
    ? [{ name: "SID", value: "v", domain: ".youtube.com", path: "/", secure: true, expirationDate: 1893456000 },
       { name: "PREF", value: "hl=en", domain: ".youtube.com", path: "/", session: true }]
    : [{ name: "SID", value: "v", domain: ".youtube.com", path: "/", secure: true, expirationDate: 1893456000 }];
  const cookies = await captureCookies("https://youtu.be/x");
  assert.deepEqual(cookies.map(c => c.name).sort(), ["PREF", "SID"]);
});

test("captureCookies returns [] when the cookies API throws (send is never blocked)", async () => {
  global.chrome.cookies.getAll = async () => { throw new Error("no permission"); };
  const cookies = await captureCookies("https://youtu.be/x");
  assert.deepEqual(cookies, []);
});

test("captureCookies returns [] when the cookies API is unavailable", async () => {
  const saved = global.chrome.cookies.getAll;
  delete global.chrome.cookies.getAll;
  const cookies = await captureCookies("https://youtu.be/x");
  assert.deepEqual(cookies, []);
  global.chrome.cookies.getAll = saved;
});

test("sendToAppSilently POSTs JSON with the app's cookie shape when cookies are present", async () => {
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };
  const cookies = [{ name: "SID", value: "v", domain: ".youtube.com", path: "/", secure: true, expires: 1893456000 }];
  const result = await sendToAppSilently("http://127.0.0.1:15151", "https://youtu.be/x", null, cookies);
  assert.equal(result, "ok");
  assert.equal(seen.endpoint, "http://127.0.0.1:15151/api/add");
  assert.equal(seen.opts.method, "POST");
  const body = JSON.parse(seen.opts.body);
  assert.equal(body.url, "https://youtu.be/x");
  assert.equal(body.cookies[0].name, "SID");
  assert.equal(body.cookies[0].value, "v");
});

test("sendToAppSilently keeps the URL-only GET path when no cookies are captured", async () => {
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };
  const result = await sendToAppSilently("http://127.0.0.1:15151", "https://example.com/a.zip", null, []);
  assert.equal(result, "ok");
  assert.match(seen.endpoint, /\/api\/add\?url=/);
  assert.notEqual(seen.opts && seen.opts.method, "POST");
});

test("a DASH manifest counts as media", () => {
  assert.ok(MEDIA_EXTENSIONS.includes("mpd"));
  assert.ok(looksLikeMedia("https://cdn.example.com/stream/manifest.mpd"));
  assert.ok(looksLikeMedia("https://cdn.example.com/stream/manifest.mpd?token=abc"));
  assert.ok(isMediaContentType("application/dash+xml"));
});

test("manifests are recognised as manifests, plain media is not", () => {
  assert.ok(isManifest("https://cdn.example.com/s/manifest.mpd"));
  assert.ok(isManifest("https://cdn.example.com/s/master.m3u8"));
  assert.equal(isManifest("https://cdn.example.com/s/movie.mp4"), false);
});

test("groupKey treats every .mpd URL as its own group", () => {
  // A DASH manifest's representations are expanded by the app, so quality-token grouping must never
  // merge two manifests (or a manifest with a plain file) into one card.
  const a = "https://cdn.example.com/stream/video_720p.mpd";
  const b = "https://cdn.example.com/stream/video_1080p.mpd";
  assert.equal(groupKey(a), a);
  assert.equal(groupKey(b), b);
  assert.notEqual(groupKey(a), groupKey(b));
});
