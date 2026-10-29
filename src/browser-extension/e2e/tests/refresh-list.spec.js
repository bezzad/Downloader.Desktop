const { test, expect, openPopupFor } = require("../fixtures");

// The header's refresh button clears the list and looks at the page again.

test("refresh drops a video that is no longer on the page", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/video-paused.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await expect(popup.locator("#list li")).toHaveCount(1, { timeout: 10000 });

  await page.evaluate(() => document.getElementById("v").remove()); // like a feed scrolling it away
  await popup.click("#refreshList");
  await expect(popup.locator("#list li")).toHaveCount(0, { timeout: 10000 });
  await expect(popup.locator("#empty")).toBeVisible();

  // The background forgot it too: opening the popup again does not bring it back.
  await popup.close();
  const again = await openPopupFor(context, extensionId, page);
  await again.waitForTimeout(1500);
  await expect(again.locator("#list li")).toHaveCount(0);
});

test("refresh finds again a video that is still on the page", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/video-paused.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await expect(popup.locator("#list li")).toHaveCount(1, { timeout: 10000 });

  await popup.click("#refreshList");
  await expect(popup.locator("#list li")).toHaveCount(1, { timeout: 10000 });
  await expect(popup.locator("#list li")).toContainText("main_video");
});
