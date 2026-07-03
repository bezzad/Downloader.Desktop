// Node-runnable unit tests for the pure/mockable helpers in common.js.
// Run with:  node --test src/browser-extension/common.test.js
"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const {
  groupKey, extractQualityToken, parseHlsMaster, probeSize,
  runProbesBounded, formatBytes, isKnownUnsupportedHost
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
