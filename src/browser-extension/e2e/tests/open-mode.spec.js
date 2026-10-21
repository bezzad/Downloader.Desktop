const { test, expect, openPopupFor } = require("../fixtures");

// "Open as" (options page): the small popup, or the browser's side panel that stays open.
// Playwright cannot click the toolbar button, so these check what the button is set to do.

async function buttonState(sw) {
  return sw.evaluate(async () => ({
    popup: await chrome.action.getPopup({}),
    panelOnClick: (await chrome.sidePanel.getPanelBehavior()).openPanelOnActionClick
  }));
}

test("the toolbar button opens the popup by default", async ({ context }) => {
  const [sw] = context.serviceWorkers();
  await expect.poll(() => buttonState(sw)).toEqual({
    popup: expect.stringMatching(/popup\.html$/), panelOnClick: false
  });
});

test("choosing the side panel in the options makes the button open and close the side panel", async ({ context, extensionId }) => {
  const [sw] = context.serviceWorkers();
  const options = await context.newPage();
  await options.goto(`chrome-extension://${extensionId}/options.html`);
  await expect(options.locator("#openSection")).toBeVisible();
  await expect(options.locator("#openMode")).toHaveValue("popup");

  await options.selectOption("#openMode", "panel");
  await expect.poll(() => buttonState(sw)).toEqual({ popup: "", panelOnClick: true });

  // The page as a side panel fills the panel instead of keeping the popup's fixed width.
  const page = await context.newPage();
  await page.goto("/video-playing.html");
  const panel = await openPopupFor(context, extensionId, page);
  await expect(panel.locator("body")).toHaveClass(/\bpanel\b/);

  await options.selectOption("#openMode", "popup");
  await expect.poll(() => buttonState(sw)).toEqual({
    popup: expect.stringMatching(/popup\.html$/), panelOnClick: false
  });
});

test("the side panel's list updates when the page is scrolled", async ({ context, extensionId }) => {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(() => chrome.storage.local.set({ openMode: "panel" }));

  const page = await context.newPage();
  await page.goto("/empty.html");
  const panel = await openPopupFor(context, extensionId, page);
  await expect(panel.locator("body")).toHaveClass(/\bpanel\b/);
  await expect(panel.locator("#list li")).toHaveCount(0);

  // A feed adds a video further down; nothing fetches it, so only the scroll can bring it in.
  await page.evaluate(() => {
    document.body.insertAdjacentHTML("beforeend",
      '<div style="height:3000px"></div><a href="/videos/movie_720p.mp4">movie</a>');
    window.scrollTo(0, 2500);
  });
  await expect(panel.locator("#list li")).toHaveCount(1, { timeout: 10000 });
});
