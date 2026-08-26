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
  isManifest, MEDIA_EXTENSIONS, looksLikeMedia, isMediaContentType,
  shouldIntercept, normalizeInterceptSettings, hostMatchesSite,
  filenameFromContentDisposition, filenameFromUrlQuery, extFromMime, resolveDownloadExt,
  INTERCEPT_DEFAULTS, INTERCEPT_FILE_TYPES, handOffToApp
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

// ---------------- Download interception (issue #9) ----------------

const ON = { ...INTERCEPT_DEFAULTS, enabled: true };

test("interception is off by default, so updating the extension changes nothing", () => {
  assert.equal(INTERCEPT_DEFAULTS.enabled, false);
  const d = shouldIntercept({ url: "https://example.com/a.zip", size: 9e6 }, INTERCEPT_DEFAULTS);
  assert.equal(d.intercept, false);
  assert.equal(d.reason, "disabled");
});

test("an allow-listed type on an ordinary link is intercepted", () => {
  for (const name of ["a.zip", "setup.exe", "disk.iso", "pkg.tar.gz"]) {
    const d = shouldIntercept({ url: `https://example.com/${name}`, size: 9e6 }, ON);
    assert.equal(d.intercept, true, `${name} should be intercepted`);
    assert.equal(d.reason, "ok");
  }
});

test("a type outside the allow list is left to the browser", () => {
  const d = shouldIntercept({ url: "https://example.com/page.pdf", size: 9e6 }, ON);
  assert.equal(d.intercept, false);
  assert.equal(d.reason, "type-not-allowed");
});

test("the browser's suggested filename beats the URL when deciding the type", () => {
  // A signed CDN link often has no extension in its path; Content-Disposition carries the real name.
  const d = shouldIntercept(
    { url: "https://cdn.example.com/download?id=42", filename: "installer.msi", size: 9e6 }, ON);
  assert.equal(d.intercept, true);
});

// ---- Type resolution for signed / extensionless links (issue #9 follow-up) ----
// The real-world shape: the URL path is an opaque blob id and the filename lives in a
// content-disposition query parameter. Before this, every such download looked typeless.

test("content-disposition parsing covers the shapes servers actually send", () => {
  const cases = [
    ["attachment; filename=Downloader-win-x64.zip", "Downloader-win-x64.zip"],
    ['attachment; filename="my file.exe"', "my file.exe"],
    ["attachment; filename*=UTF-8''report%20final.msi", "report final.msi"],
    ["attachment%3B%20filename%3Dapp.apk", "app.apk"],          // still percent-encoded
    ["attachment; filename=/tmp/nested/app.deb", "app.deb"],     // path stripped to the base name
    ["inline", ""],
    ["", ""],
    [null, ""]
  ];
  for (const [input, expected] of cases) {
    assert.equal(filenameFromContentDisposition(input), expected, `for ${JSON.stringify(input)}`);
  }
});

test("filename* wins over filename when a server sends both", () => {
  const v = "attachment; filename=fallback.zip; filename*=UTF-8''real.iso";
  assert.equal(filenameFromContentDisposition(v), "real.iso");
});

test("malformed content-disposition never throws, it just names nothing", () => {
  for (const junk of ["filename*=UTF-8''%E0%A4%A", "filename=", "%%%", "attachment;;;"]) {
    assert.doesNotThrow(() => filenameFromContentDisposition(junk));
  }
});

test("a filename is read from either content-disposition query parameter", () => {
  assert.equal(
    filenameFromUrlQuery("https://cdn.example.com/blob/abc?rscd=attachment%3B+filename%3Dapp.apk"),
    "app.apk");
  assert.equal(
    filenameFromUrlQuery("https://cdn.example.com/blob/abc?response-content-disposition=attachment%3B%20filename%3Dsetup.exe"),
    "setup.exe");
  assert.equal(filenameFromUrlQuery("https://cdn.example.com/blob/abc?sig=xyz"), "");
  assert.equal(filenameFromUrlQuery("not a url"), "");
});

test("a real GitHub release asset URL is intercepted despite having no extension in its path", () => {
  // Taken from an actual v2.6.1 asset redirect: the path is an opaque id and the name is in `rscd`.
  const url = "https://release-assets.githubusercontent.com/github-production-release-asset/830513186/"
    + "76697026-b00b-4edf-88eb-ae09b19e728e?sp=r&sv=2018-11-09"
    + "&rscd=attachment%3B+filename%3DDownloader-win-x64.zip"
    + "&rsct=application%2Foctet-stream&sig=abc%3D";
  assert.equal(new URL(url).pathname.includes("."), false, "the path really has no extension");

  const d = shouldIntercept({ url, filename: "", mime: "application/octet-stream", size: 9e6 }, ON);
  assert.equal(d.intercept, true);
  assert.equal(d.reason, "ok");
});

