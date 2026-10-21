const { test, expect, openPopupFor } = require("../fixtures");

// "Hide files smaller than" (options page). The fixtures turn it off; these tests put it back.

test("by default a file under 1 MB is not listed", async ({ context, extensionId }) => {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(() => chrome.storage.local.remove("listMinSizeBytes"));

  const page = await context.newPage();
  await page.goto("/video-playing.html"); // a ~20 KB video
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);
  await expect(popup.locator("#list li")).toHaveCount(0);
});

test("the options slider starts at 1 MB, reaches 10 GB, and saves its value", async ({ context, extensionId }) => {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(() => chrome.storage.local.remove("listMinSizeBytes"));

  const options = await context.newPage();
  await options.goto(`chrome-extension://${extensionId}/options.html`);
  const slider = options.locator("#listMinSize");
  await expect(options.locator("#listMinSizeValue")).toHaveText("1.0 MB");

  await slider.focus();
  await options.keyboard.press("End");
  await expect(options.locator("#listMinSizeValue")).toHaveText("10 GB");
  await expect.poll(() => sw.evaluate(async () =>
    (await chrome.storage.local.get("listMinSizeBytes")).listMinSizeBytes)).toBe(10 * 1024 ** 3);

  await options.keyboard.press("Home");
  await expect(options.locator("#listMinSizeValue")).toHaveText("0 (show all)");
  await expect.poll(() => sw.evaluate(async () =>
    (await chrome.storage.local.get("listMinSizeBytes")).listMinSizeBytes)).toBe(0);
});
