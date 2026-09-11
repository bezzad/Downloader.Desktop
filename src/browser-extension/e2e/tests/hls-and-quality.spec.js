const http = require("node:http");
const { test, expect, openPopupFor } = require("../fixtures");

// 15151 is deliberately excluded: a developer's real app usually listens there and would receive
// this test's add. The stub takes a later port in the declared range and the extension's cached-port
// preference points discovery at it first (same approach as interception.spec.js).
const STUB_PORTS = [15152, 15153, 15154, 15155];

/**
 * Is something already answering /ping in the range, i.e. is a real Downloader running? Then this test
 * MUST bow out: the kernel allows a second listener on a port the app already holds (SO_REUSEPORT), so
 * a stub can bind 15152 alongside the app and the extension's request goes to whichever the kernel
 * picks — observed here, with the add landing in the developer's real app. Same guard, same reason, as
 * interception.spec.js.
 */
async function appAnsweringInRange() {
  for (const port of [15151, ...STUB_PORTS]) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/ping`, { signal: AbortSignal.timeout(1500) });
      if (res.status > 0) return port;
    } catch { /* nothing there */ }
  }
  return null;
}

/** Minimal stub of the app's local API: answers /ping for discovery and records every add. */
function startAddRecorder() {
  const adds = [];
  const server = http.createServer((req, res) => {
    if (req.url.startsWith("/ping")) {
      res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end("{}");
      return;
    }
    if (req.url.startsWith("/api/add") || req.url.startsWith("/add")) {
      let body = "";
      req.on("data", c => { body += c; });
      req.on("end", () => {
        let parsed = null;
        try { parsed = JSON.parse(body); } catch { /* the GET form has no body */ }
        adds.push({ url: req.url, body: parsed });
        res.writeHead(201, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ id: "11111111-1111-1111-1111-111111111111", name: "master.mp4" }));
      });
      return;
    }
    res.writeHead(404);
    res.end();
  });
  return new Promise(resolve => {
    const tryPort = i => {
      if (i >= STUB_PORTS.length) return resolve(null);
      server.once("error", () => tryPort(i + 1));
      server.listen(STUB_PORTS[i], "127.0.0.1", () => resolve({ server, port: STUB_PORTS[i], adds }));
    };
    tryPort(0);
  });
}

async function setCachedPort(context, port) {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(async p => { await chrome.storage.local.set({ appPort: p }); }, port);
}

test("HLS master expands into a quality picker with an estimated size, with no duplicate variant/segment cards", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/hls-playing.html");
  await page.waitForTimeout(1500); // let the fetches land

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000); // getMedia + probeMedia round trip

  const cards = popup.locator("#list li");
  // Exactly ONE card: the master. Its variant playlists (low/high index.m3u8) and segment
  // (low/seg0.ts) are all independently sniffed network responses too, but must be represented
  // by the master's quality picker, not show up as their own redundant cards (real-world
  // regression: this used to produce 3+ near-duplicate cards for one video).
  await expect(cards).toHaveCount(1);

  const select = cards.first().locator("select.quality");
  const optionTexts = await select.locator("option").allTextContents();
  expect(optionTexts.some(t => t.includes("320x240"))).toBeTruthy();
  expect(optionTexts.some(t => t.includes("640x480"))).toBeTruthy();

  await expect(cards.first().locator(".size-val")).toContainText("~"); // HLS = always an estimate
});

test("an implausibly tiny junk .m3u8 is filtered out entirely", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/hls-playing.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  await expect(popup.locator("li", { hasText: "junk.m3u8" })).toHaveCount(0);
});

test("direct-file quality variants are grouped into one card", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/direct-quality.html");
  await page.waitForTimeout(1000);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  const cards = popup.locator("#list li");
  await expect(cards).toHaveCount(1); // both qualities grouped into ONE card
  await expect(cards.first().locator("select.quality option")).toHaveCount(2);
});

test("choosing a quality sends the MASTER plus that quality's id, not the rendition URL", async ({ context, extensionId }) => {
  // The bug this pins (reported on x.com): the picker used to send the chosen rendition's own URL.
  // A rendition of a master that keeps its audio in a separate #EXT-X-MEDIA group is VIDEO ONLY, so
  // the app downloaded a file with no sound. The app has to receive the master to be able to attach
  // the audio track, plus the id of the quality the user picked.
  const running = await appAnsweringInRange();
  test.skip(running != null, `a real app is answering on ${running} — it would receive this test's add`);
  const app = await startAddRecorder();
  test.skip(!app, "no free port in the app range for the stub");
  try {
    await setCachedPort(context, app.port);

    const page = await context.newPage();
    await page.goto("/hls-playing.html");
    await page.waitForTimeout(1500);

    const popup = await openPopupFor(context, extensionId, page);
    await popup.waitForTimeout(3000);

    const card = popup.locator("#list li").first();
    const select = card.locator("select.quality");
    // The picker lists the master's variants in playlist order; pick the 640x480 one by its label.
    const labels = await select.locator("option").allTextContents();
    const wanted = labels.findIndex(t => t.includes("640x480"));
    expect(wanted).toBeGreaterThanOrEqual(0);
    await select.selectOption({ index: wanted });
    await card.locator("button.row-action").click();

    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);
    const sent = app.adds[0].url;
    expect(decodeURIComponent(sent)).toContain("master.m3u8");
    expect(decodeURIComponent(sent)).not.toContain("high/index.m3u8");
    expect(sent).toContain("variantId=1200000"); // the 640x480 variant's BANDWIDTH
  } finally {
    await new Promise(r => app.server.close(r));
  }
});

