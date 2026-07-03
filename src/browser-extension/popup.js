// Popup UI: shows media detected on the active tab (grouped by video, with a size/quality upgrade
// pass), lets the user scan page links, paste a URL, and send any/all of them to the desktop app.
const mainListEl = document.getElementById("mainList");
const otherListEl = document.getElementById("otherList");
const mainHeadingEl = document.getElementById("mainHeading");
const otherSectionEl = document.getElementById("otherSection");
const otherSummaryEl = document.getElementById("otherSummary");
const emptyEl = document.getElementById("empty");
const statusEl = document.getElementById("status");

let rawItems = []; // { url, type, group, capturedAt, main }
const probedByUrl = new Map(); // url -> probeMedia result ({ kind, size } or { kind: "hls", variants })
let currentTabId = null;
let isUnsupportedHost = false;
let currentGroups = [];
const selectsByGroup = new Map(); // group.key -> <select> element (or null when ungrouped)

async function activeTab() {
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

// "720p" stays as-is; short word tokens read better upper-cased in a compact dropdown ("HD").
function qualityLabel(url) {
  const token = extractQualityToken(url);
  if (!token) return fileName(url);
  return /^\d/.test(token) ? token : token.toUpperCase();
}

// Collapses rawItems into one card per group (see common.js's groupKey / design.md Decision 4),
// then layers in whatever probeMedia has resolved so far (sizes, and for HLS masters, the real
// quality variants replacing the single placeholder option).
function buildGroups() {
  const map = new Map();
  for (const item of rawItems) {
    const key = item.group || groupKey(item.url);
    let g = map.get(key);
    if (!g) {
      g = { key, kind: extOf(item.url) === "m3u8" ? "hls" : "direct", main: false, options: [] };
      map.set(key, g);
    }
    g.main = g.main || !!item.main;
    if (!g.options.some(o => o.url === item.url))
      g.options.push({ url: item.url, label: null, size: null, approx: false });
  }

  for (const g of map.values()) {
    const probed = probedByUrl.get(g.key);
    if (g.kind === "hls" && probed?.kind === "hls" && probed.variants.length) {
      g.options = probed.variants.map(v => ({
        url: v.uri,
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
    g.title = fileName(g.kind === "hls" ? g.key : g.options[0]?.url ?? g.key);

    // Drop options a probe confirmed are implausibly tiny for real media (tracking beacons,
    // empty init segments — e.g. sub-1KB responses seen on X.com) — never before a probe has run.
    g.options = g.options.filter(opt => isPlausibleMediaSize(opt.size));
  }
  for (const [key, g] of map) if (g.options.length === 0) map.delete(key);
  return [...map.values()];
}

function buildCard(group) {
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
      o.value = opt.url;
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
    const url = select ? select.value : group.options[0]?.url;
    return group.options.find(o => o.url === url) || group.options[0];
  };
  const updateSize = () => {
    const opt = currentOption();
    const human = opt && formatBytes(opt.size);
    sizeEl.textContent = human ? (opt.approx ? "~" : "") + human : "";
  };
  if (select) select.onchange = updateSize;
  updateSize();

  const btn = document.createElement("button");
  btn.className = "primary";
  btn.textContent = "Download";
  btn.onclick = () => sendOne(currentOption()?.url, btn);

  li.append(meta, btn);
  return li;
}

function render() {
  // Known-unsupported sites (YouTube, Netflix, …) ALWAYS show the explanatory message and
  // suppress the list — even when something was incidentally sniffed (e.g. YouTube's own UI
  // sound-effect mp3s), since none of it is ever the protected video content the user wants.
  // Real-world fix: v1.2.0 only checked this when zero items existed, so those unrelated sounds
  // were shown as if they were downloadable.
  if (isUnsupportedHost) {
    currentGroups = [];
    selectsByGroup.clear();
    mainListEl.innerHTML = "";
    otherListEl.innerHTML = "";
    mainHeadingEl.style.display = "none";
    otherSectionEl.style.display = "none";
    emptyEl.style.display = "block";
    emptyEl.classList.add("unsupported");
    emptyEl.textContent = "This site streams video in a format Downloader can't capture directly.";
    return;
  }

  currentGroups = buildGroups();
  selectsByGroup.clear();
  const mainGroups = currentGroups.filter(g => g.main);
  const otherGroups = currentGroups.filter(g => !g.main);

  mainListEl.innerHTML = "";
  otherListEl.innerHTML = "";
  for (const g of mainGroups) mainListEl.append(buildCard(g));
  for (const g of otherGroups) otherListEl.append(buildCard(g));

  mainHeadingEl.style.display = currentGroups.length ? "flex" : "none";
  otherSectionEl.style.display = otherGroups.length ? "block" : "none";
  otherSummaryEl.textContent = `Other detected (${otherGroups.length})`;

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
  rawItems.push({ url, type: type || extOf(url), group: groupKey(url), capturedAt: Date.now(), main: false });
}

async function sendOne(url, btn) {
  if (!url) return;
  if (btn) { btn.disabled = true; btn.textContent = "…"; }
  const { ok } = await send("send", { url });
  if (btn) { btn.textContent = ok ? "Sent ✓" : "Failed"; }
}

async function refreshStatus() {
  const { ok } = await send("ping", {});
  statusEl.className = "status " + (ok ? "on" : "off");
  statusEl.title = ok ? "Desktop app connected" : "Desktop app not reachable — start it and enable browser integration";
}

// Renders immediately with whatever's known, then upgrades in place once probes resolve — a slow
// or blocked probe never delays first paint (design.md Decision 6).
async function probeAndRender() {
  if (currentTabId == null) return;
  const { results } = await send("probeMedia", { tabId: currentTabId });
  for (const r of results || []) if (r) probedByUrl.set(r.url, r);
  render();
}

async function loadDetected() {
  const tab = await activeTab();
  currentTabId = tab.id;
  try { isUnsupportedHost = isKnownUnsupportedHost(new URL(tab.url).hostname); } catch { isUnsupportedHost = false; }
  const { media } = await send("getMedia", { tabId: tab.id });
  for (const m of media || []) if (!rawItems.some(i => i.url === m.url)) rawItems.push(m);
  render();
  probeAndRender();
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
    const url = select ? select.value : g.options[0]?.url;
    await sendOne(url);
  }
};

// Silent-vs-dialog choice (persisted; the background worker reads it on every send).
const silentEl = document.getElementById("silentMode");
getAddMode().then(mode => { silentEl.checked = mode === "silent"; });
silentEl.onchange = () => setAddMode(silentEl.checked ? "silent" : "dialog");

refreshStatus();
loadDetected();
