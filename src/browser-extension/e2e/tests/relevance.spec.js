const { test, expect, openPopupFor } = require("../fixtures");

test("a currently playing video is promoted to Main media", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/video-playing.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  await expect(popup.locator("#mainList li")).toHaveCount(1);
});

test("a video paused after autoplay is STILL promoted to Main media (the x.com regression)", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/video-paused.html");
  // Let it play, pause at ~300ms, and content.js's periodic (2s) re-check keep the hint fresh.
  await page.waitForTimeout(2500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  await expect(popup.locator("#mainList li")).toHaveCount(1);
});
