// What the popup says when the desktop app cannot be found (issue #9).
//
// The reporter believed the extension only started working after upgrading the app, when nothing in
// the extension requires that version. The extension had no way to say "I looked and found nothing",
// so an app that simply wasn't listening was indistinguishable from a broken extension.
const http = require("node:http");
const { test, expect, openPopupFor } = require("../fixtures");

const APP_PORT_RANGE = [15151, 15152, 15153, 15154, 15155];
// 15151 is excluded deliberately, as in interception.spec.js: on a developer's machine the real app
// is usually there, and this test must never talk to it.
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

/** A stub that answers /ping, on the first port in the range it can bind. */
function startPingStub() {
  const server = http.createServer((req, res) => {
    if (req.url.startsWith("/ping")) {
      res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end("{}");
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

test("the popup says the app was not found, and which ports it tried", async ({ context, extensionId }) => {
  test.skip(await appAnsweringInRange() !== null, "a real app is listening — it would be found");

  const page = await context.newPage();
  await page.goto("/empty.html");

  const popup = await openPopupFor(context, extensionId, page);
  await expect(popup.locator("#appMissing")).toBeVisible();
  const text = await popup.locator("#appMissing").textContent();
  expect(text).toContain("15151");
  expect(text).toContain("15155");
  await expect(popup.locator("#status")).toHaveClass(/off/);
});

test("the message is gone once the app answers again", async ({ context, extensionId }) => {
  test.skip(await appAnsweringInRange() !== null, "a real app is listening on the range");
  const app = await startPingStub();
  test.skip(!app, "no free port in the app range for the stub");
  try {
    await setCachedPort(context, app.port);

    const page = await context.newPage();
    await page.goto("/empty.html");

    const popup = await openPopupFor(context, extensionId, page);
    await expect(popup.locator("#status")).toHaveClass(/on/);
    await expect(popup.locator("#appMissing")).toBeHidden();
  } finally {
    await new Promise(r => app.server.close(r));
  }
});
