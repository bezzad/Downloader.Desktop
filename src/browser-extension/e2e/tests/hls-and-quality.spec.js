const { test, expect, openPopupFor } = require("../fixtures");

test("HLS master expands into a quality picker with an estimated size, with no duplicate variant/segment cards", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/hls-playing.html");
  await page.waitForTimeout(1500); // let the fetches land

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000); // getMedia + probeMedia round trip

  const cards = popup.locator("#list li");
  // Exactly ONE card: the master. Its variant playlists (low/high index.m3u8) and segment
  // (low/seg0.ts) are all independently sniffed network responses too, but must be represented
  // by the master's quality picker, not show up as their own redundant cards (real-world
  // regression: this used to produce 3+ near-duplicate cards for one video).
  await expect(cards).toHaveCount(1);

  const select = cards.first().locator("select.quality");
  const optionTexts = await select.locator("option").allTextContents();
  expect(optionTexts.some(t => t.includes("320x240"))).toBeTruthy();
  expect(optionTexts.some(t => t.includes("640x480"))).toBeTruthy();

  await expect(cards.first().locator(".size-line")).toContainText("~"); // HLS = always an estimate
});

test("an implausibly tiny junk .m3u8 is filtered out entirely", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/hls-playing.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  await expect(popup.locator("li", { hasText: "junk.m3u8" })).toHaveCount(0);
});

test("direct-file quality variants are grouped into one card", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/direct-quality.html");
  await page.waitForTimeout(1000);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(3000);

  const cards = popup.locator("#list li");
  await expect(cards).toHaveCount(1); // both qualities grouped into ONE card
  await expect(cards.first().locator("select.quality option")).toHaveCount(2);
});
