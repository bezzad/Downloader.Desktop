const { test, expect, openPopupFor } = require("../fixtures");

test("a known-unsupported host always shows the explanatory message, even with incidental detections", async ({ context, extensionId }) => {
  // Mocked at the network layer — no real request ever leaves the machine. Models the exact
  // real-world bug: YouTube's own UI sound effects were sniffed and shown as if downloadable
  // because the old logic only checked "zero items found".
  await context.route("https://www.youtube.com/**", async route => {
    const url = route.request().url();
    if (url.endsWith(".mp3")) {
      await route.fulfill({ contentType: "audio/mpeg", body: Buffer.alloc(2000, 1) });
    } else {
      await route.fulfill({
        contentType: "text/html",
        body: "<html><body><script>fetch('/no_input.mp3');</script></body></html>"
      });
    }
  });

  const page = await context.newPage();
  await page.goto("https://www.youtube.com/watch?v=e2e-test");
  await page.waitForTimeout(1000);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(1500);

  await expect(popup.locator("#empty")).toBeVisible();
  await expect(popup.locator("#empty")).toHaveClass(/unsupported/);
  await expect(popup.locator("#list li")).toHaveCount(0);
});

test("a non-blocked hostname with genuinely zero detections shows the generic empty state", async ({ context, extensionId }) => {
  const page = await context.newPage();
  await page.goto("/empty.html");
  await page.waitForTimeout(500);

  const popup = await openPopupFor(context, extensionId, page);
  await popup.waitForTimeout(1500);

  await expect(popup.locator("#empty")).toBeVisible();
  await expect(popup.locator("#empty")).not.toHaveClass(/unsupported/);
  await expect(popup.locator("#empty")).toHaveText("No media detected on this page yet.");
});
