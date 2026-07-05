// @ts-check
const { PORT } = require("./server");

/** @type {import('@playwright/test').PlaywrightTestConfig} */
module.exports = {
  testDir: "./tests",
  timeout: 30000,
  fullyParallel: false, // one shared extension context per file keeps tabId lookups unambiguous
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