test("type sources are consulted most-trustworthy first", () => {
  const url = "https://cdn.example.com/blob/1?rscd=attachment%3B+filename%3Dfrom-query.iso";
  // The browser's suggested name outranks the query parameter.
  assert.equal(resolveDownloadExt({ url, filename: "from-browser.exe" }), "exe");
  // With no suggested name, the query parameter outranks the path.
  assert.equal(resolveDownloadExt({ url: url.replace("/blob/1", "/blob/1.bin") }), "iso");
  // MIME is the last resort only.
  assert.equal(
    resolveDownloadExt({ url: "https://e.com/x", mime: "application/vnd.android.package-archive" }),
    "apk");
  // Nothing names it.
  assert.equal(resolveDownloadExt({ url: "https://e.com/x", mime: "application/octet-stream" }), "");
});

test("a generic octet-stream identifies nothing, so it cannot trigger interception by itself", () => {
  assert.equal(extFromMime("application/octet-stream"), "");
  assert.equal(extFromMime("binary/octet-stream"), "");
  assert.equal(extFromMime(""), "");
  // Parameters after the type are ignored.
  assert.equal(extFromMime("application/zip; charset=binary"), "zip");
});

test("a download nothing can identify is left to the browser and says so", () => {
  const d = shouldIntercept(
    { url: "https://cdn.example.com/blob/opaque", mime: "application/octet-stream", size: 9e6 }, ON);
  assert.equal(d.intercept, false);
  assert.equal(d.reason, "type-unknown", "must be distinguishable from 'type-not-allowed'");
});

test("Android package types are intercepted by default", () => {
  for (const ext of ["apk", "xapk", "apks", "obb"]) {
    assert.ok(INTERCEPT_FILE_TYPES.includes(ext), `${ext} should be an allow-listed type`);
    const d = shouldIntercept({ url: `https://apkpure.example/app.${ext}`, size: 9e6 }, ON);
    assert.equal(d.intercept, true, `${ext} should be intercepted`);
  }
});

test("an APKPure-shaped signed link with the name only in the query is intercepted", () => {
  const url = "https://d.apkpure.example/b/XAPK/com.example.app?"
    + "response-content-disposition=attachment%3B%20filename%3Dcom.example.app.xapk&k=sig";
  const d = shouldIntercept({ url, filename: "", mime: "application/octet-stream", size: 9e6 }, ON);
  assert.equal(d.intercept, true);
});

test("a deny list intercepts everything except the listed types", () => {
  const deny = { ...ON, fileTypes: { mode: "deny", list: ["pdf"] } };
  assert.equal(shouldIntercept({ url: "https://e.com/a.pdf" }, deny).reason, "type-denied");
  assert.equal(shouldIntercept({ url: "https://e.com/a.bin" }, deny).intercept, true);
});

test("size below the minimum is left to the browser, above it is taken", () => {
  const min = { ...ON, minSizeBytes: 1048576 };
  assert.equal(shouldIntercept({ url: "https://e.com/a.zip", size: 5000 }, min).reason, "too-small");
  assert.equal(shouldIntercept({ url: "https://e.com/a.zip", size: 5e6 }, min).intercept, true);
});

test("an unknown size does NOT block interception", () => {
  // downloads.onCreated routinely reports -1/0 before the headers land. Reading that as "too small"
  // would make interception fail at random — the exact class of bug issue #9 is about.
  const min = { ...ON, minSizeBytes: 1048576 };
  for (const size of [undefined, null, 0, -1, NaN]) {
    assert.equal(shouldIntercept({ url: "https://e.com/a.zip", size }, min).intercept, true,
      `size ${size} should not block`);
  }
});

test("an excluded site is left alone, including subdomains and the referring page", () => {
  const ex = { ...ON, excludedSites: ["example.com"] };
  assert.equal(shouldIntercept({ url: "https://example.com/a.zip" }, ex).reason, "excluded-site");
  assert.equal(shouldIntercept({ url: "https://files.example.com/a.zip" }, ex).reason, "excluded-site");
  assert.equal(shouldIntercept({ url: "https://www.example.com/a.zip" }, ex).reason, "excluded-site");
  // The exclusion is about the page you're on, so a download it started is excluded too.
  assert.equal(
    shouldIntercept({ url: "https://cdn.other.com/a.zip", referrer: "https://example.com/p" }, ex).reason,
    "excluded-site");
  assert.equal(shouldIntercept({ url: "https://notexample.com/a.zip" }, ex).intercept, true);
});

test("non-http downloads are never intercepted", () => {
  // There is no URL the app could re-fetch, so taking one over would destroy the download outright.
  for (const url of ["blob:https://e.com/1234", "data:application/zip;base64,AAAA", "file:///tmp/a.zip", ""]) {
    assert.equal(shouldIntercept({ url, filename: "a.zip" }, ON).reason, "not-http");
  }
});

test("hostMatchesSite covers the host and its subdomains only", () => {
  assert.ok(hostMatchesSite("example.com", "example.com"));
  assert.ok(hostMatchesSite("a.b.example.com", "example.com"));
  assert.ok(hostMatchesSite("example.com", ".example.com"));
  assert.ok(!hostMatchesSite("badexample.com", "example.com"));
  assert.ok(!hostMatchesSite("example.com", ""));
});

