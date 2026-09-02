// Popup UI: shows media detected on the active tab as ONE list, best copy first (HLS master, then
// quality, then size — see common.js's sortDetectedGroups for why relevance-ranking was removed),
// each row with a preview image, a size/quality upgrade pass, and a Download button.
const listEl = document.getElementById("list");
const emptyEl = document.getElementById("empty");
const statusEl = document.getElementById("status");
const versionEl = document.getElementById("version");
const appMissingEl = document.getElementById("appMissing");

let rawItems = []; // { url, type, group, capturedAt }
const probedByUrl = new Map(); // url -> probeMedia result ({ kind, size } or { kind: "hls", variants })
let thumbIndex = { byUrl: new Map(), fallback: null };
let currentTabId = null;
let isUnsupportedHost = false;
let siteState = { mode: "normal", message: null }; // set once the app has been asked about this page
let currentPageUrl = "";
let currentPageTitle = "";
let currentGroups = [];
let pageVariants = []; // the app's qualities for THIS page, once it has answered
const selectsByGroup = new Map(); // group.key -> <select> element (or null when ungrouped)

async function activeTab() {
  // Test-only override: e2e tests open popup.html as a normal tab (there's no public API to
  // trigger a real toolbar-popup click), which makes THAT tab "active" instead of the page under
  // test. A real toolbar popup URL never carries these params, so normal usage is unaffected.
  const params = new URLSearchParams(location.search);
  const forcedId = params.get("__testTabId");
  if (forcedId) return { id: parseInt(forcedId, 10), url: params.get("__testTabUrl") || "" };
  const [tab] = await api.tabs.query({ active: true, currentWindow: true });
  return tab;
}

function send(type, payload) {
  return new Promise(resolve => api.runtime.sendMessage({ type, ...payload }, resolve));
}

function fileName(url) {
  try {
    const p = new URL(url).pathname;
    return decodeURIComponent(p.split("/").pop()) || url;
  } catch { return url; }
}

// What identifies one option inside its card. The URL alone doesn't: a page's qualities are all the
// SAME url (the page) distinguished only by the variant the app should extract, so keying the
// <select> by URL would collapse them onto whichever came first.
function optionKey(opt) {
  if (!opt) return "";
  return opt.variantId ? `${opt.url}#${opt.variantId}` : opt.url;
}

// "720p" stays as-is; short word tokens read better upper-cased in a compact dropdown ("HD").
function qualityLabel(url) {
  const token = extractQualityToken(url);
  if (!token) return fileName(url);
  return /^\d/.test(token) ? token : token.toUpperCase();
}

// Collapses rawItems into one card per group (see common.js's groupKey / design.md Decision 4),
// then layers in whatever probeMedia has resolved so far (sizes, and for HLS masters, the real
// quality variants replacing the single placeholder option).
// "hls" is a manifest the app expands into its own qualities; everything else is one directly
// downloadable file. (DASH is not surfaced at all — see common.js's MEDIA_EXTENSIONS note.)
function kindOf(url) {
  return extOf(url) === "m3u8" ? "hls" : "direct";
}

