// Options page: the interception rules. Reads and writes the one versioned settings object through
// common.js, so the rules the listener applies and the rules shown here can never drift apart.
// Every change saves immediately and takes effect on the next download — there is nothing to reload,
// because background.js reads the settings per download rather than caching them.

const els = {
  status: document.getElementById("status"),
  firstRun: document.getElementById("firstRun"),
  firstRunEnable: document.getElementById("firstRunEnable"),
  firstRunDismiss: document.getElementById("firstRunDismiss"),
  enabled: document.getElementById("enabled"),
  rules: document.getElementById("rules"),
  typeMode: document.getElementById("typeMode"),
  fileTypes: document.getElementById("fileTypes"),
  resetTypes: document.getElementById("resetTypes"),
  minSize: document.getElementById("minSize"),
  excludedSites: document.getElementById("excludedSites"),
  saved: document.getElementById("saved")
};

// "zip, exe\niso" -> ["zip","exe","iso"]. Accepts either separator because people paste both.
function parseList(text) {
  return String(text || "")
    .split(/[\s,]+/)
    .map(s => s.trim().replace(/^\./, "").toLowerCase())
    .filter(Boolean);
}

function render(settings) {
  els.enabled.checked = settings.enabled;
  els.typeMode.value = settings.fileTypes.mode;
  els.fileTypes.value = settings.fileTypes.list.join(", ");
  els.excludedSites.value = settings.excludedSites.join("\n");

  // An arbitrary stored minimum (set before, or hand-edited) must still be shown rather than
  // silently snapped to a preset — otherwise saving from this page would quietly change the rule.
  const size = String(settings.minSizeBytes || 0);
  if (![...els.minSize.options].some(o => o.value === size)) {
    const opt = document.createElement("option");
    opt.value = size;
    opt.textContent = `Skip files under ${Math.round(settings.minSizeBytes / 1048576)} MB`;
    els.minSize.appendChild(opt);
  }
  els.minSize.value = size;

  els.rules.disabled = !settings.enabled;
}

function collect() {
  return {
    enabled: els.enabled.checked,
    minSizeBytes: Number(els.minSize.value) || 0,
    fileTypes: { mode: els.typeMode.value === "deny" ? "deny" : "allow", list: parseList(els.fileTypes.value) },
    excludedSites: parseList(els.excludedSites.value.replace(/,/g, "\n"))
  };
}

let savedTimer = null;
async function save() {
  const settings = await setInterceptSettings(collect());
  els.rules.disabled = !settings.enabled;
  els.saved.hidden = false;
  clearTimeout(savedTimer);
  savedTimer = setTimeout(() => { els.saved.hidden = true; }, 1500);
  return settings;
}

// Has the user ever made a deliberate choice here? Stored separately from the settings themselves,
// so "never asked" stays distinguishable from "asked, and chose off" — otherwise the first-run
// explanation would come back every visit for anyone who declined it.
async function seenFirstRun() {
  try {
    const r = await api.storage.local.get({ interceptIntroSeen: false });
    return r.interceptIntroSeen === true;
  } catch {
    return true; // can't tell — don't nag
  }
}

function markFirstRunSeen() {
  try { api.storage.local.set({ interceptIntroSeen: true }); } catch { /* optional */ }
}

async function showAppStatus() {
  const ok = await pingApp();
  els.status.textContent = ok ? "App connected" : "App not running";
  els.status.className = `status ${ok ? "on" : "off"}`;
}

(async function init() {
  render(await getInterceptSettings());
  showAppStatus();

  if (!(await seenFirstRun())) els.firstRun.hidden = false;

  els.firstRunEnable.addEventListener("click", async () => {
    els.enabled.checked = true;
    await save();
    markFirstRunSeen();
    els.firstRun.hidden = true;
  });
  els.firstRunDismiss.addEventListener("click", () => {
    markFirstRunSeen();
    els.firstRun.hidden = true;
  });

  // Turning it on from the main switch is just as deliberate as using the callout button.
  els.enabled.addEventListener("change", async () => { markFirstRunSeen(); els.firstRun.hidden = true; await save(); });
  els.typeMode.addEventListener("change", save);
  els.minSize.addEventListener("change", save);
  for (const el of [els.fileTypes, els.excludedSites]) {
    el.addEventListener("change", save); // on blur — saving per keystroke would fight the typist
  }
  els.resetTypes.addEventListener("click", async () => {
    els.fileTypes.value = INTERCEPT_DEFAULTS.fileTypes.list.join(", ");
    els.typeMode.value = "allow";
    await save();
  });
})();
