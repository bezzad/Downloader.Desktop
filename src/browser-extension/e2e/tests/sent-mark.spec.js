const { test, expect, openPopupFor } = require("../fixtures");

// A row sent to the app keeps its check mark: it is saved in extension storage, so a rebuilt list
// (a feed rebuilds it on every scroll) or a new popup still shows it as sent.

test("a sent row still shows its check mark after the list is built again", async ({ context, extensionId, baseURL }) => {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(async url => { await chrome.storage.local.set({ sentKeys: [url] }); }, `${baseURL}/main_video.mp4`);

  const page = await context.newPage();
  await page.goto("/video-paused.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  const button = popup.locator("#list li .row-action");
  await expect(button).toHaveClass(/done/, { timeout: 10000 });

  await popup.click("#refreshList");
  await expect(popup.locator("#list li")).toHaveCount(1, { timeout: 10000 });
  await expect(button).toHaveClass(/done/);
});

test("a row never sent has no check mark", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/video-paused.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await expect(popup.locator("#list li")).toHaveCount(1, { timeout: 10000 });
  await expect(popup.locator("#list li .row-action")).not.toHaveClass(/done/);
});
