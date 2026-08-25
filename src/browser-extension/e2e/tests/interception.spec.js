// Download interception (issue #9), end to end in a real browser with the real extension.
//
// The safety property under test is the ORDERING: the browser's own download is cancelled only after
// the app has accepted the hand-off. Every failure path must leave the browser download alone, because
// the alternative — a cancelled download that was never handed over — loses the user's file.
//
// A stub of the desktop app runs on the real loopback port the extension probes, so the extension is
// exercised exactly as it would be against the app: /ping for discovery, POST /api/add for the add.
const http = require("node:http");
const { test, expect } = require("../fixtures");

// The extension only ever talks to this declared range (MV3 host_permissions are static).
const APP_PORT_RANGE = [15151, 15152, 15153, 15154, 15155];

// 15151 is DELIBERATELY excluded. On a developer's machine the real Downloader app is usually
// listening there, and a stub that lost the race would send this test's downloads to it — adding real
// downloads to someone's actual app. The stub takes a later port and the test primes the extension's
// cached-port preference so discovery reaches the stub first; see setCachedPort below.
const STUB_PORTS = APP_PORT_RANGE.slice(1);

/** Is something already answering /ping in the range (i.e. a real app is running)? */
async function appAnsweringInRange() {
  for (const port of APP_PORT_RANGE) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/ping`, { signal: AbortSignal.timeout(1500) });
      if (res.status > 0) return port;
    } catch { /* nothing there */ }
  }
  return null;
}

/** Point the extension at our stub first. discoverAppPort probes the cached port before the rest. */
async function setCachedPort(context, port) {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(async p => { await chrome.storage.local.set({ appPort: p }); }, port);
}

/** Stub of the app's local API. `behavior` decides how it answers an add. */
function startStubApp(behavior = "accept") {
  const adds = [];
  const server = http.createServer((req, res) => {
    if (req.url.startsWith("/ping")) {
      res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end("{}");
      return;
    }
    if (req.url.startsWith("/api/add")) {
      let body = "";
      req.on("data", c => { body += c; });
      req.on("end", () => {
        let parsed = null;
        try { parsed = JSON.parse(body); } catch { /* the GET form has no body */ }
        adds.push({ url: req.url, body: parsed });
        if (behavior === "reject") {
          res.writeHead(500, { "Content-Type": "application/json" });
          res.end(JSON.stringify({ error: "the app could not add it" }));
          return;
        }
        res.writeHead(201, { "Content-Type": "application/json" });
        res.end(JSON.stringify({
          id: "11111111-1111-1111-1111-111111111111",
          name: "sample.zip",
          status: "Running",
          cookies: parsed?.cookies?.length ?? 0,
          headers: parsed?.headers ? Object.keys(parsed.headers).length : 0,
          referer: !!parsed?.referer
        }));
      });
      return;
    }
    res.writeHead(404).end();
  });
  // Take the first port in the range we can actually bind.
  return new Promise(resolve => {
    let i = 0;
    const tryNext = () => {
      if (i >= STUB_PORTS.length) return resolve(null); // every port busy — the test skips
      const port = STUB_PORTS[i++];
      server.listen(port, "127.0.0.1", () => resolve({ server, adds, port }));
    };
    server.on("error", tryNext); // EADDRINUSE — move along
    tryNext();
  });
}

/** Write the interception settings straight through the extension's own helper. */
async function setSettings(context, settings) {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(async s => { await setInterceptSettings(s); }, settings);
}

/**
 * Start a real browser download.
 *
 * Deliberately NOT a page navigation to an attachment: Playwright installs its own download
 * behaviour on pages it controls, so a navigation-triggered download never reaches
 * `chrome.downloads` and `onCreated` never fires — the event this whole feature hangs off. Asking
 * the browser's own download API to fetch the URL fires exactly the same event with the same
 * DownloadItem, so the extension's listener, rules, hand-off and cancel are all exercised for real.
 * The one difference is that `referrer` is empty, which is useful in itself: it exercises the
 * active-tab fallback that a direct navigation would otherwise hide.
 */
async function startBrowserDownload(context, url) {
  const [sw] = context.serviceWorkers();
  return sw.evaluate(u => chrome.downloads.download({ url: u }), url);
}

/** The browser's own view of the download, once it has one. */
async function downloadState(context, filenameIncludes) {
  const [sw] = context.serviceWorkers();
  for (let i = 0; i < 60; i++) {
    const found = await sw.evaluate(async needle => {
      const items = await chrome.downloads.search({});
      // Match on the URL *and* the filename: Playwright redirects downloads into its own artifacts
      // directory under a random name, so `filename` is truthy but never contains the real one.
      const hit = items.find(d => `${d.url || ""} ${d.filename || ""}`.includes(needle));
      return hit ? { state: hit.state, error: hit.error || null } : null;
    }, filenameIncludes);
    if (found) return found;
    await new Promise(r => setTimeout(r, 100));
  }
  return null;
}

test.describe("download interception", () => {
  let app;

  test.afterEach(async () => {
    if (app?.server) await new Promise(r => app.server.close(r));
    app = null;
  });

  test("an allow-listed download is handed to the app and the browser's copy is cancelled", async ({ context }) => {
    app = await startStubApp("accept");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);

    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html"); // the active tab, which is where the referer fallback comes from
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1&slow=1");

    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);

    // The hand-off went out on the POST form and carried the page context, not just the URL.
    const add = app.adds[0];
    expect(add.body).not.toBeNull();
    expect(add.body.url).toContain("sample.zip");
    expect(add.body.referer).toContain("127.0.0.1");

    // And only then was the browser's own download cancelled.
    const state = await downloadState(context, "sample.zip");
    expect(state).not.toBeNull();
    expect(state.state).toBe("interrupted");
    expect(state.error).toBe("USER_CANCELED");
  });

  test("a download the rules exclude is left entirely to the browser", async ({ context }) => {
    app = await startStubApp("accept");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);

    // Same download, but "zip" is not in the allow list this time.
    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["iso"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html"); // the active tab, which is where the referer fallback comes from
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1");

    const state = await downloadState(context, "sample.zip");
    expect(state).not.toBeNull();
    expect(state.state).not.toBe("interrupted"); // the browser kept it
    expect(app.adds.length).toBe(0);             // and nothing was handed over
  });

  test("interception off means the extension never touches the download", async ({ context }) => {
    app = await startStubApp("accept");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);

    await setSettings(context, { enabled: false, fileTypes: { mode: "allow", list: ["zip"] } });

    const page = await context.newPage();
    await page.goto("/empty.html"); // the active tab, which is where the referer fallback comes from
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1");

    const state = await downloadState(context, "sample.zip");
    expect(state).not.toBeNull();
    expect(state.state).not.toBe("interrupted");
    expect(app.adds.length).toBe(0);
  });

  test("when the app refuses the add, the browser download is left running", async ({ context }) => {
    // The worst case this feature can produce is a cancelled download that was never handed over.
    // The app answering 500 is the cheapest way to prove the ordering holds: the add is attempted,
    // refused, and the browser's copy survives — the user still gets the file.
    app = await startStubApp("reject");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);

    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html"); // the active tab, which is where the referer fallback comes from
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1");

    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0); // it was attempted

    const state = await downloadState(context, "sample.zip");
    expect(state).not.toBeNull();
    expect(state.state).not.toBe("interrupted"); // ...and refused, so the browser still has it
  });

  test("with the app unreachable the download proceeds untouched", async ({ context }) => {
    // No stub at all: discovery must fail on every port in the range. It cannot fail while a REAL
    // Downloader is listening (the usual case on a developer's machine), and handing this test's
    // download to someone's actual app would be worse than not running — so skip instead.
    const busy = await appAnsweringInRange();
    test.skip(busy != null, `a real app is answering on 127.0.0.1:${busy} — cannot simulate "unreachable"`);

    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html"); // the active tab, which is where the referer fallback comes from
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1");

    const state = await downloadState(context, "sample.zip");
    expect(state).not.toBeNull();
    expect(state.state).not.toBe("interrupted");
  });
});
