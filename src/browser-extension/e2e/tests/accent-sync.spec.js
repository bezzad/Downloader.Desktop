// The popup wears the accent the APP is set to.
//
// Its palette used to be a hand-copied constant: choosing Blue in the app's settings left the
// extension teal for ever, and the two read as different products. The app reports the colour it is
// actually wearing over /api/settings, and the popup paints itself with it. Light vs dark deliberately
// stays with the browser — only the accent is followed.
const http = require("node:http");
const { test, expect, openPopupFor } = require("../fixtures");

const APP_PORT_RANGE = [15151, 15152, 15153, 15154, 15155];
// 15151 is excluded deliberately (as in the other specs): on a developer's machine the real app is
// usually there, and these tests must never talk to it.
const STUB_PORTS = APP_PORT_RANGE.slice(1);

async function appAnsweringInRange() {
  for (const port of APP_PORT_RANGE) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/ping`, { signal: AbortSignal.timeout(1500) });
      if (res.status > 0) return port;
    } catch { /* nothing there */ }
  }
  return null;
}

/** A stub app that answers /ping and reports one accent from /api/settings. */
function startAppStub(accentColor) {
  const server = http.createServer((req, res) => {
    if (req.url.startsWith("/ping")) {
      res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end("{}");
      return;
    }
    if (req.url.startsWith("/api/settings")) {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ defaultSavePath: "/tmp", version: "9.9.9", accentColor }));
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

const accentOf = popup => popup.evaluate(() =>
  getComputedStyle(document.documentElement).getPropertyValue("--accent").trim().toUpperCase());

test("the popup takes the accent the app reports", async ({ context, extensionId }) => {
  test.skip(await appAnsweringInRange() !== null, "a real app is listening — its real accent would be used");
  const app = await startAppStub("#2F7DE1"); // the app's Blue
  test.skip(!app, "no free port in the app range for the stub");
  try {
    await setCachedPort(context, app.port);
    const page = await context.newPage();
    await page.goto("/direct-quality.html");

    const popup = await openPopupFor(context, extensionId, page);
    await expect.poll(() => accentOf(popup), { timeout: 10000 }).toBe("#2F7DE1");

    // The fill's ink and the text colour move with it, or a light accent leaves unreadable labels.
    const ink = await popup.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue("--on-accent").trim().toUpperCase());
    expect(ink).toBe("#FFFFFF");
    const text = await popup.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue("--accent-text").trim().toUpperCase());
    expect(text).not.toBe("#2F7DE1"); // darkened until it reads on the light background
  } finally {
    await new Promise(r => app.server.close(r));
  }
});

test("an app that reports no accent leaves the stylesheet's own palette alone", async ({ context, extensionId }) => {
  test.skip(await appAnsweringInRange() !== null, "a real app is listening — its real accent would be used");
  // An older app has no such field; a broken one could answer with anything. Neither may end up in a
  // style, and neither may leave the popup unpainted.
  const app = await startAppStub("javascript:alert(1)");
  test.skip(!app, "no free port in the app range for the stub");
  try {
    await setCachedPort(context, app.port);
    const page = await context.newPage();
    await page.goto("/direct-quality.html");

    const popup = await openPopupFor(context, extensionId, page);
    await popup.waitForTimeout(1500);
    expect(await accentOf(popup)).toBe("#0E8FB3"); // popup.css's own default
  } finally {
    await new Promise(r => app.server.close(r));
  }
});