function buildGroups() {
  const map = new Map();
  for (const item of rawItems) {
    const key = item.group || groupKey(item.url);
    let g = map.get(key);
    if (!g) {
      g = { key, kind: kindOf(item.url), options: [] };
      map.set(key, g);
    }
    if (!g.options.some(o => o.url === item.url))
      g.options.push({ url: item.url, label: null, size: null, approx: false });
  }

  for (const g of map.values()) {
    const probed = probedByUrl.get(g.key);
    if (g.kind === "hls" && probed?.kind === "hls" && probed.variants.length) {
      // The row still IDENTIFIES itself by the rendition URL (that is what was probed, deduped and
      // thumbnailed), but what gets SENT is the MASTER plus the chosen quality's id. A rendition of a
      // master that keeps its audio in a separate #EXT-X-MEDIA group is video-only, so handing the app
      // the rendition URL downloaded a video with no sound (reported on x.com). The app re-reads the
      // master, picks this quality and attaches its audio track. `variantId` is the app-side id scheme
      // (BANDWIDTH); when a variant declares none, the id is omitted and the app picks its own best —
      // audio always beats an exact quality match.
      g.options = probed.variants.map(v => ({
        url: v.uri,
        sendUrl: g.key,
        variantId: v.bandwidth ? String(v.bandwidth) : null,
        label: v.resolution || (v.bandwidth ? `${Math.round(v.bandwidth / 1000)} kbps` : "Variant"),
        size: v.size,
        approx: true
      }));
    } else if (probed?.kind === "direct") {
      const opt = g.options.find(o => o.url === probed.url);
      if (opt) { opt.size = probed.size; opt.approx = false; }
    }
    if (g.kind === "direct" && g.options.length > 1)
      for (const opt of g.options) opt.label = opt.label || qualityLabel(opt.url);
    g.title = fileName(g.kind === "direct" ? g.options[0]?.url ?? g.key : g.key);

    // Drop options a probe confirmed are implausibly tiny for real media (tracking beacons,
    // empty init segments — e.g. sub-1KB responses seen on X.com) — never before a probe has run.
    g.options = g.options.filter(opt => isPlausibleMediaSize(opt.size));
  }
  for (const [key, g] of map) if (g.options.length === 0) map.delete(key);

  // A variant playlist AND its individual segments are ALSO independently sniffed as their own
  // network responses (the browser fetches each one just like any other resource) — without this,
  // they'd show up as redundant top-level cards instead of being represented by the master's
  // quality picker. Real-world regression: on x.com this produced several near-duplicate "Main
  // media" cards (the master, each variant playlist, and/or raw .ts segments) for one video.
  const childUris = new Set();
  for (const g of map.values()) {
    const probed = probedByUrl.get(g.key);
    if (g.kind === "hls" && probed?.kind === "hls")
      for (const v of probed.variants) {
        childUris.add(v.uri);
        for (const seg of v.segmentUrls || []) childUris.add(seg);
      }
  }
  for (const key of childUris) map.delete(key);

  return sortDetectedGroups([...map.values()]);
}

// A fixed-size preview slot, so the list never reflows as previews arrive and a source that fails to
// load falls back to the type placeholder instead of leaving a broken image. `src` is this group's
// OWN assigned image (see assignThumbnails) — never looked up freshly here, or every card would draw
// from the same shared fallback and repeat one photo across unrelated items (the x.com regression).
function buildThumb(group, src) {
  const slot = document.createElement("div");
  slot.className = "thumb";
  const ext = (extOf(groupTypeUrl(group)) || "").toUpperCase();
  // A page row has no file extension to show — it stands for the video the app will extract.
  const label = group.kind === "page" ? "PAGE" : (ext ? ext.slice(0, 4) : "FILE");
  const placeholder = () => {
    slot.textContent = label;
    slot.classList.add("placeholder");
  };
  if (!src) { placeholder(); return slot; }
  const img = document.createElement("img");
  img.alt = "";
  img.decoding = "async";
  img.onerror = () => { slot.innerHTML = ""; placeholder(); };
  img.src = src;
  slot.appendChild(img);
  return slot;
}

