// What the popup says on a site whose video can't be sniffed off the network (YouTube and the like).
//
// It used to be a dead end for everyone: one hard-coded message saying the site is unsupported. Whether
// that is true depends on the app — with the site-media plugin installed the app downloads the page
// itself — so the extension now asks (/api/can-handle) before deciding what to say, and offers the page
// when the answer is yes (issue #9 follow-up).
const http = require("node:http");
const { test, expect, openPopupFor } = require("../fixtures");

const APP_PORT_RANGE = [15151, 15152, 15153, 15154, 15155];
// 15151 is excluded deliberately (as in the other specs): on a developer's machine the real app is
// usually there, and these tests must never talk to it.
const STUB_PORTS = APP_PORT_RANGE.slice(1);

/** Is anything already answering /ping in the range (i.e. the real app is running here)? */
async function appAnsweringInRange() {
  for (const port of APP_PORT_RANGE) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/ping`, { signal: AbortSignal.timeout(1500) });
      if (res.status > 0) return port;
    } catch { /* nothing there */ }
  }
  return null;
}

/**
 * A stub app that answers /ping and /api/can-handle. `handled` decides which answer it gives, i.e.
 * whether this "install" has a plugin that claims video pages.
 */
function startAppStub({ handled, by, variants, adds }) {
  const server = http.createServer((req, res) => {
    if (req.url.startsWith("/ping")) {
      res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end("{}");
      return;
    }
    if (req.url.startsWith("/api/can-handle")) {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ handled, by: handled ? by : null }));
      return;
    }
    if (req.url.startsWith("/api/variants")) {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ variants: variants || [] }));
      return;
    }
    if (req.url.startsWith("/api/add")) {
      // With no cookies captured (nothing is signed in here) the extension uses the GET form, so the
      // add's fields arrive in the query — the POST form is read below for when there are.
      if (req.method === "GET") {
        const q = new URL(req.url, "http://127.0.0.1").searchParams;
        adds?.push(Object.fromEntries(q.entries()));
        res.writeHead(201, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ id: "1", name: "video", status: "Running" }));
        return;
      }
      let body = "";
      req.on("data", c => { body += c; });
      req.on("end", () => {
        try { adds?.push(JSON.parse(body || "{}")); } catch { adds?.push({}); }
        res.writeHead(201, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ id: "1", name: "video", status: "Running" }));
      });
      return;
    }
    res.writeHead(404).end();
  });
  return new Promise(resolve => {
    let i = 0;
    const tryNext = () => {
      if (i >= STUB_PORTS.length) return resolve(null);
      const port = STUB_PORTS[i++];
      server.listen(port, "127.0.0.1", () => resolve({ server, port }));
    };
    server.on("error", tryNext);
    tryNext();
  });
}

async function setCachedPort(context, port) {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(async p => { await chrome.storage.local.set({ appPort: p }); }, port);
}

/** Serve a stand-in for the site entirely from the mock layer — nothing leaves the machine. */
async function openBlockedSitePage(context) {
  await context.route("https://www.youtube.com/**", route =>
    route.fulfill({ contentType: "text/html", body: "<html><body>a video page</body></html>" }));
  const page = await context.newPage();
  await page.goto("https://www.youtube.com/watch?v=e2e-site-support");
  await page.waitForTimeout(300);
  return page;
}

test("with the plugin installed, the page itself is listed as a downloadable item", async ({ context, extensionId }) => {
  test.skip(await appAnsweringInRange() !== null, "a real app is listening — its real answer would be used");
  const app = await startAppStub({ handled: true, by: "Video sites (YouTube and others)" });
  test.skip(!app, "no free port in the app range for the stub");
  try {
    await setCachedPort(context, app.port);
    const page = await openBlockedSitePage(context);

    const popup = await openPopupFor(context, extensionId, page);
    // The page is an ITEM, not a notice: one ordinary row with a Download button, and no message at
    // all where the video belongs (the red block of text was the complaint this replaced).
    await expect(popup.locator("#list li")).toHaveCount(1);
    await expect(popup.locator("#list li button")).toHaveText("Download");
    await expect(popup.locator("#list li .size-line")).toContainText("Video sites");
    await expect(popup.locator("#empty")).toBeHidden();
  } finally {
    await new Promise(r => app.server.close(r));
  }
});

test("the page row offers the app's qualities, and the pick is what gets sent", async ({ context, extensionId }) => {
  test.skip(await appAnsweringInRange() !== null, "a real app is listening — its real answer would be used");
  const adds = [];
  const app = await startAppStub({
    handled: true,
    by: "Video sites (YouTube and others)",
    adds,
    variants: [
      { id: "1080", label: "1080p (≈120 MB)", size: 120000000, default: true, url: null },
      { id: "720", label: "720p (≈60 MB)", size: 60000000, default: false, url: null },
      { id: "audio", label: "Audio only (≈4 MB)", size: 4000000, default: false, url: null }
    ]
  });
  test.skip(!app, "no free port in the app range for the stub");
  try {
    await setCachedPort(context, app.port);
    const page = await openBlockedSitePage(context);

    const popup = await openPopupFor(context, extensionId, page);
    const select = popup.locator("#list li select.quality");
    await expect(select).toBeVisible();
    await expect(select.locator("option")).toHaveText([
      "1080p (≈120 MB)", "720p (≈60 MB)", "Audio only (≈4 MB)"
    ]);

    // What most people are after on a music video is the audio — so the pick has to survive the send.
    await select.selectOption({ index: 2 });
    await popup.locator("#list li button").click();
    await expect(popup.locator("#list li button")).toHaveText("Sent ✓");
    expect(adds.length).toBe(1);
    expect(adds[0].variantId).toBe("audio");
    expect(adds[0].url).toContain("youtube.com/watch");
  } finally {
    await new Promise(r => app.server.close(r));
  }
});

test("without the plugin, the message names the plugin and never says to sign in", async ({ context, extensionId }) => {
  test.skip(await appAnsweringInRange() !== null, "a real app is listening — its real answer would be used");
  const app = await startAppStub({ handled: false });
  test.skip(!app, "no free port in the app range for the stub");
  try {
    await setCachedPort(context, app.port);
    const page = await openBlockedSitePage(context);

    const popup = await openPopupFor(context, extensionId, page);
    await expect(popup.locator("#empty")).toBeVisible();
    await expect(popup.locator("#empty")).toHaveClass(/unsupported/);
    const text = await popup.locator("#empty").textContent();
    expect(text).toContain("Video sites (YouTube and others)");
    expect(text).toContain("Settings");
    // The people who saw the old wording were already signed in; repeating it sent them nowhere.
    expect(text).not.toMatch(/sign in|signed in/i);
    await expect(popup.locator("#empty button")).toHaveCount(0);
  } finally {
    await new Promise(r => app.server.close(r));
  }
});
