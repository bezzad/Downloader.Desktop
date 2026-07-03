// Shared Playwright fixtures: loads the REAL unpacked extension (src/browser-extension) into a
// persistent Chromium context, and exposes the extension id (parsed from its service worker URL —
// the standard way to test an MV3 extension with Playwright, since there's no public API to
// trigger a real toolbar-icon click).
"use strict";
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const base = require("@playwright/test");

const EXTENSION_PATH = path.join(__dirname, ".."); // src/browser-extension

const test = base.test.extend({
  context: async ({}, use) => {
    const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), "downloader-ext-e2e-"));
    const context = await base.chromium.launchPersistentContext(userDataDir, {
      headless: false, // MV3 extensions need a real (or new-headless) browser session
      args: [
        `--disable-extensions-except=${EXTENSION_PATH}`,
        `--load-extension=${EXTENSION_PATH}`
      ]
    });

    // Test-harness-only warm-up: in a freshly launched profile, the extension's webRequest
    // listener is not reliably wired up until AFTER the very first navigation — confirmed by
    // direct reproduction, it silently misses every request on that first navigation regardless
    // of destination, then works reliably from the second navigation on. This never affects real
    // users (their extension has been running for a while before they visit any page); it's
    // purely an artifact of launching a brand-new browser + extension together for each test.
    const warm = await context.newPage();
    await warm.goto("about:blank");
    await warm.waitForTimeout(300);
    await warm.close();

    await use(context);
    await context.close();
    fs.rmSync(userDataDir, { recursive: true, force: true });
  },

  extensionId: async ({ context }, use) => {
    let [sw] = context.serviceWorkers();
    if (!sw) sw = await context.waitForEvent("serviceworker");
    await use(sw.url().split("/")[2]);
  }
});

// Waits for the given URL substring to become a real browser tab, then returns its chrome tab id
// (via the extension's own service worker — Playwright has no public API to read a tab id).
async function tabIdFor(context, urlIncludes) {
  const [sw] = context.serviceWorkers();
  for (let i = 0; i < 50; i++) {
    const id = await sw.evaluate(async needle => {
      const tabs = await chrome.tabs.query({});
      return tabs.find(t => t.url && t.url.includes(needle))?.id ?? null;
    }, urlIncludes);
    if (id != null) return id;
    await new Promise(r => setTimeout(r, 100));
  }
  throw new Error(`no tab found matching "${urlIncludes}"`);
}

// Opens the popup pointed at a specific tab (see popup.js's __testTabId override) instead of
// relying on chrome.tabs' real "active tab", which the popup-as-a-normal-tab testing technique
// itself would otherwise hijack.
async function openPopupFor(context, extensionId, targetPage) {
  const tabId = await tabIdFor(context, targetPage.url());
  const popup = await context.newPage();
  const qs = new URLSearchParams({ __testTabId: String(tabId), __testTabUrl: targetPage.url() });
  await popup.goto(`chrome-extension://${extensionId}/popup.html?${qs}`);
  return popup;
}

module.exports = { test, expect: base.expect, openPopupFor };