function buildCard(group, thumbSrc) {
  const li = document.createElement("li");
  const meta = document.createElement("div");
  meta.className = "meta";
  const name = document.createElement("div");
  name.className = "name";
  name.textContent = group.title;
  name.title = group.title;
  meta.appendChild(name);

  let select = null;
  if (group.options.length > 1) {
    select = document.createElement("select");
    select.className = "quality";
    for (const opt of group.options) {
      const o = document.createElement("option");
      o.value = optionKey(opt);
      o.textContent = opt.label || fileName(opt.url);
      select.appendChild(o);
    }
    meta.appendChild(select);
  }
  selectsByGroup.set(group.key, select);

  const sizeEl = document.createElement("div");
  sizeEl.className = "type size-line";
  meta.appendChild(sizeEl);

  const currentOption = () => {
    const key = select ? select.value : optionKey(group.options[0]);
    return group.options.find(o => optionKey(o) === key) || group.options[0];
  };
  const updateSize = () => {
    const opt = currentOption();
    const human = opt && formatBytes(opt.size);
    const size = human ? (opt.approx ? "~" : "") + human : "";
    // Say the quality the row was ranked on — otherwise the order of the list is unexplainable from
    // looking at it. Only when there is no picker: a picker already shows every quality.
    const height = select ? -1 : groupQualityHeight(group);
    const quality = height > 0 ? `${height}p` : "";
    sizeEl.textContent = [quality, size].filter(Boolean).join(" · ") || group.note || "";
  };
  if (select) select.onchange = updateSize;
  updateSize();

  const btn = document.createElement("button");
  btn.className = "primary";
  btn.textContent = "Download";
  btn.onclick = () => sendOption(currentOption(), btn);

  li.append(buildThumb(group, thumbSrc), meta, btn);
  return li;
}

// The active page as a one-option group, so it renders through the SAME card builder as everything
// else. Its URL is the page's: the app re-reads the page with the plugin that claimed it and picks the
// stream itself, which is the only way to get the video off a site whose player hides the file.
// Its options are the qualities the APP reported for the page (1080p, 720p, audio-only …) — the same
// picker the Add window shows, on the row itself, because most of the time what is wanted is not the
// quality that happened to be playing: it's the audio, or a smaller copy. Until the app answers (or
// when it offers no real choice) the row keeps its single implicit option and downloads the app's own
// best pick, exactly as before.
function pageGroup() {
  const options = pageVariants.length
    ? pageVariants.map(v => ({
        url: v.url || currentPageUrl,
        sendUrl: v.url || currentPageUrl,
        variantId: v.url ? null : v.id, // a variant that IS its own link substitutes the URL instead
        label: v.label || v.id,
        size: typeof v.size === "number" ? v.size : null,
        approx: true
      }))
    : [{ url: currentPageUrl, label: null, size: null, approx: false }];
  return {
    key: currentPageUrl,
    kind: "page",
    title: currentPageTitle || fileName(currentPageUrl) || currentPageUrl,
    note: siteState.handler ? `Video page · ${siteState.handler}` : "Video page",
    options,
  };
}

function render() {
  // A page the app itself can download is an ITEM, not a notice: one ordinary row, same thumbnail,
  // same Download button as any sniffed file. It replaced a block of red explanatory text standing
  // where the video belonged, which read as an error for a page that downloads perfectly well.
  if (siteState.mode === "offer" && currentPageUrl) {
    currentGroups = [pageGroup()];
    selectsByGroup.clear();
    listEl.innerHTML = "";
    listEl.append(buildCard(currentGroups[0], thumbIndex.fallback));
    emptyEl.style.display = "none";
    emptyEl.classList.remove("unsupported");
    return;
  }

  // A site whose video this install genuinely cannot get ALWAYS says so and suppresses the list —
  // even when something was incidentally sniffed (e.g. YouTube's own UI sound-effect mp3s), since
  // none of it is ever the protected video content the user wants. Real-world fix: v1.2.0 only
  // checked this when zero items existed, so those unrelated sounds were shown as if they were
  // downloadable.
  if (siteState.mode !== "normal") {
    currentGroups = [];
    selectsByGroup.clear();
    listEl.innerHTML = "";
    emptyEl.style.display = "block";
    emptyEl.classList.add("unsupported");
    emptyEl.textContent = siteState.message;
    return;
  }

  currentGroups = buildGroups();
  selectsByGroup.clear();
  listEl.innerHTML = "";
  // Computed ONCE per render, in list order, so each group gets its own image off the shared leftover
  // queue instead of every card independently picking (and repeating) the same one.
  const thumbs = assignThumbnails(thumbIndex, currentGroups);
  for (const g of currentGroups) listEl.append(buildCard(g, thumbs.get(g.key)));

  if (currentGroups.length === 0) {
    emptyEl.style.display = "block";
    emptyEl.classList.remove("unsupported");
    emptyEl.textContent = "No media detected on this page yet.";
  } else {
    emptyEl.style.display = "none";
    emptyEl.classList.remove("unsupported");
  }
}