test("a rendition the master does not list is dropped, not offered beside it", async ({ context, extensionId }) => {
  // The "downloaded without sound" report (x.com): the popup showed the rendition as its own row,
  // ABOVE the master — its URL names 720x1280 while a master's names no resolution at all — so the
  // top row, the one that gets clicked, was the video-only copy. The child-URI dedup could not help
  // here because the master does not list this URL.
  const page = await context.newPage();
  await page.goto("/hls-orphan-rendition.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  await expect(popup.locator("#list li")).toHaveCount(1);
  await expect(popup.locator("#list li").first()).toContainText("master.m3u8");
  await expect(popup.locator("li", { hasText: "rogue.m3u8" })).toHaveCount(0);
  // The audio rendition too. Dropping only the video one is what made the audio track the top row,
  // so the next download arrived with sound and no picture.
  await expect(popup.locator("li", { hasText: "aud.m3u8" })).toHaveCount(0);
});

test("a rendition with no master anywhere is still offered, and says it may have no sound", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/hls-rendition-only.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  const cards = popup.locator("#list li");
  await expect(cards).toHaveCount(1);
  await expect(cards.first().locator(".size-line")).toContainText("may have no sound");
});

test("an audio-only rendition with no master says it is audio, not video", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/hls-audio-only.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  const cards = popup.locator("#list li");
  await expect(cards).toHaveCount(1);
  await expect(cards.first().locator(".size-line")).toContainText("Audio track only");
});

test("an audio-only rendition is listed BELOW a real video file, never above it", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/hls-audio-vs-file.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  // Read the list AS FIRST RENDERED, before any probe result lands. That window is the one that
  // matters: the popup shows rows immediately and the user clicks the top one, while the manifest
  // probes are still in flight (they get 8s, and there are usually many of them on a feed page).
  // Until a probe answers, the URL is the only thing that can say this playlist is an audio track.
  // The stalled fetch keeps it that way for the length of the assertion.
  await expect(popup.locator("#list li")).toHaveCount(2);
  const titles = await popup.locator("#list li .name").allTextContents();
  const video = titles.findIndex(t => t.includes("movie_720p.mp4"));
  const audio = titles.findIndex(t => t.includes("aud.m3u8"));
  expect(video).toBeGreaterThanOrEqual(0);
  expect(audio).toBeGreaterThanOrEqual(0);
  expect(video).toBeLessThan(audio);
});
