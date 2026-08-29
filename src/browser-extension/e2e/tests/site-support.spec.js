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
function startAppStub({ handled, by }) {
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

test("with the plugin installed, the page itself is offered to the app", async ({ context, extensionId }) => {
  test.skip(await appAnsweringInRange() !== null, "a real app is listening — its real answer would be used");
  const app = await startAppStub({ handled: true, by: "Video sites (YouTube and others)" });
  test.skip(!app, "no free port in the app range for the stub");
  try {
    await setCachedPort(context, app.port);
    const page = await openBlockedSitePage(context);

    const popup = await openPopupFor(context, extensionId, page);
    await expect(popup.locator("#empty")).toBeVisible();
    await expect(popup.locator("#empty")).toContainText("Downloader can fetch this page");
    await expect(popup.locator("#empty")).toContainText("Video sites");
    // The offer must be actionable, not just words.
    await expect(popup.locator("#empty button")).toBeVisible();
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
