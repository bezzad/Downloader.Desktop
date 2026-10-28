const { test, expect, openPopupFor } = require("../fixtures");

// The button beside Settings in the popup: the small popup, or the browser's side panel that stays open.
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

test("the button beside Settings switches to the side panel and back", async ({ context, extensionId }) => {
  const [sw] = context.serviceWorkers();
  const page = await context.newPage();
  await page.goto("/video-playing.html");

  const popup = await openPopupFor(context, extensionId, page);
  const toggle = popup.locator("#togglePanel");
  await expect(toggle).toBeVisible();
  await expect(popup.locator("body")).not.toHaveClass(/\bpanel\b/);
  await toggle.click();
  await expect.poll(() => buttonState(sw)).toEqual({ popup: "", panelOnClick: true });

  // Now the page is the side panel: it fills the panel, and the same button goes back.
  const panel = await openPopupFor(context, extensionId, page);
  await expect(panel.locator("body")).toHaveClass(/\bpanel\b/);
  await expect(panel.locator("#togglePanel")).toHaveClass(/\bon\b/);
  await panel.locator("#togglePanel").click();
  await expect.poll(() => buttonState(sw)).toEqual({
    popup: expect.stringMatching(/popup\.html$/), panelOnClick: false
  });
});

test("the options page no longer carries the open mode", async ({ context, extensionId }) => {
  const options = await context.newPage();
  await options.goto(`chrome-extension://${extensionId}/options.html`);
  await expect(options.locator("#openMode")).toHaveCount(0);
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

test("in the side panel only the list scrolls; header and footer stay in place", async ({ context, extensionId }) => {
  const [sw] = context.serviceWorkers();
  await sw.evaluate(() => chrome.storage.local.set({ openMode: "panel" }));

  const page = await context.newPage();
  await page.goto("/empty.html");
  const panel = await openPopupFor(context, extensionId, page);
  await panel.setViewportSize({ width: 360, height: 500 });
  await expect(panel.locator("body")).toHaveClass(/\bpanel\b/);

  // Far more rows than fit, so the list must scroll.
  await panel.evaluate(() => {
    const list = document.getElementById("list");
    for (let i = 0; i < 60; i++) list.insertAdjacentHTML("beforeend", `<li><div class="row">file ${i}.mp4</div></li>`);
    list.scrollTop = list.scrollHeight;
  });

  const box = await panel.evaluate(() => ({
    pageScrolls: document.scrollingElement.scrollHeight > innerHeight,
    listScrolls: document.getElementById("list").scrollTop > 0,
    headerTop: document.querySelector("header").getBoundingClientRect().top,
    footerBottom: document.querySelector("footer").getBoundingClientRect().bottom,
    height: innerHeight
  }));
  expect(box).toEqual({ pageScrolls: false, listScrolls: true, headerTop: 0, footerBottom: box.height, height: 500 });
});
