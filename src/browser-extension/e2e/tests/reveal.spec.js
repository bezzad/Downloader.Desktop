const { test, expect, openPopupFor } = require("../fixtures");

// Where a player's middle is, as a share of the window's height (0.5 = the middle of the page).
const middleOf = (page, id) => page.evaluate(id => {
  const r = document.getElementById(id).getBoundingClientRect();
  return (r.top + r.height / 2) / window.innerHeight;
}, id);

test("clicking a row scrolls the page so that row's player is in the middle", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/reveal.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await expect(popup.locator("#list li")).toHaveCount(2, { timeout: 10000 });

  await popup.locator("#list li", { hasText: "alpha_360p" }).locator(".name").click();
  await expect.poll(() => middleOf(page, "b"), { timeout: 5000 }).toBeCloseTo(0.5, 1);

  await popup.locator("#list li", { hasText: "main_video" }).locator(".name").click();
  await expect.poll(() => middleOf(page, "a"), { timeout: 5000 }).toBeCloseTo(0.5, 1);
});

test("the Download button does not scroll the page", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/reveal.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  await expect(popup.locator("#list li")).toHaveCount(2, { timeout: 10000 });

  await popup.locator("#list li", { hasText: "alpha_360p" }).locator(".row-action").click();
  await page.waitForTimeout(1000);
  expect(await page.evaluate(() => window.scrollY)).toBe(0);
});

test("a grouped item, a player in a shadow root and a player in a frame are all found", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/reveal-grouped.html");
  await page.waitForTimeout(1500);

  const popup = await openPopupFor(context, extensionId, page);
  const grouped = popup.locator("#list li", { hasText: "master" });
  await expect(grouped.locator(".chip")).toHaveCount(2, { timeout: 10000 });

  // The empty part of the quality band, not a chip: most of a grouped item.
  const band = await grouped.locator(".qband").boundingBox();
  await popup.mouse.click(band.x + band.width - 6, band.y + band.height - 3);
  await expect.poll(() => middleOf(page, "b"), { timeout: 5000 }).toBeCloseTo(0.5, 1);

  await popup.locator("#list li", { hasText: "main_video" }).locator(".name").click();
  await expect.poll(() => middleOf(page, "a"), { timeout: 5000 }).toBeCloseTo(0.5, 1);
});
