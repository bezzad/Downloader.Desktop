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

/**
 * Stub of the app's local API. `behavior` decides how it answers an add:
 *   "accept"  — 201, and /api/list then reports bytes arriving (the app really is fetching)
 *   "reject"  — 500 on the add
 *   "stalled" — 201, but /api/list never reports progress. This is the Softpedia shape: the app
 *               queued the download and its own request never got anywhere. The browser's copy must
 *               survive it.
 *   "failing" — 201, then /api/list reports the download Failed.
 *   "confirm-added"     — 202 + ticket (the app opened its Add dialog), then the user confirms.
 *   "confirm-cancelled" — 202 + ticket, then the user cancels. Nothing was added, so the browser's
 *                         own download must survive untouched (issue #13).
 */
function startStubApp(behavior = "accept") {
  const adds = [];
  const ADD_ID = "11111111-1111-1111-1111-111111111111";
  const confirming = behavior.startsWith("confirm-");
  const server = http.createServer((req, res) => {
    // Before /api/add — it is a prefix of this path.
    if (req.url.startsWith("/api/add-status")) {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify(behavior === "confirm-added"
        ? { ticket: "T1", state: "added", id: ADD_ID }
        : { ticket: "T1", state: "cancelled" }));
      return;
    }
    if (req.url.startsWith("/ping")) {
      res.writeHead(200, { "Content-Type": "application/json", "Access-Control-Allow-Origin": "*" });
      res.end("{}");
      return;
    }
    if (req.url.startsWith("/api/list")) {
      const row = { id: ADD_ID, name: "sample.zip", status: "Running", size: 0, downloaded: 0 };
      if (behavior === "accept" || behavior === "confirm-added") { row.size = 200120; row.downloaded = 8192; }
      if (behavior === "failing") { row.status = "Failed"; }
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify(adds.length ? [row] : []));
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
        if (confirming) {
          // The app did NOT add anything: it opened its Add dialog and handed back a ticket.
          res.writeHead(202, { "Content-Type": "application/json" });
          res.end(JSON.stringify({ ticket: "T1" }));
          return;
        }
        res.writeHead(201, { "Content-Type": "application/json" });
        res.end(JSON.stringify({
          id: ADD_ID,
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

/** Set the popup's silent-vs-dialog choice through the extension's own helper. */
async function setMode(context, mode) {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(m => { setAddMode(m); }, mode);
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

/** Record the notifications the extension raises, so "the user was told" can be asserted. */
async function recordNotifications(context) {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(() => {
    globalThis.__seenNotifications = [];
    const real = chrome.notifications.create.bind(chrome.notifications);
    chrome.notifications.create = (opts, cb) => {
      globalThis.__seenNotifications.push({ title: opts?.title || "", message: opts?.message || "" });
      return real(opts, cb);
    };
  });
  return () => sw.evaluate(() => globalThis.__seenNotifications);
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
    // The app is told the browser had already started this, which is what lets it read a first-request
    // failure as a spent single-use link rather than a bad one (issue #9, Softpedia).
    expect(add.body.fromBrowser).toBe(true);

    // And only then was the browser's own download cancelled.
    const state = await downloadState(context, "sample.zip");
    expect(state).not.toBeNull();
    expect(state.state).toBe("interrupted");
    expect(state.error).toBe("USER_CANCELED");
  });

  // Issue #13: the popup's "Add silently" toggle was ignored on this path — an intercepted download
  // was always added and started with no dialog. In dialog mode the hand-off must ASK first, and the
  // browser's own copy may only be cancelled once the user has actually confirmed it.
  test("in dialog mode an intercepted download asks the user before it is taken over", async ({ context }) => {
    app = await startStubApp("confirm-added");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);
    await setMode(context, "dialog");
    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html");
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1&slow=1&confirmed=1");

    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);

    // It asked — and it asked over /api/add, so the dialog opens carrying the context a URL-only
    // endpoint would have thrown away.
    const add = app.adds[0];
    expect(add.body.confirm).toBe(true);
    expect(add.body.url).toContain("sample.zip");
    expect(add.body.referer).toContain("127.0.0.1");
    expect(add.body.fromBrowser).toBe(true);

    // The user confirmed, so the take-over completes exactly as a silent one does.
    const state = await downloadState(context, "confirmed=1");
    expect(state).not.toBeNull();
    expect(state.state).toBe("interrupted");
    expect(state.error).toBe("USER_CANCELED");
  });

  test("cancelling the dialog leaves the browser's own download running", async ({ context }) => {
    app = await startStubApp("confirm-cancelled");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);
    await setMode(context, "dialog");
    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html");
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1&slow=1&declined=1");

    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);
    expect(app.adds[0].body.confirm).toBe(true);

    // Nothing was added, so nothing may be cancelled — the user keeps the file they were already
    // getting. This is the same branch every other "the app didn't take it" failure lands in.
    const state = await downloadState(context, "declined=1");
    expect(state).not.toBeNull();
    expect(state.state).not.toBe("interrupted");
  });

  // The APKPure regression (issue #9 follow-up). Nothing about this URL names the file: the path's
  // last segment is a package name, the content type is generic, and Chromium reports no suggested
  // filename at `downloads.onCreated`. The only source is the response's own Content-Disposition,
  // which the extension records as the response goes past.
  test("a download named only by its response header is intercepted", async ({ context }) => {
    app = await startStubApp("accept");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);

    // Only .xapk is allowed, so interception can only happen if the header was actually read.
    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["xapk"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html");
    const url = "http://127.0.0.1:8991/b/XAPK/com.example.app?version=latest&slow=1";

    // Fetch it from the page first, so the response passes through `webRequest` and its header is
    // recorded. In real use that happens by itself: a click navigates, the server answers with the
    // Content-Disposition, and only THEN does the browser turn it into a download. This test has to
    // arrange it explicitly because `chrome.downloads.download()` — the only way to start a download
    // Playwright does not intercept (see startBrowserDownload) — is not observed by `webRequest` at
    // all, so on its own it would test the fallback, not the header path.
    await page.evaluate(u => fetch(u).then(r => r.body?.cancel()), url);
    await expect.poll(async () => {
      const [sw] = context.serviceWorkers();
      return sw.evaluate(u => !!recallResponseHeaders(u), url);
    }, { timeout: 10000 }).toBe(true);

    await startBrowserDownload(context, url);

    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);
    expect(app.adds[0].body.url).toContain("com.example.app");

    const state = await downloadState(context, "com.example.app");
    expect(state).not.toBeNull();
    expect(state.state).toBe("interrupted");
    expect(state.error).toBe("USER_CANCELED");
  });

  // The data-loss regression (issue #9, Softpedia "Secure Download"). The app ACCEPTS the add — a 201
  // means the item was queued, not that the link is fetchable — and then never gets anywhere. The
  // browser's own download must survive that, or the user is left with no file at all.
  test("an add the app accepts but never fetches leaves the browser's download alone", async ({ context }) => {
    app = await startStubApp("stalled");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);

    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html");
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1&slow=1");

    // The hand-off is attempted...
    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);

    // ...but with no confirmation that the app is really fetching, the browser keeps the file.
    const state = await downloadState(context, "sample.zip");
    expect(state).not.toBeNull();
    expect(state.state).not.toBe("interrupted");
  });

  test("an add the app reports as failed leaves the browser's download alone, and says so", async ({ context }) => {
    app = await startStubApp("failing");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);

    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });
    const notifications = await recordNotifications(context);

    const page = await context.newPage();
    await page.goto("/empty.html");
    await startBrowserDownload(context, "http://127.0.0.1:8991/sample.zip?attach=1&slow=1");

    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);
    const state = await downloadState(context, "sample.zip");
    expect(state).not.toBeNull();
    expect(state.state).not.toBe("interrupted");

    // Keeping the file is not enough on its own: a download the app visibly refused, with no word
    // about it, reads as the extension having done nothing at all.
    const seen = await notifications();
    expect(seen.length).toBeGreaterThan(0);
    expect(`${seen[0].title} ${seen[0].message}`.toLowerCase()).toContain("browser is still downloading");
  });

  // The regression the whole follow-up is about. Every other test here downloads `/sample.zip`, so
  // the extension was always visible in the URL path — which is exactly why this shipped broken.
  // Here the path has no extension at all and only Content-Disposition names the file, the shape
  // GitHub releases / APKPure / Softpedia serve.
  // v2.8.0 led the hand-off with the link the user CLICKED and kept the end of the redirect chain as a
  // "fallback" that nothing ever tried, which broke every site serving the file from a different address
  // than the page (Softpedia's mirrors, APKMirror — issue #9). The chain's end leads again, and the
  // clicked link travels with it so the app can fall back to it.
  test("a redirected download hands over the chain's end first, with the clicked link as fallback",
    async ({ context }) => {
      app = await startStubApp("accept");
      test.skip(!app, "no free port in the app range for the stub");
      await setCachedPort(context, app.port);
      await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });

      const page = await context.newPage();
      await page.goto("/empty.html");
      const clicked = "http://127.0.0.1:8991/mirror-handler?p=999";
      await startBrowserDownload(context, clicked);

      await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);
      const body = app.adds[0].body;

      // The address the browser actually fetched the bytes from leads...
      expect(body.url).toContain("/sample.zip");
      // ...and the clicked link is handed over too, so the app can try it when the first one fails.
      expect(body.mirrors).toEqual([clicked]);
    });

  test("a signed link with no extension in its path is still intercepted by type", async ({ context }) => {
    app = await startStubApp("accept");
    test.skip(!app, "no free port in the app range for the stub");
    await setCachedPort(context, app.port);

    await setSettings(context, { enabled: true, fileTypes: { mode: "allow", list: ["zip"] }, minSizeBytes: 0 });

    const page = await context.newPage();
    await page.goto("/empty.html");
    // The name is in the QUERY, as GitHub's `rscd` / `response-content-disposition` puts it — that is
    // what the decision can actually see. A name carried only in the response *header* is invisible
    // at `onCreated` in Chromium and is the documented residual gap.
    await startBrowserDownload(
      context,
      "http://127.0.0.1:8991/signed-blob/9f3c1a?sig=abc"
        + "&slow=1&rscd=attachment%3B+filename%3Dsigned-sample.zip");

    await expect.poll(() => app.adds.length, { timeout: 15000 }).toBeGreaterThan(0);
    expect(app.adds[0].body.url).toContain("/signed-blob/");

    const state = await downloadState(context, "signed-blob");
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
