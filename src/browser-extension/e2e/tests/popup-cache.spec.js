const { test, expect, openPopupFor } = require("../fixtures");

// The popup keeps what it measured on a page, so opening it again does not fetch every size again.
// /videos/grow.mp4 answers with a bigger size on every request, so a re-measured size would differ.

async function shownSize(context, extensionId, page) {
  const popup = await openPopupFor(context, extensionId, page);
  const size = popup.locator("#list li .size-val").first();
  await expect(size).toHaveText(/KB/, { timeout: 10000 });
  const text = await size.textContent();
  await popup.close();
  return text;
}

test("opening the popup again reuses the measured size", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/grow.html");
  await page.waitForTimeout(1500);

  const first = await shownSize(context, extensionId, page);
  const second = await shownSize(context, extensionId, page);
  expect(second).toBe(first);
});

test("reloading the page measures the size again", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/grow.html");
  await page.waitForTimeout(1500);
  const first = await shownSize(context, extensionId, page);

  await page.reload();
  await page.waitForTimeout(1500);
  const afterReload = await shownSize(context, extensionId, page);
  expect(afterReload).not.toBe(first);
});

test("a feed keeps the picture of a video that scrolled away and adds the new one", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/feed.html");
  await page.waitForTimeout(1500);

  let popup = await openPopupFor(context, extensionId, page);
  await expect(popup.locator("#list li .thumb img")).toHaveCount(1, { timeout: 10000 });
  await popup.close();

  await page.evaluate(() => scrollMore()); // A leaves the page, B arrives
  await page.waitForTimeout(1500);

  popup = await openPopupFor(context, extensionId, page);
  await expect(popup.locator("#list li")).toHaveCount(2, { timeout: 10000 });
  await expect(popup.locator("#list li .thumb img")).toHaveCount(2, { timeout: 10000 });
  const srcs = await popup.locator("#list li .thumb img").evaluateAll(els => els.map(e => e.src));
  expect(srcs.some(s => s.includes("thumbA"))).toBe(true);
  expect(srcs.some(s => s.includes("thumbB"))).toBe(true);
});
