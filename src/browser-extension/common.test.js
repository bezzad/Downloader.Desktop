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
  runProbesBounded, formatBytes, shortVersion, isKnownUnsupportedHost,
  isPlausibleMediaSize, MIN_MEDIA_BYTES,
  sortDetectedGroups, groupTypeUrl, groupKnownSize, groupQualityHeight, leadsList,
  qualityHeight, qualityHeightFromUrl,
  buildThumbnailIndex, pickThumbnail, assignThumbnails, shotImage,
  getSavePath, setSavePath, fetchAppDefaultSavePath,
  candidatePorts, discoverAppPort, APP_PORT_RANGE, appNotFoundMessage,
  captureCookies, mapCookie, sendToAppSilently, cookieUrlsFor, confirmAppFetching, handOffUrls,
  extensionIdentity, browserLabel, withIdentity, withIdentityHeaders,
  isManifest, MEDIA_EXTENSIONS, looksLikeMedia, isMediaContentType,
  shouldIntercept, normalizeInterceptSettings, hostMatchesSite,
  filenameFromContentDisposition, filenameFromUrlQuery, extFromMime, resolveDownloadExt,
  candidateExts, isPlausiblePathExt,
  rememberResponseHeaders, recallResponseHeaders,
  RESPONSE_HEADER_CACHE_MAX, RESPONSE_HEADER_TTL_MS,
  INTERCEPT_DEFAULTS, INTERCEPT_FILE_TYPES, handOffToApp,
  unsupportedSiteState, appCanHandlePage, SITE_MEDIA_PLUGIN_NAME
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

