const { test, expect, openPopupFor } = require("../fixtures");

// The fix: there is no "Main media" / "Other detected" split any more. The old promotion rule needed
// a visibility hint from a content script to be fresh at the exact moment the popup asked, which on a
// feed page whose player had finished autoplaying (x.com) it routinely was not — so the page's own
// video was demoted and the popup opened empty behind a collapsed section.

test("a video paused after autoplay is listed directly, with nothing to expand (the x.com fix)", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/video-paused.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  await expect(popup.locator("#list li")).toHaveCount(1);
  await expect(popup.locator("#empty")).toBeHidden();
  // The sections the user used to have to hunt through are gone from the document entirely.
  await expect(popup.locator("#mainList")).toHaveCount(0);
  await expect(popup.locator("#otherSection")).toHaveCount(0);
});

test("a playing video is listed the same way — playback state no longer changes anything", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/video-playing.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  await expect(popup.locator("#list li")).toHaveCount(1);
});

test("a same-origin video gets a real captured frame as its preview", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/video-playing.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  // Same-origin media leaves the canvas untainted, so the frame grab is the real thing here. On a
  // cross-origin player it fails and the poster/page image takes over (unit-tested).
  const src = await popup.locator("#list li .thumb img").first().getAttribute("src");
  expect(src).toMatch(/^data:image\/jpeg/);
});

test("a row with no available preview still shows a fixed-size type placeholder", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/direct-quality.html"); // fetches only — no player element to photograph
  await page.waitForTimeout(1000);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  const thumb = popup.locator("#list li .thumb").first();
  await expect(thumb).toHaveClass(/placeholder/);
  await expect(thumb).toHaveText("MP4");
  const box = await thumb.boundingBox();
  expect(box.width).toBeGreaterThan(50); // occupies the same slot a real preview would
});

test("an HLS master leads the list, above a direct mp4", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/mixed-media.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  const names = await popup.locator("#list li .name").allTextContents();
  const manifest = names.findIndex(n => n.includes("master.m3u8"));
  const mp4 = names.findIndex(n => n.includes("movie"));
  expect(manifest).toBeGreaterThanOrEqual(0);
  expect(mp4).toBeGreaterThanOrEqual(0);
  expect(manifest).toBeLessThan(mp4); // detected second, listed first
});

test("after HLS the higher quality leads, even when the lower-quality file is bigger", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/quality-order.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000); // needs the size probes to have landed too

  const names = await popup.locator("#list li .name").allTextContents();
  const hi = names.findIndex(n => n.includes("beta_1080p"));
  const lo = names.findIndex(n => n.includes("alpha_360p"));
  expect(hi).toBeGreaterThanOrEqual(0);
  expect(lo).toBeGreaterThanOrEqual(0);
  expect(hi).toBeLessThan(lo);

  // The row says what it was ranked on, so the order is explainable from looking at it.
  await expect(popup.locator("#list li").first().locator(".size-line")).toContainText("1080p");
});
