// Popup UI: shows media detected on the active tab, lets the user scan page links, paste a URL,
// and send any/all of them to the desktop app.
const listEl = document.getElementById("list");
const emptyEl = document.getElementById("empty");
const statusEl = document.getElementById("status");

let items = []; // { url, type }

async function activeTab() {
  const [tab] = await api.tabs.query({ active: true, currentWindow: true });
  return tab;
}

function send(type, payload) {
  return new Promise(resolve => api.runtime.sendMessage({ type, ...payload }, resolve));
}

function render() {
  listEl.innerHTML = "";
  emptyEl.style.display = items.length ? "none" : "block";
  // The first master (top of the prioritized list) is the most likely "main video".
  const mainUrl = items.find(i => i.kind === "master")?.url;
  for (const it of items) {
    const li = document.createElement("li");
    if (it.url === mainUrl) li.className = "main";
    const meta = document.createElement("div");
    meta.className = "meta";
    const name = document.createElement("div");
    name.className = "name";
    name.textContent = (it.url === mainUrl ? "★ " : "") + fileName(it.url);
    name.title = it.url;
    const type = document.createElement("div");
    type.className = "type";
    type.textContent = (it.label || it.type || "").toString();
    meta.append(name, type);
    const btn = document.createElement("button");
    btn.className = "primary";
    btn.textContent = "Download";
    btn.onclick = () => sendOne(it.url, btn);
    li.append(meta, btn);
    listEl.append(li);
  }
}

function fileName(url) {
  try {
    const p = new URL(url).pathname;
    return decodeURIComponent(p.split("/").pop()) || url;
  } catch { return url; }
}

function addItem(url, type, kind, label) {
  if (!isHttp(url) || items.some(i => i.url === url)) return;
  items.push({ url, type: type || extOf(url), kind: kind || "file", label });
}

async function sendOne(url, btn) {
  if (btn) { btn.disabled = true; btn.textContent = "…"; }
  const { ok } = await send("send", { url });
  if (btn) { btn.textContent = ok ? "Sent ✓" : "Failed"; }
}

async function refreshStatus() {
  const { ok } = await send("ping", {});
  statusEl.className = "status " + (ok ? "on" : "off");
  statusEl.title = ok ? "Desktop app connected" : "Desktop app not reachable — start it and enable browser integration";
}

async function loadDetected() {
  const tab = await activeTab();
  const { media } = await send("getMedia", { tabId: tab.id });
  for (const m of media || []) addItem(m.url, m.type, m.kind, m.label);
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
  for (const it of items) await sendOne(it.url);
};

refreshStatus();
loadDetected();