function addItem(url, type) {
  if (!isHttp(url) || rawItems.some(i => i.url === url)) return;
  rawItems.push({ url, type: type || extOf(url), group: groupKey(url), capturedAt: Date.now() });
}

async function sendOne(url, btn, variantId) {
  if (!url) return;
  if (btn) { btn.disabled = true; btn.textContent = "…"; }
  const { ok } = await send("send", { url, variantId: variantId || null });
  if (btn) { btn.textContent = ok ? "Sent ✓" : "Failed"; }
}

// Sends one chosen option: its `sendUrl` when the option stands for a rendition of a manifest the app
// should expand itself (see buildGroups), else its own URL.
function sendOption(opt, btn) {
  if (!opt) return Promise.resolve();
  return sendOne(opt.sendUrl || opt.url, btn, opt.variantId);
}

async function refreshStatus() {
  const { ok } = await send("ping", {});
  statusEl.className = "status " + (ok ? "on" : "off");
  statusEl.title = ok ? "Desktop app connected" : "Desktop app not reachable — start it and enable browser integration";
  // The dot alone only says something is wrong once you hover it. Say what was actually tried, so a
  // report can be answered with a screenshot instead of a guess.
  if (appMissingEl) {
    appMissingEl.textContent = ok ? "" : appNotFoundMessage();
    appMissingEl.style.display = ok ? "none" : "block";
  }
}


// Renders immediately with whatever's known, then upgrades in place once probes resolve — a slow
// or blocked probe never delays first paint (design.md Decision 6).
async function probeAndRender() {
  if (currentTabId == null) return;
  const { results } = await send("probeMedia", { tabId: currentTabId });
  for (const r of results || []) if (r) probedByUrl.set(r.url, r);
  render();
}

// Asks the PAGE what it can see, using the same injection path as "Scan page links" (which is why no
// content script is needed on every page just to serve a UI that is only looked at while the popup is
// open). For each player element it reports what identifies it (src), what the site itself offers
// (poster), and a frame drawn onto a small canvas — which is the real thing but often unavailable,
// since a cross-origin video taints the canvas. Plus the page's own social image as a last resort.
//
// Everything here stays inside the extension: the data URL is the return value of this call, is used
// to set an <img> in this popup, and is never sent anywhere — least of all to the app (a hand-off
// carries the link and its request context only).
async function collectThumbnails() {
  if (currentTabId == null) return;
  try {
    const results = await api.scripting.executeScript({
      target: { tabId: currentTabId },
      func: () => {
        const MAX_W = 160; // a thumbnail, not a frame: keeps the data URL a few KB
        const shots = [];
        for (const el of document.querySelectorAll("video, audio")) {
          const rect = el.getBoundingClientRect();
          let frame = null;
          try {
            if (el.tagName === "VIDEO" && el.videoWidth > 0 && el.videoHeight > 0) {
              const scale = Math.min(1, MAX_W / el.videoWidth);
              const canvas = document.createElement("canvas");
              canvas.width = Math.max(1, Math.round(el.videoWidth * scale));
              canvas.height = Math.max(1, Math.round(el.videoHeight * scale));
              canvas.getContext("2d").drawImage(el, 0, 0, canvas.width, canvas.height);
              frame = canvas.toDataURL("image/jpeg", 0.6); // throws (SecurityError) if tainted
            }
          } catch {
            frame = null; // cross-origin media — the poster/page image below is the answer
          }
          shots.push({
            src: el.currentSrc || el.src || "",
            poster: el.getAttribute("poster") ? el.poster : "",
            frame,
            area: Math.max(0, rect.width * rect.height)
          });
        }
        const meta = sel => document.querySelector(sel)?.content || "";
        const pageImage = meta('meta[property="og:image"]') || meta('meta[name="twitter:image"]');
        return { shots, pageImage };
      }
    });
    const data = results?.[0]?.result;
    if (data) thumbIndex = buildThumbnailIndex(data.shots, data.pageImage);
  } catch {
    // Browser-internal pages and pages that forbid injection: rows keep their placeholders, and
    // every Download action still works.
  }
}

