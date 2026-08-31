// @ts-check
const { PORT } = require("./server");

/** @type {import('@playwright/test').PlaywrightTestConfig} */
module.exports = {
  testDir: "./tests",
  timeout: 30000,
  fullyParallel: false, // one shared extension context per file keeps tabId lookups unambiguous
  // One worker, because every spec's stand-in for the app has to listen on the SAME fixed port range
  // the extension is allowed to probe (MV3 host permissions must be static). Run two spec files at
  // once and they take each other's ports: an add lands on the other file's stub, or the "no app is
  // running" spec finds one and fails. Neither is a real defect, and both are invisible when a file
  // is run alone — so the suite is serialized rather than left to fail by arrangement.
  workers: 1,
  reporter: "list",
  webServer: {
    command: "node server.js",
    port: PORT,
    reuseExistingServer: !process.env.CI
  },
  use: {
    baseURL: `http://127.0.0.1:${PORT}`
  }
};