test("stored settings are normalized, so a partial or broken object can't wedge interception", () => {
  const s = normalizeInterceptSettings(
    { enabled: true, fileTypes: { mode: "nonsense", list: [".ZIP", " exe "] }, minSizeBytes: -5 });
  assert.equal(s.fileTypes.mode, "allow");            // unknown mode falls back
  assert.deepEqual(s.fileTypes.list, ["zip", "exe"]); // leading dots and case normalized
  assert.equal(s.minSizeBytes, 0);                    // negative treated as no minimum
  assert.deepEqual(s.excludedSites, []);
  assert.equal(normalizeInterceptSettings(null).enabled, false);
  assert.equal(normalizeInterceptSettings("garbage").enabled, false);
});

// ---------------- Hand-off with the page's context (the issue #7 half) ----------------

test("handOffToApp POSTs the referer and headers alongside the cookies", async () => {
  global.chrome.cookies.getAll = async () => [
    { name: "SID", value: "v", domain: ".example.com", path: "/", secure: true, session: true }
  ];
  let seen = null;
  global.fetch = async (endpoint, opts) => {
    if (String(endpoint).endsWith("/ping")) return { ok: true, status: 200 };
    seen = { endpoint, opts };
    return { ok: true, status: 201, json: async () => ({ id: "abc", cookies: 1, headers: 1, referer: true }) };
  };

  const res = await handOffToApp("https://example.com/a.zip", "a.zip", {
    referer: "https://example.com/page",
    headers: { Referer: "https://example.com/page" }
  });

  assert.equal(res.ok, true);
  assert.equal(res.reason, "ok");
  assert.equal(seen.opts.method, "POST"); // never a GET — a session cookie has no business in a URL
  const body = JSON.parse(seen.opts.body);
  assert.equal(body.referer, "https://example.com/page");
  assert.equal(body.headers.Referer, "https://example.com/page");
  assert.equal(body.cookies[0].name, "SID");
  assert.equal(body.filename, "a.zip");
});

test("handOffToApp reports a hand-off whose context the app dropped", async () => {
  // A 201 whose accepted-cookie count is 0 means the session did not arrive: the download will run
  // but a gated file will fail. Reporting it is what makes that explainable instead of mysterious.
  global.chrome.cookies.getAll = async () => [
    { name: "SID", value: "v", domain: ".example.com", path: "/", session: true }
  ];
  global.fetch = async (endpoint) => String(endpoint).endsWith("/ping")
    ? { ok: true, status: 200 }
    : { ok: true, status: 201, json: async () => ({ id: "abc", cookies: 0, headers: 0, referer: false }) };

  const res = await handOffToApp("https://example.com/a.zip", null, { referer: "https://example.com/p" });
  assert.equal(res.ok, true);                 // the add DID succeed — the browser copy may be cancelled
  assert.equal(res.reason, "context-dropped");
  assert.equal(res.contextSent.cookies, 1);
});

test("an older app that reports no counts is not treated as having dropped the context", async () => {
  global.chrome.cookies.getAll = async () => [];
  global.fetch = async (endpoint) => String(endpoint).endsWith("/ping")
    ? { ok: true, status: 200 }
    : { ok: true, status: 201, json: async () => ({ id: "abc" }) };

  const res = await handOffToApp("https://example.com/a.zip", null, { referer: "https://example.com/p" });
  assert.equal(res.reason, "ok");
  assert.equal(res.accepted.cookies, null); // unknown, not zero
});

test("handOffToApp fails cleanly when the app is unreachable, so the browser keeps the download", async () => {
  global.chrome.cookies.getAll = async () => [];
  global.fetch = async () => { throw new Error("ECONNREFUSED"); };
  const res = await handOffToApp("https://example.com/a.zip", null, {});
  assert.equal(res.ok, false);
  assert.equal(res.reason, "app-unreachable");
});

test("a cookie-capture failure never stops the hand-off", async () => {
  global.chrome.cookies.getAll = async () => { throw new Error("no permission"); };
  let posted = false;
  global.fetch = async (endpoint) => {
    if (String(endpoint).endsWith("/ping")) return { ok: true, status: 200 };
    posted = true;
    return { ok: true, status: 201, json: async () => ({ id: "x", cookies: 0, headers: 0, referer: true }) };
  };
  const res = await handOffToApp("https://example.com/a.zip", null, { referer: "https://example.com/p" });
  assert.ok(posted, "the link must still be sent");
  assert.equal(res.ok, true);
  assert.equal(res.contextSent.cookies, 0);
});

test("handOffToApp refuses a non-http URL outright", async () => {
  const res = await handOffToApp("blob:https://example.com/1234", null, {});
  assert.equal(res.ok, false);
  assert.equal(res.reason, "not-http");
});

test("sendToAppSilently now carries a referer even when there are no cookies", async () => {
  // The right-click capture path gains the same context; previously it sent cookies only.
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };
  const result = await sendToAppSilently("http://127.0.0.1:15151", "https://e.com/a.zip", null, [],
    { referer: "https://e.com/page" });
  assert.equal(result, "ok");
  assert.equal(seen.opts.method, "POST");
  assert.equal(JSON.parse(seen.opts.body).referer, "https://e.com/page");
});