async function loadDetected() {
  const tab = await activeTab();
  currentTabId = tab.id;
  currentPageUrl = tab.url || "";
  currentPageTitle = tab.title || "";
  try { isUnsupportedHost = isKnownUnsupportedHost(new URL(tab.url).hostname); } catch { isUnsupportedHost = false; }
  // Whether such a page is a dead end depends on the app, not on this list: with the site-media plugin
  // installed the page itself is downloadable. Ask before deciding what to say (issue #9 follow-up).
  siteState = unsupportedSiteState({ hostUnsupported: isUnsupportedHost, appHandlesPage: false, handlerName: null });
  if (isUnsupportedHost) {
    const { handled, by } = await send("canHandlePage", { url: tab.url });
    siteState = unsupportedSiteState({ hostUnsupported: true, appHandlesPage: handled, handlerName: by });
    // Upgrades the page row in place when it answers; the lookup runs the site tool and can take a few
    // seconds, so it must never hold up the first paint below.
    if (handled) loadPageVariants(tab.url);
  }
  const { media } = await send("getMedia", { tabId: tab.id });
  for (const m of media || []) if (!rawItems.some(i => i.url === m.url)) rawItems.push(m);
  render();
  // Both upgrade the list in place; neither delays this first paint.
  probeAndRender();
  collectThumbnails().then(render);
}

async function loadPageVariants(url) {
  const { variants } = await send("pageVariants", { url });
  if (!variants || !variants.length) return; // no choice to offer — the row stays as it is
  pageVariants = variants;
  render();
}

async function scanPageLinks() {
  const tab = await activeTab();
  try {
    const results = await api.scripting.executeScript({
      target: { tabId: tab.id },
      func: () => {
        const urls = new Set();
        document.querySelectorAll("a[href]").forEach(a => urls.add(a.href));
        document.querySelectorAll("video[src],audio[src],source[src]").forEach(s => urls.add(s.src));
        return [...urls];
      }
    });
    const urls = results?.[0]?.result || [];
    for (const u of urls) if (looksLikeMedia(u)) addItem(u);
    render();
    probeAndRender();
  } catch {
    // Some pages (e.g. browser internal pages) don't allow injection.
  }
}

document.getElementById("sendManual").onclick = () => {
  const input = document.getElementById("manualUrl");
  const url = input.value.trim();
  if (url) { sendOne(url); input.value = ""; }
};
document.getElementById("scanLinks").onclick = scanPageLinks;
document.getElementById("sendAll").onclick = async () => {
  for (const g of currentGroups) {
    const select = selectsByGroup.get(g.key);
    const key = select ? select.value : optionKey(g.options[0]);
    await sendOption(g.options.find(o => optionKey(o) === key) || g.options[0]);
  }
};

// Silent-vs-dialog choice (persisted; the background worker reads it on every send).
const silentEl = document.getElementById("silentMode");
getAddMode().then(mode => { silentEl.checked = mode === "silent"; });
silentEl.onchange = () => setAddMode(silentEl.checked ? "silent" : "dialog");

// Interception rules and the rest of the settings live on the options page; the popup only links to
// it (the extension had no settings surface at all before — see issue #9).
document.getElementById("openOptions").onclick = () => {
  if (api.runtime?.openOptionsPage) api.runtime.openOptionsPage();
  else api.tabs?.create({ url: api.runtime.getURL("options.html") });
};

// Shows the installed extension's own version next to its name, so a report ("I'm on the latest
// version but...") can be answered from a screenshot instead of asking the user to dig through
// about:addons. `getManifest()` is synchronous and always available — no permission, no network.
if (versionEl) {
  const full = api.runtime.getManifest().version;
  versionEl.textContent = `v${shortVersion(full)}`;
  versionEl.title = `Downloader extension ${full}`;
}

refreshStatus();
loadDetected();