test("shortVersion drops a trailing zero patch but keeps a real one", () => {
  assert.equal(shortVersion("1.7.0"), "1.7");
  assert.equal(shortVersion("1.7.1"), "1.7.1");
  assert.equal(shortVersion("2.0.0"), "2.0");   // only ONE trailing ".0" is stripped
  assert.equal(shortVersion("10"), "10");
  assert.equal(shortVersion(""), "");
  assert.equal(shortVersion(null), "");
  assert.equal(shortVersion(undefined), "");
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

// ---------------- One list: HLS first, then quality, then size ----------------
// Replaces the old "Main media vs Other detected" promotion tests (that rule needed a fresh
// visibility hint at the exact moment the popup asked, which on x.com it routinely was not) AND a
// first attempt at ordering purely by file type, which was ambiguous: type cannot say which copy of
// a video is the good one. Quality can, and size stands in for it when the link names no quality.

function group(key, opts) {
  return { key, kind: isManifest(key) ? "hls" : "direct", title: key, options: opts ?? [{ url: key, size: null }] };
}

test("qualityHeight reads the forms sites actually use", () => {
  assert.equal(qualityHeight("1080p"), 1080);
  assert.equal(qualityHeight("720P"), 720);
  assert.equal(qualityHeight("1920x1080"), 1080);   // the HLS variant label form
  assert.equal(qualityHeight("1280 × 720"), 720);
  assert.equal(qualityHeight("4k"), 2160);
  assert.equal(qualityHeight("2K"), 1440);
});

test("qualityHeight invents nothing for a relative word or a bare number", () => {
  // "hd"/"high"/"low" are relative to a stream we cannot see: ordering on a made-up number would be
  // worse than ordering on size, which is at least measured.
  for (const t of ["hd", "sd", "high", "low", "medium", "variant", "", null, undefined, 1080])
    assert.equal(qualityHeight(t), null, `expected no quality from ${JSON.stringify(t)}`);
  assert.equal(qualityHeight("2400 kbps"), null); // a bitrate is not a resolution
  assert.equal(qualityHeight("99p"), null);       // below any real rendition
  assert.equal(qualityHeight("9000p"), null);     // above any real rendition
});

test("a quality anywhere in the path counts, not just a trailing token", () => {
  assert.equal(qualityHeightFromUrl("https://c/hls/1080p/video.mp4"), 1080);
  assert.equal(qualityHeightFromUrl("https://c/v/1280x720/seg.ts"), 720);
  assert.equal(qualityHeightFromUrl("https://c/clip_720p.mp4"), 720);
  assert.equal(qualityHeightFromUrl("https://c/clip.mp4"), null);
  // A query string is not the file's identity — only the path is read.
  assert.equal(qualityHeightFromUrl("https://c/clip.mp4?label=1080p"), null);
});

test("an HLS master always leads the list", () => {
  const hls = group("https://c/master.m3u8");
  const big1080 = group("https://c/movie_1080p.mp4", [{ url: "https://c/movie_1080p.mp4", size: 900_000_000 }]);
  assert.deepEqual(sortDetectedGroups([big1080, hls]).map(g => g.key),
    ["https://c/master.m3u8", "https://c/movie_1080p.mp4"]);
});

test("after HLS, higher quality wins — regardless of type or size", () => {
  const webm1080 = group("https://c/a_1080p.webm", [{ url: "https://c/a_1080p.webm", size: 10_000_000 }]);
  const mp4_360 = group("https://c/b_360p.mp4", [{ url: "https://c/b_360p.mp4", size: 800_000_000 }]);
  // The 360p file is 80x bigger and an mp4; the 1080p webm is still the better copy.
  assert.deepEqual(sortDetectedGroups([mp4_360, webm1080]).map(g => g.key),
    ["https://c/a_1080p.webm", "https://c/b_360p.mp4"]);
});

test("with no quality to read, the bigger file wins", () => {
  const small = group("https://c/a.mp4", [{ url: "https://c/a.mp4", size: 2_000_000 }]);
  const big = group("https://c/b.mp3", [{ url: "https://c/b.mp3", size: 40_000_000 }]);
  assert.deepEqual(sortDetectedGroups([small, big]).map(g => g.key), ["https://c/b.mp3", "https://c/a.mp4"]);
});

test("a known quality outranks an unknown one even when the unknown file is bigger", () => {
  const known = group("https://c/a_720p.mp4", [{ url: "https://c/a_720p.mp4", size: 5_000_000 }]);
  const unknown = group("https://c/b.mp4", [{ url: "https://c/b.mp4", size: 500_000_000 }]);
  assert.deepEqual(sortDetectedGroups([unknown, known]).map(g => g.key), ["https://c/a_720p.mp4", "https://c/b.mp4"]);
});

test("a group is ranked by its BEST quality and its LARGEST size", () => {
  const grouped = group("https://c/v.mp4", [
    { url: "https://c/v_360p.mp4", size: 1_000_000 },
    { url: "https://c/v_1080p.mp4", size: 30_000_000 }
  ]);
  const other = group("https://c/w_720p.mp4", [{ url: "https://c/w_720p.mp4", size: 900_000_000 }]);
  assert.equal(groupQualityHeight(grouped), 1080);
  assert.equal(groupKnownSize(grouped), 30_000_000);
  assert.deepEqual(sortDetectedGroups([other, grouped]).map(g => g.key), ["https://c/v.mp4", "https://c/w_720p.mp4"]);
});

test("an HLS variant label supplies the quality once the master has been probed", () => {
  const probed = { key: "https://c/master.m3u8", kind: "hls", title: "master.m3u8", options: [
    { url: "https://c/low/index.m3u8", label: "640x360", size: 5_000_000 },
    { url: "https://c/high/index.m3u8", label: "1920x1080", size: 50_000_000 }
  ]};
  assert.equal(groupQualityHeight(probed), 1080);
});

test("unprobed, quality-less items come last, deterministically", () => {
  const probed = group("https://c/b.mp4", [{ url: "https://c/b.mp4", size: 5_000_000 }]);
  const unprobedA = group("https://c/a.mp4");
  const unprobedC = group("https://c/c.mp4");
  const sorted = sortDetectedGroups([unprobedC, probed, unprobedA]);
  assert.deepEqual(sorted.map(g => g.key), ["https://c/b.mp4", "https://c/a.mp4", "https://c/c.mp4"]);
  // Stable across calls: nothing here reads a clock or page state.
  assert.deepEqual(sortDetectedGroups(sorted).map(g => g.key), sorted.map(g => g.key));
});

test("sortDetectedGroups never mutates its input", () => {
  const input = [group("https://c/clip.mp4"), group("https://c/master.m3u8")];
  const before = input.map(g => g.key);
  sortDetectedGroups(input);
  assert.deepEqual(input.map(g => g.key), before);
});

test("leadsList/groupTypeUrl read a manifest from its key and a file from its first option", () => {
  assert.ok(leadsList(group("https://c/master.m3u8")));
  assert.equal(groupTypeUrl(group("https://c/master.m3u8")), "https://c/master.m3u8");
  const g = { key: "https://c/v.mp4", kind: "direct", options: [{ url: "https://c/v_720p.mp4" }] };
  assert.ok(!leadsList(g));
  assert.equal(groupTypeUrl(g), "https://c/v_720p.mp4");
  assert.equal(groupTypeUrl(null), "");
});

test("groupKnownSize is -1 until something has been probed", () => {
  assert.equal(groupKnownSize(group("https://c/a.mp4")), -1);
  assert.equal(groupKnownSize(group("https://c/a.mp4", [{ url: "https://c/a.mp4", size: 12 }])), 12);
});

// ---------------- Thumbnails ----------------

test("shotImage prefers a captured frame over a poster", () => {
  assert.equal(shotImage({ frame: "data:image/jpeg;base64,AAA", poster: "https://c/p.jpg" }), "data:image/jpeg;base64,AAA");
  assert.equal(shotImage({ frame: null, poster: "https://c/p.jpg" }), "https://c/p.jpg");
  assert.equal(shotImage({}), null);
  assert.equal(shotImage(null), null);
});

test("pickThumbnail prefers the matching element's own image over anything else", () => {
  const index = buildThumbnailIndex(
    [{ src: "https://c/clip.mp4", frame: "FRAME", area: 100 }],
    "https://c/og.jpg");
  assert.equal(pickThumbnail(index, group("https://c/clip.mp4")), "FRAME");
});

test("pickThumbnail matches through the group key, not just the exact option URL", () => {
  // The element plays one quality; the popup shows the merged group whose key drops the token.
  const index = buildThumbnailIndex([{ src: "https://c/v_720p.mp4", poster: "https://c/p.jpg", area: 9 }], null);
  const merged = { key: groupKey("https://c/v_720p.mp4"), kind: "direct", options: [{ url: "https://c/v_1080p.mp4" }] };
  assert.equal(pickThumbnail(index, merged), "https://c/p.jpg");
});

test("pickThumbnail returns null on an unmatched group — no page-image or other-element fallback", () => {
  // Falling back INSIDE pickThumbnail is exactly the v1.8.0 bug: every unmatched group would resolve
  // to the same single image. Fallback now only happens through assignThumbnails, one group at a time.
  const index = buildThumbnailIndex(
    [{ src: "blob:https://x.com/v", frame: "SOMEONE_ELSES_PHOTO", area: 900 }],
    "https://c/og.jpg");
  assert.equal(pickThumbnail(index, group("https://video.twimg.com/master.m3u8")), null);
  assert.equal(pickThumbnail(null, group("https://c/a.mp4")), null);
  assert.equal(pickThumbnail(index, null), null);
});

test("buildThumbnailIndex tolerates junk shots", () => {
  const index = buildThumbnailIndex([null, {}, { src: 42, frame: "F", area: "x" }], undefined);
  assert.deepEqual(index.queue, ["F"]);
  assert.equal(index.byUrl.size, 0); // a non-http src is never indexed
});

// ---------------- assignThumbnails: one distinct image per group, never a repeat ----------------
// The actual bug report: a feed page with several DIFFERENT videos (all blob: src, so none has an
// exact URL match) showed the SAME photo on every row — because the old pickThumbnail fell back to
// one shared "best" image for every group that asked. assignThumbnails hands out the captured images
// one at a time, in list order, so distinct videos get distinct photos.

test("two distinct unmatched videos on one page get two distinct photos, not the same one", () => {
  const index = buildThumbnailIndex([
    { src: "blob:https://x.com/1", frame: "PHOTO_A", area: 500 },
    { src: "blob:https://x.com/2", frame: "PHOTO_B", area: 400 }
  ], "https://c/og.jpg");
  const groups = [group("https://c/videoA.m3u8"), group("https://c/videoB.m3u8")];
  const assigned = assignThumbnails(index, groups);
  assert.equal(assigned.get("https://c/videoA.m3u8"), "PHOTO_A"); // largest first
  assert.equal(assigned.get("https://c/videoB.m3u8"), "PHOTO_B");
  assert.notEqual(assigned.get("https://c/videoA.m3u8"), assigned.get("https://c/videoB.m3u8"));
});

test("an exact match is never displaced by the queue", () => {
  const index = buildThumbnailIndex([
    { src: "https://c/known.mp4", frame: "KNOWN_FRAME", area: 10 },
    { src: "blob:https://x.com/1", frame: "QUEUE_PHOTO", area: 900 }
  ], null);
  const groups = [group("https://c/known.mp4"), group("https://c/other.m3u8")];
  const assigned = assignThumbnails(index, groups);
  assert.equal(assigned.get("https://c/known.mp4"), "KNOWN_FRAME"); // its own image, not the bigger queued one
  assert.equal(assigned.get("https://c/other.m3u8"), "QUEUE_PHOTO");
});

test("once the queue runs dry, later groups get null (placeholder), never a repeat", () => {
  const index = buildThumbnailIndex([{ src: "blob:https://x.com/1", frame: "ONLY_PHOTO", area: 500 }], null);
  const groups = [group("https://c/a.m3u8"), group("https://c/b.m3u8"), group("https://c/c.m3u8")];
  const assigned = assignThumbnails(index, groups);
  assert.equal(assigned.get("https://c/a.m3u8"), "ONLY_PHOTO");
  assert.equal(assigned.get("https://c/b.m3u8"), null);
  assert.equal(assigned.get("https://c/c.m3u8"), null);
});

test("the page image is used for at most ONE group, never repeated across a feed", () => {
  const index = buildThumbnailIndex([], "https://c/og.jpg"); // no captured elements at all
  const groups = [group("https://c/a.m3u8"), group("https://c/b.m3u8")];
  const assigned = assignThumbnails(index, groups);
  assert.equal(assigned.get("https://c/a.m3u8"), "https://c/og.jpg");
  assert.equal(assigned.get("https://c/b.m3u8"), null); // NOT the same og:image again
});

test("assignThumbnails is pure — calling it again reproduces the same assignment", () => {
  const index = buildThumbnailIndex([{ src: "blob:https://x.com/1", frame: "PHOTO", area: 500 }], "https://c/og.jpg");
  const groups = [group("https://c/a.m3u8"), group("https://c/b.m3u8")];
  const first = assignThumbnails(index, groups);
  const second = assignThumbnails(index, groups);
  assert.deepEqual([...first], [...second]);
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

test("sendToAppSilently carries the chosen quality in the GET form", async () => {
  // An HLS master's rendition is often video-only (its audio lives in a separate #EXT-X-MEDIA group),
  // so the popup hands over the MASTER plus the quality's id and lets the app expand it. A quality is
  // not a secret, so it travels in the query and does not force the POST form.
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };

  const result = await sendToAppSilently(
    "http://127.0.0.1:15151", "https://video.twimg.com/x/pl/master.m3u8", null, [], { variantId: "4800000" });

  assert.equal(result, "ok");
  assert.match(seen.endpoint, /\/api\/add\?url=/);
  assert.match(seen.endpoint, /[?&]variantId=4800000(&|$)/);
  assert.notEqual(seen.opts && seen.opts.method, "POST");
});

test("sendToAppSilently carries the chosen quality in the JSON form too", async () => {
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };
  const cookies = [{ name: "auth_token", value: "v", domain: ".x.com", path: "/", secure: true }];

  await sendToAppSilently(
    "http://127.0.0.1:15151", "https://video.twimg.com/x/pl/master.m3u8", null, cookies, { variantId: "2400000" });

  const body = JSON.parse(seen.opts.body);
  assert.equal(body.variantId, "2400000");
});

test("sendToAppSilently omits the quality when none was chosen", async () => {
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };

  await sendToAppSilently("http://127.0.0.1:15151", "https://example.com/a.zip", null, []);

  assert.ok(!seen.endpoint.includes("variantId"));
});

test("a DASH manifest is deliberately never surfaced", () => {
  // The app CAN download a .mpd (its streaming plugin handles DASH), but the popup cannot probe one
  // for a size or read a quality off it, so it could only ever be a nameless, sizeless row that the
  // ordering rule can say nothing about. It stays available by pasting the link.
  assert.ok(!MEDIA_EXTENSIONS.includes("mpd"));
  assert.equal(looksLikeMedia("https://cdn.example.com/stream/manifest.mpd"), false);
  assert.equal(isMediaContentType("application/dash+xml"), false);
  assert.equal(isManifest("https://cdn.example.com/s/manifest.mpd"), false);
});

test("an HLS master is recognised as a manifest, plain media is not", () => {
  assert.ok(isManifest("https://cdn.example.com/s/master.m3u8"));
  assert.equal(isManifest("https://cdn.example.com/s/movie.mp4"), false);
});

test("groupKey treats every manifest URL as its own group", () => {
  // A manifest's renditions are expanded by the app, so quality-token grouping must never merge two
  // manifests (or a manifest with a plain file) into one card.
  const a = "https://cdn.example.com/stream/video_720p.m3u8";
  const b = "https://cdn.example.com/stream/video_1080p.m3u8";
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

// Issue #9 follow-up: the reporter's APKPure downloads were never intercepted. The URL's last path
// segment is a PACKAGE NAME (`com.instagram.android`), whose trailing dotted run was read as the file
// type — a value that is both wrong and non-empty, so it masked every later source, including a
// perfectly correct MIME type.
test("an APKPure package-name path does not masquerade as a file type", () => {
  for (const url of [
    "https://d.apkpure.com/b/XAPK/com.instagram.android?version=latest",
    "https://d.apkpure.com/b/APK/com.whatsapp?version=latest",
    "https://d.cdnpure.com/b/XAPK/com.foo.bar?versionCode=123"
  ]) {
    const d = shouldIntercept({ url, filename: "", mime: "", size: 9e6 }, ON);
    assert.equal(d.reason, "type-unknown",
      `${url} names no type, so the reason must say so rather than inventing one`);
  }
});

test("an APKPure APK is intercepted on its MIME type, which the path must not mask", () => {
  const d = shouldIntercept({
    url: "https://d.apkpure.com/b/APK/com.whatsapp?version=latest",
    filename: "",
    mime: "application/vnd.android.package-archive",
    size: 9e6
  }, ON);
  assert.equal(d.intercept, true);
});

test("an APKPure XAPK is intercepted on the response's content-disposition", () => {
  // No MIME identifies .xapk, and the path is a package name — the response header is the only source.
  const d = shouldIntercept({
    url: "https://d.apkpure.com/b/XAPK/com.instagram.android?version=latest",
    filename: "",
    contentDisposition: 'attachment; filename="Instagram_v390.0.0.xapk"',
    mime: "application/octet-stream",
    size: 9e6
  }, ON);
  assert.equal(d.intercept, true);
});

test("a path extension is trusted when it is a type anything here recognises", () => {
  for (const ext of ["msi", "appimage", "zst", "iso", "mp4", "pdf"])
    assert.equal(isPlausiblePathExt(ext), true, `${ext} is a real extension`);
  for (const token of ["android", "whatsapp", "bar", "co", "com", "instagram", ""])
    assert.equal(isPlausiblePathExt(token), false, `${token} is not an extension`);
});

test("a type the user added by hand is trusted in a URL path", () => {
  const custom = { ...ON, fileTypes: { mode: "allow", list: ["mycustom"] } };
  const d = shouldIntercept({ url: "https://e.com/thing.mycustom", size: 9e6 }, custom);
  assert.equal(d.intercept, true);
});

test("candidateExts lists every source's answer, most trustworthy first, deduped", () => {
  const exts = candidateExts({
    url: "https://cdn.example.com/blob/xyz.zip?rscd=attachment%3Bfilename%3Dreal.7z",
    filename: "browser-said.exe",
    contentDisposition: 'attachment; filename="server-said.msi"',
    mime: "application/zip"
  });
  assert.deepEqual(exts, ["exe", "msi", "7z", "zip"]);
});

test("one source being wrong cannot veto a source that is right", () => {
  // The path says .zip (allowed) but the server names an .exe: both are candidates, and in deny
  // mode either one matching is enough to leave the download alone.
  const deny = { ...ON, fileTypes: { mode: "deny", list: ["exe"] } };
  const item = {
    url: "https://cdn.example.com/pkg.zip",
    contentDisposition: 'attachment; filename="setup.exe"',
    size: 9e6
  };
  assert.equal(shouldIntercept(item, deny).reason, "type-denied");
  // ...and in allow mode, a single matching candidate is enough to take it.
  const allowExe = { ...ON, fileTypes: { mode: "allow", list: ["exe"] } };
  assert.equal(shouldIntercept(item, allowExe).intercept, true);
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

// ---- Confirming the app is really fetching before cancelling the browser's copy (issue #9, 2b) ----
// A 201 from /api/add means "queued", not "reachable". Cancelling on it lost the user's file
// whenever the app then could not fetch the link.

/** Feed confirmAppFetching a scripted sequence of /api/list responses; no real clock, no real waits. */
function listReturning(rowsPerCall) {
  let i = 0;
  global.fetch = async () => {
    const rows = rowsPerCall[Math.min(i++, rowsPerCall.length - 1)];
    return { ok: true, json: async () => rows };
  };
}
const FAST = { timeoutMs: 50, intervalMs: 1 };

test("the hand-off is confirmed once bytes have actually arrived", async () => {
  listReturning([
    [{ id: "abc", status: "Running", size: 0, downloaded: 0 }],
    [{ id: "abc", status: "Running", size: 0, downloaded: 4096 }]
  ]);
  const r = await confirmAppFetching("http://127.0.0.1:15151", "abc", FAST);
  assert.deepEqual(r, { ok: true, reason: "confirmed" });
});

test("a known total size also confirms it — the engine only learns one from a real response", async () => {
  listReturning([[{ id: "abc", status: "Running", size: 1048576, downloaded: 0 }]]);
  const r = await confirmAppFetching("http://127.0.0.1:15151", "abc", FAST);
  assert.deepEqual(r, { ok: true, reason: "confirmed" });
});

test("status Running alone NEVER confirms — the app sets it before touching the network", async () => {
  listReturning([[{ id: "abc", status: "Running", size: 0, downloaded: 0 }]]);
  const r = await confirmAppFetching("http://127.0.0.1:15151", "abc", FAST);
  assert.equal(r.ok, false);
  assert.equal(r.reason, "timeout", "a queued-but-silent download must not license a cancel");
});

test("a reported failure ends the wait at once rather than timing out", async () => {
  let calls = 0;
  global.fetch = async () => {
    calls++;
    return { ok: true, json: async () => [{ id: "abc", status: "Failed", size: 0, downloaded: 0 }] };
  };
  const r = await confirmAppFetching("http://127.0.0.1:15151", "abc", { timeoutMs: 5000, intervalMs: 1 });
  assert.deepEqual(r, { ok: false, reason: "failed" });
  assert.equal(calls, 1, "it should not keep polling a download the app already gave up on");
});

test("an unreachable or malformed /api/list never throws, it just fails to confirm", async () => {
  global.fetch = async () => { throw new Error("ECONNREFUSED"); };
  assert.deepEqual(await confirmAppFetching("http://127.0.0.1:15151", "abc", FAST),
    { ok: false, reason: "timeout" });

  global.fetch = async () => ({ ok: true, json: async () => ({ not: "an array" }) });
  assert.deepEqual(await confirmAppFetching("http://127.0.0.1:15151", "abc", FAST),
    { ok: false, reason: "timeout" });

  // No id to look for means nothing can ever be confirmed.
  assert.deepEqual(await confirmAppFetching("http://127.0.0.1:15151", null, FAST),
    { ok: false, reason: "timeout" });
});

test("the hand-off carries the browser's User-Agent so a server that checks it isn't refused", async () => {
  global.chrome.cookies.getAll = async () => [];
  // `navigator` is a getter-only global in Node, so assert against whatever it really reports rather
  // than fabricating one — the behaviour under test is "the browser's own UA is forwarded".
  const expectedUa = typeof navigator !== "undefined" ? navigator.userAgent : "";
  assert.ok(expectedUa, "this Node build should expose navigator.userAgent");
  let seen = null;
  global.fetch = async (endpoint, opts) => {
    if (String(endpoint).endsWith("/ping")) return { ok: true, status: 200 };
    seen = JSON.parse(opts.body);
    return { ok: true, status: 201, json: async () => ({ id: "abc" }) };
  };

  const res = await handOffToApp("https://example.com/a.zip", "a.zip",
    { referer: "https://example.com/p", headers: { Referer: "https://example.com/p" } });

  assert.equal(res.ok, true);
  assert.equal(seen.headers["User-Agent"], expectedUa);
  assert.equal(seen.headers.Referer, "https://example.com/p", "the referer is still sent");
  assert.ok(res.port, "the port comes back so the caller can confirm the transfer");
});

test("a User-Agent the caller set explicitly is not overwritten", async () => {
  global.chrome.cookies.getAll = async () => [];
  let seen = null;
  global.fetch = async (endpoint, opts) => {
    if (String(endpoint).endsWith("/ping")) return { ok: true, status: 200 };
    seen = JSON.parse(opts.body);
    return { ok: true, status: 201, json: async () => ({ id: "abc" }) };
  };
  await handOffToApp("https://example.com/a.zip", "a.zip", { headers: { "user-agent": "Explicit/9" } });
  assert.equal(seen.headers["user-agent"], "Explicit/9");
  assert.equal(seen.headers["User-Agent"], undefined, "no duplicate under a different casing");
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

// ---------------- The extension's download folder ----------------
// The app must not ask where a download goes, and must not quietly use somewhere else. A folder set
// here travels with every hand-off; unset means "say nothing", i.e. exactly the old behaviour.

test("a configured folder travels in the GET form as `path`", async () => {
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };
  const result = await sendToAppSilently("http://127.0.0.1:15151", "https://e.com/a.zip", null, [],
    { savePath: "/home/me/Downloads" });
  assert.equal(result, "ok");
  assert.match(seen.endpoint, /\/api\/add\?url=/);           // still the plain GET path
  assert.match(seen.endpoint, /[?&]path=%2Fhome%2Fme%2FDownloads/);
});

test("a configured folder travels in the POST form too, alongside the context", async () => {
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };
  const cookies = [{ name: "SID", value: "v", domain: ".e.com", path: "/", secure: true, expires: 1893456000 }];
  await sendToAppSilently("http://127.0.0.1:15151", "https://e.com/a.zip", null, cookies,
    { savePath: "C:\\Users\\me\\Downloads" });
  assert.equal(seen.opts.method, "POST");
  assert.equal(JSON.parse(seen.opts.body).path, "C:\\Users\\me\\Downloads");
});

test("no configured folder means no `path` at all — the app applies its own setting", async () => {
  let seen = null;
  global.fetch = async (endpoint, opts) => { seen = { endpoint, opts }; return { ok: true, status: 201 }; };
  await sendToAppSilently("http://127.0.0.1:15151", "https://e.com/a.zip", null, [], { savePath: "   " });
  assert.doesNotMatch(seen.endpoint, /[?&]path=/);
  await sendToAppSilently("http://127.0.0.1:15151", "https://e.com/a.zip", null, [], {});
  assert.doesNotMatch(seen.endpoint, /[?&]path=/);
});

test("an intercepted hand-off carries the folder, and no image data", async () => {
  global.chrome.cookies.getAll = async () => [];
  let seen = null;
  global.fetch = async (endpoint, opts) => {
    if (String(endpoint).endsWith("/ping")) return { ok: true, status: 200 };
    seen = { endpoint, opts };
    return { ok: true, status: 201, json: async () => ({ id: "abc" }) };
  };
  const res = await handOffToApp("https://e.com/a.zip", "a.zip", { savePath: "/data/dl" });
  assert.equal(res.ok, true);
  const body = JSON.parse(seen.opts.body);
  assert.equal(body.path, "/data/dl");
  // A preview is a popup-only affordance: it must never reach the app or leave the machine.
  assert.deepEqual(Object.keys(body).filter(k => /thumb|image|frame|poster|preview/i.test(k)), []);
  assert.doesNotMatch(seen.opts.body, /data:image/);
});

test("a folder the app refuses is a failed send, never a silent success", async () => {
  // The app answers 400 for a path that isn't absolute. That must read as "fail" (not "fallback"),
  // because the interception path cancels the browser's own download only on a success.
  global.fetch = async () => ({ ok: false, status: 400 });
  const result = await sendToAppSilently("http://127.0.0.1:15151", "https://e.com/a.zip", null, [],
    { savePath: "relative/dir" });
  assert.equal(result, "fail");

  global.chrome.cookies.getAll = async () => [];
  global.fetch = async endpoint => String(endpoint).endsWith("/ping")
    ? { ok: true, status: 200 }
    : { ok: false, status: 400, json: async () => ({ error: "'path' must be an absolute folder path" }) };
  const handed = await handOffToApp("https://e.com/a.zip", "a.zip", { savePath: "relative/dir" });
  assert.equal(handed.ok, false);
  assert.equal(handed.reason, "app-rejected-400");
});

test("getSavePath/setSavePath round-trip through extension storage, trimmed", async () => {
  const store = {};
  global.chrome.storage = {
    local: {
      get: async defaults => ({ ...defaults, ...store }),
      set: async values => { Object.assign(store, values); }
    }
  };
  assert.equal(await getSavePath(), "");        // never configured
  setSavePath("  /home/me/Downloads  ");
  assert.equal(store.savePath, "/home/me/Downloads");
  assert.equal(await getSavePath(), "/home/me/Downloads");
  delete global.chrome.storage;
});

test("fetchAppDefaultSavePath reads the app's default, and is null when it can't", async () => {
  global.fetch = async endpoint => String(endpoint).endsWith("/api/settings")
    ? { ok: true, status: 200, json: async () => ({ defaultSavePath: "/home/me/Downloads", version: "2.8.2" }) }
    : { ok: true, status: 200 };
  assert.equal(await fetchAppDefaultSavePath(15151), "/home/me/Downloads");

  global.fetch = async () => ({ ok: false, status: 404 }); // an app too old for this endpoint
  assert.equal(await fetchAppDefaultSavePath(15151), null);

  global.fetch = async () => { throw new Error("app not running"); };
  assert.equal(await fetchAppDefaultSavePath(15151), null);

  global.fetch = async () => ({ ok: true, status: 200, json: async () => ({ defaultSavePath: "  " }) });
  assert.equal(await fetchAppDefaultSavePath(15151), null);
});

// ---- Response-header cache (issue #9: the only place an .xapk is ever named) ----

test("a response's content-disposition is recorded and read back", () => {
  const url = "https://d.apkpure.example/b/XAPK/com.example.app?v=1";
  rememberResponseHeaders(url, { contentDisposition: 'attachment; filename="App_v3.xapk"' });
  const seen = recallResponseHeaders(url);
  assert.equal(filenameFromContentDisposition(seen.contentDisposition), "App_v3.xapk");
  // ...and it makes the download interceptable, which is the whole point.
  const d = shouldIntercept({ url, contentDisposition: seen.contentDisposition, size: 9e6 }, ON);
  assert.equal(d.intercept, true);
});

test("a response that names nothing is not remembered", () => {
  const url = "https://example.com/page";
  rememberResponseHeaders(url, { contentType: "text/html; charset=utf-8" });
  assert.equal(recallResponseHeaders(url), null);
});

test("an identifying content type alone is worth remembering", () => {
  const url = "https://example.com/blob/opaque-id";
  rememberResponseHeaders(url, { contentType: "application/vnd.android.package-archive" });
  assert.equal(recallResponseHeaders(url).contentType, "application/vnd.android.package-archive");
});

test("the cache is bounded, dropping the least recently recorded", () => {
  for (let i = 0; i < RESPONSE_HEADER_CACHE_MAX + 25; i++)
    rememberResponseHeaders(`https://e.com/f${i}`, { contentDisposition: `attachment; filename=f${i}.zip` });
  assert.equal(recallResponseHeaders("https://e.com/f0"), null, "the oldest entry is gone");
  assert.ok(recallResponseHeaders(`https://e.com/f${RESPONSE_HEADER_CACHE_MAX + 24}`), "the newest is kept");
});

test("a stale entry is dropped rather than returned — a wrong name is worse than none", () => {
  const url = "https://e.com/old";
  rememberResponseHeaders(url, { contentDisposition: "attachment; filename=old.zip" }, 1000);
  assert.ok(recallResponseHeaders(url, 1000 + RESPONSE_HEADER_TTL_MS));
  assert.equal(recallResponseHeaders(url, 1001 + RESPONSE_HEADER_TTL_MS), null);
});

test("a cache miss is harmless — the decision falls back to the other sources", () => {
  assert.equal(recallResponseHeaders("https://never.seen/this"), null);
  const d = shouldIntercept({ url: "https://never.seen/thing.zip", size: 9e6 }, ON);
  assert.equal(d.intercept, true);
});

// The header cache exists so that reading the response never costs a new permission — a permission
// change would send the extension back through a full store review.
test("reading response headers needs no permission the extension did not already have", () => {
  const fs = require("node:fs");
  const expected = {
    permissions: ["contextMenus", "webRequest", "tabs", "scripting", "notifications", "storage",
      "cookies", "downloads"],
    hostPermissions: ["<all_urls>"]
  };
  for (const file of ["manifest.json", "manifest.firefox.json"]) {
    const m = JSON.parse(fs.readFileSync(`${__dirname}/${file}`, "utf8"));
    assert.deepEqual(m.permissions, expected.permissions, `${file} permissions changed`);
    assert.ok(m.host_permissions.includes("<all_urls>"), `${file} must already read every host`);
  }
});

// ---- The hand-off sends a link the app can resolve again (issue #9, Softpedia) ----

/** Capture what handOffToApp POSTs, with the app answering as v2.5.0+ does. */
async function captureHandOff(url, context) {
  const posted = [];
  global.fetch = async (u, opts) => {
    if (String(u).endsWith("/ping")) return { ok: true, status: 200 };
    posted.push(JSON.parse(opts.body));
    return { ok: true, status: 201, json: async () => ({ id: "1", cookies: 0, headers: 1, referer: true }) };
  };
  const result = await handOffToApp(url, "file.zip", context);
  return { result, body: posted[0] };
}

test("a redirected download hands over the clicked link, with the signed one as a mirror", async () => {
  const clicked = "https://www.softpedia.example/dyn-postdownload.php?p=999&t=abc";
  const signed = "https://cdn.softpedia.example/blob/6f2c1a?sig=one-shot";
  const { body } = await captureHandOff(clicked, { mirrors: [signed] });
  assert.equal(body.url, clicked, "the primary link must be the one that can be resolved again");
  assert.deepEqual(body.mirrors, [signed]);
});

test("a download that never redirected carries no mirrors", async () => {
  const url = "https://files.example.com/app.zip";
  const { body } = await captureHandOff(url, { mirrors: null });
  assert.equal(body.url, url);
  assert.equal(body.mirrors, undefined);
});

test("a mirror identical to the primary link is not sent twice", async () => {
  const url = "https://files.example.com/app.zip";
  const { body } = await captureHandOff(url, { mirrors: [url] });
  assert.equal(body.mirrors, undefined);
});

test("cookies are captured for the link actually being handed over", async () => {
  const asked = [];
  global.chrome.cookies.getAll = (details, cb) => { asked.push(details.url); cb([]); };
  const clicked = "https://www.softpedia.example/dyn-postdownload.php?p=999";
  await captureHandOff(clicked, { mirrors: ["https://cdn.example.com/blob/x"] });
  assert.ok(asked.includes(clicked), `cookies must be read for ${clicked}, asked for ${asked}`);
});

test("an intercepted hand-off tells the app the browser had already started it", async () => {
  const { body } = await captureHandOff("https://files.example.com/app.zip", {});
  assert.equal(body.fromBrowser, true);
});

test("the app-not-found message names the ports actually probed", () => {
  const msg = appNotFoundMessage();
  assert.match(msg, /15151/);
  assert.match(msg, new RegExp(String(APP_PORT_RANGE[APP_PORT_RANGE.length - 1])));
  assert.match(msg, /Downloader was not found/);
  // It must point at something the user can check, not just state a failure.
  assert.match(msg, /Browser integration/);
});


// ── What a "we can't capture this site" page actually says ───────────────────────────────────────
// The old message was a dead end, and the wording on the manual path told people to sign in — which
// they already were. What the page can do depends on which plugins the running app has, so the app
// is asked before anything is claimed (issue #9 follow-up).

test("a page the app can handle is offered as an item, with no message", () => {
  const state = unsupportedSiteState({
    hostUnsupported: true, appHandlesPage: true, handlerName: "Video sites (YouTube and others)"
  });
  assert.equal(state.mode, "offer");
  // No prose at all: the popup renders the page as an ordinary row with a Download button. A block of
  // explanatory red text standing in for the video item was the complaint this replaced.
  assert.equal(state.message, null);
  assert.equal(state.handler, "Video sites (YouTube and others)");
});

test("an offered page without a named handler still carries no message", () => {
  const state = unsupportedSiteState({ hostUnsupported: true, appHandlesPage: true, handlerName: null });
  assert.equal(state.mode, "offer");
  assert.equal(state.message, null);
  assert.equal(state.handler, null);
});

test("without the plugin the message names the plugin, never a sign-in", () => {
  const state = unsupportedSiteState({ hostUnsupported: true, appHandlesPage: false, handlerName: null });
  assert.equal(state.mode, "unsupported");
  assert.match(state.message, new RegExp(SITE_MEDIA_PLUGIN_NAME.replace(/[()]/g, "\\$&")));
  assert.match(state.message, /Settings/);
  // The whole point: people who saw the old wording WERE signed in. Saying it again is misleading.
  assert.doesNotMatch(state.message, /sign in|signed in|log in/i);
});

test("an ordinary site is left alone", () => {
  const state = unsupportedSiteState({ hostUnsupported: false, appHandlesPage: false, handlerName: null });
  assert.equal(state.mode, "normal");
  assert.equal(state.message, null);
});

test("asking the app what it can handle survives an old app, an error and no app at all", async () => {
  const realFetch = global.fetch;
  try {
    global.fetch = async () => ({ ok: true, json: async () => ({ handled: true, by: "Video sites (YouTube and others)" }) });
    assert.deepEqual(await appCanHandlePage("https://youtube.com/watch?v=a", 15151),
      { handled: true, by: "Video sites (YouTube and others)" });

    // An app older than this endpoint 404s — that must read as "no", exactly as before it existed.
    global.fetch = async () => ({ ok: false, status: 404, json: async () => ({}) });
    assert.deepEqual(await appCanHandlePage("https://youtube.com/watch?v=a", 15151), { handled: false, by: null });

    global.fetch = async () => { throw new Error("connection refused"); };
    assert.deepEqual(await appCanHandlePage("https://youtube.com/watch?v=a", 15151), { handled: false, by: null });

    // No port discovered at all — never even attempts a request.
    let called = false;
    global.fetch = async () => { called = true; };
    assert.deepEqual(await appCanHandlePage("https://youtube.com/watch?v=a", null), { handled: false, by: null });
    assert.equal(called, false);
  } finally {
    global.fetch = realFetch;
  }
});


// ── Which address an intercepted download hands over first ───────────────────────────────────────
// v2.8.0 led with the clicked link and kept the chain's end as a "fallback" nothing tried, which broke
// every site that serves the file from a different address than the page (issue #9). The app now tries
// both; this pins which one leads and that the pair is never sent twice.

test("the end of the redirect chain leads, with the clicked link as the fallback", () => {
  const { url, mirrors } = handOffUrls({
    url: "https://www.softpedia.example/dyn-postdownload.php?p=999",
    finalUrl: "https://cdn.example.com/blob/setup.exe"
  });
  assert.equal(url, "https://cdn.example.com/blob/setup.exe");
  assert.deepEqual(mirrors, ["https://www.softpedia.example/dyn-postdownload.php?p=999"]);
});

test("a download that was never redirected hands over one address", () => {
  const direct = "https://files.example.com/app.zip";
  const { url, mirrors } = handOffUrls({ url: direct, finalUrl: direct });
  assert.equal(url, direct);
  assert.equal(mirrors, null);
});

test("a missing or non-http chain end falls back to the clicked link alone", () => {
  const clicked = "https://files.example.com/app.zip";
  for (const finalUrl of [undefined, null, "", "blob:https://example.com/1234", "data:text/plain,hi"]) {
    const { url, mirrors } = handOffUrls({ url: clicked, finalUrl });
    assert.equal(url, clicked, `finalUrl=${finalUrl}`);
    assert.equal(mirrors, null, `finalUrl=${finalUrl}`);
  }
});

test("a non-http clicked link still lets the chain's end through", () => {
  const { url, mirrors } = handOffUrls({ url: "blob:https://example.com/x", finalUrl: "https://cdn/f.bin" });
  assert.equal(url, "https://cdn/f.bin");
  assert.equal(mirrors, null); // nothing usable to fall back to
});

test("a download with no usable address at all hands over nothing", () => {
  assert.deepEqual(handOffUrls({ url: "blob:x", finalUrl: null }), { url: null, mirrors: null });
  assert.deepEqual(handOffUrls({}), { url: null, mirrors: null });
  assert.deepEqual(handOffUrls(null), { url: null, mirrors: null });
});

// ---------------------------------------------------------------------------
// The two manifests must declare the same version.
//
// The code is shared — build-extension.sh packs the same common.js/popup.js into both zips — so a
// one-sided bump does not change behaviour, it just publishes a Chrome/Edge zip that LIES about its
// version. Nothing enforced this before: PUBLISHING.md asks for both, and the AMO workflow's bump guard
// only watches the Firefox manifest. The extension catalog now reads each target's version from its own
// manifest, which makes the agreement load-bearing.
// ---------------------------------------------------------------------------
const fs = require("node:fs");
const path = require("node:path");

function manifestVersion(file) {
  const full = path.join(__dirname, file);
  return { file, version: JSON.parse(fs.readFileSync(full, "utf8")).version };
}

test("both extension manifests declare the same version", () => {
  const chrome = manifestVersion("manifest.json");
  const firefox = manifestVersion("manifest.firefox.json");

  // Deliberately not compared against a hard-coded number — this compares the files to each other, so it
  // keeps working across every bump and only fails when they actually drift.
  assert.equal(
    chrome.version, firefox.version,
    `extension manifests disagree: ${chrome.file}=${chrome.version} vs ${firefox.file}=${firefox.version}. `
    + "Bump BOTH (see PUBLISHING.md) — the zips share their code, so a one-sided bump only mislabels one store's build."
  );
});

test("both manifests declare a plain semver version", () => {
  for (const file of ["manifest.json", "manifest.firefox.json"]) {
    const { version } = manifestVersion(file);
    assert.match(version, /^\d+\.\d+\.\d+$/, `${file} version "${version}" is not major.minor.patch`);
  }
});

// ---------------------------------------------------------------------------
// Telling the app which extension is talking to it.
//
// This rides on requests the extension already makes, so the rule that matters most is that it can never
// break one: an identity we cannot read must degrade to sending nothing, exactly like captureCookies
// returning [] rather than blocking a send.
// ---------------------------------------------------------------------------

// `api` is bound at load, so mutate the same object the module holds rather than reassigning global.chrome.
function withRuntime(manifest, fn) {
  const had = Object.prototype.hasOwnProperty.call(global.chrome, "runtime");
  const previous = global.chrome.runtime;
  const hadBrowser = Object.prototype.hasOwnProperty.call(globalThis, "browser");
  const previousBrowser = globalThis.browser;
  try {
    global.chrome.runtime = manifest === undefined ? undefined : { getManifest: () => manifest };
    delete globalThis.browser;
    return fn();
  } finally {
    if (had) global.chrome.runtime = previous; else delete global.chrome.runtime;
    if (hadBrowser) globalThis.browser = previousBrowser; else delete globalThis.browser;
  }
}

test("the identity carries the manifest version and a coarse browser label", () => {
  withRuntime({ version: "1.7.0" }, () => {
    const id = extensionIdentity();
    assert.equal(id.extVersion, "1.7.0");
    assert.ok(["chrome", "edge", "firefox"].includes(id.browser), `unexpected label ${id.browser}`);
    // A label, not a fingerprint — nothing beyond these two fields goes out.
    assert.deepEqual(Object.keys(id).sort(), ["browser", "extVersion"]);
  });
});

test("an unreadable manifest yields no identity instead of throwing", () => {
  // Every one of these is a request that must still go out unchanged.
  withRuntime(undefined, () => assert.deepEqual(extensionIdentity(), {}));
  withRuntime({}, () => assert.deepEqual(extensionIdentity(), {}));
  withRuntime({ version: "" }, () => assert.deepEqual(extensionIdentity(), {}));
  withRuntime({ version: 17 }, () => assert.deepEqual(extensionIdentity(), {}));

  const had = Object.prototype.hasOwnProperty.call(global.chrome, "runtime");
  const previous = global.chrome.runtime;
  try {
    global.chrome.runtime = { getManifest() { throw new Error("evicted"); } };
    assert.deepEqual(extensionIdentity(), {});
  } finally {
    if (had) global.chrome.runtime = previous; else delete global.chrome.runtime;
  }
});

test("Firefox is labelled by its own browser namespace", () => {
  const previous = globalThis.browser;
  const had = Object.prototype.hasOwnProperty.call(globalThis, "browser");
  try {
    globalThis.browser = { runtime: {} };
    assert.equal(browserLabel(), "firefox");
  } finally {
    if (had) globalThis.browser = previous; else delete globalThis.browser;
  }
});

test("withIdentity appends to a URL with or without an existing query", () => {
  withRuntime({ version: "1.7.0" }, () => {
    assert.match(withIdentity("http://127.0.0.1:15151/ping"), /\/ping\?extv=1\.7\.0&extb=/);
    assert.match(withIdentity("http://127.0.0.1:15151/api/add?url=x"), /\?url=x&extv=1\.7\.0&extb=/);
  });
});

test("withIdentity leaves the URL alone when there is no identity", () => {
  withRuntime(undefined, () => {
    assert.equal(withIdentity("http://127.0.0.1:15151/ping"), "http://127.0.0.1:15151/ping");
  });
});

test("withIdentityHeaders adds the header without disturbing existing ones", () => {
  withRuntime({ version: "1.7.0" }, () => {
    const init = withIdentityHeaders({ method: "POST", headers: { "Content-Type": "application/json" } });
    assert.equal(init.method, "POST");
    assert.equal(init.headers["Content-Type"], "application/json");
    assert.match(init.headers["X-Downloader-Extension"], /^1\.7\.0; (chrome|edge|firefox)$/);
  });
});

test("withIdentityHeaders returns the init untouched when there is no identity", () => {
  withRuntime(undefined, () => {
    const init = { method: "GET" };
    assert.deepEqual(withIdentityHeaders(init), init);
    assert.deepEqual(withIdentityHeaders(), {});
  });
});

test("a silent add carries the identity in its JSON body when it has a context", async () => {
  const seen = [];
  const previousFetch = global.fetch;
  global.fetch = async (url, init) => {
    seen.push({ url: String(url), init });
    return { ok: true, status: 201, json: async () => ({ id: "1" }) };
  };
  try {
    await withRuntime({ version: "1.7.0" }, () => sendToAppSilently(
      "http://127.0.0.1:15151", "https://example.com/f.zip", "f.zip",
      [{ domain: "example.com", name: "s", value: "1", path: "/" }], {}));
  } finally {
    global.fetch = previousFetch;
  }

  const body = JSON.parse(seen[0].init.body);
  assert.equal(body.extVersion, "1.7.0");
  assert.ok(body.browser);
  // Still a POST to /api/add — the identity must not change which endpoint or form is used.
  assert.equal(seen[0].url, "http://127.0.0.1:15151/api/add");
  assert.equal(seen[0].init.method, "POST");
});

test("a plain silent add keeps its GET form and gains the identity in the query", async () => {
  const seen = [];
  const previousFetch = global.fetch;
  global.fetch = async (url, init) => {
    seen.push({ url: String(url), init });
    return { ok: true, status: 201 };
  };
  try {
    await withRuntime({ version: "1.7.0" }, () => sendToAppSilently(
      "http://127.0.0.1:15151", "https://example.com/f.zip", null, [], {}));
  } finally {
    global.fetch = previousFetch;
  }

  // The GET path is the one every older caller used; it must stay a GET.
  assert.equal(seen[0].init.method, "GET");
  assert.match(seen[0].url, /^http:\/\/127\.0\.0\.1:15151\/api\/add\?url=/);
  assert.match(seen[0].url, /extv=1\.7\.0/);
});
