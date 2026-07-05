# Local API & command line

Downloader can be driven by other programs on your computer: a **local HTTP API** and a
**command-line interface (CLI)** let scripts (Node.js, Bun, Python, shell — anything that can make
an HTTP request or run a program) add and manage downloads without touching the window.

- Everything is **local-only**: the app listens on `127.0.0.1:15151` (loopback). Nothing is exposed
  to your network or the internet.
- If another program already holds `15151`, the app automatically falls back to the next free port
  in the small fixed range `15151`–`15155` and notifies you once. The effective address is shown in
  **Settings** next to the integration toggle; the browser extension and the CLI rediscover the
  port automatically, but your own scripts should check Settings (or probe `/ping` across the
  range) if the default doesn't answer.
- It is controlled by **Settings → Browser extension & local API** (on by default; turn it off if
  you don't want other local programs adding downloads).
- There is no authentication token — any program running on your own machine may use it.

## Listen port & automatic fallback

The app prefers **`15151`**. If another program already holds it when the app starts, the app binds
the next free port in a small, fixed range and remembers it:

| | |
|---|---|
| **Preferred port** | `15151` |
| **Fallback range** | `15151` → `15152` → `15153` → `15154` → `15155` (tried in order) |
| **On fallback** | the app shows a one-time notification and displays the live address in **Settings → Local API address** (with a green “connected” dot) |
| **Remembered** | the chosen port is saved; on the next start the app tries the last-known-good port first, so it stays stable |
| **All five taken** | the API simply doesn’t start (the rest of the app is unaffected); Settings shows “not running” |

You never configure the port by hand — a manually chosen port outside this range would be invisible
to the browser extension (see below), so the range is fixed on purpose.

### How the browser extension finds the port

The extension can’t just “use whatever port the app picked”, because a browser extension may only
talk to origins it declared **at install time** (Manifest V3 `host_permissions`). So the extension’s
manifest declares the **whole `15151`–`15155` range** up front, and at runtime it **discovers** which
one the app is actually on:

1. It remembers the **last port that worked** (in extension storage) and tries that first.
2. Otherwise it probes `/ping` across `15151`…`15155` **in order** and uses the first that answers
   `200`.
3. On success it caches that port, so the next add is instant (no re-scan).

This means: if the app falls back from `15151` to, say, `15153`, the extension re-discovers `15153`
on its next action automatically — **you don’t reinstall or reconfigure anything.** The same applies
to the CLI, which reads the app’s remembered port and probes the range the same way.

> **Note:** the app’s single-instance / CLI lock uses port **`15150`**, deliberately *outside* the
> API range, so all five ports `15151`–`15155` are genuinely available to the API.

### For your own scripts

If you write your own script and the default port doesn’t answer, either read the address shown in
**Settings → Local API address**, or probe `/ping` across `15151`–`15155` and use the first port that
returns `200` (that’s exactly what the extension and CLI do):

```bash
for p in 15151 15152 15153 15154 15155; do
  curl -s -o /dev/null -m 1 "http://127.0.0.1:$p/ping" && { echo "Downloader is on $p"; break; }
done
```

## HTTP API

Base URL: `http://127.0.0.1:15151` (or the fallback port shown in Settings — see above)

### Add a download — `POST /api/add` (or `GET` with query parameters)

| Field | Required | Meaning |
|---|---|---|
| `url` | yes | Absolute `http`/`https` link to download |
| `filename` | no | File name to save as (otherwise auto-resolved) |
| `path` | no | Absolute folder to save into (otherwise your default save path) |
| `queue` | no | Queue name or id (otherwise the default queue) |
| `mirrors` | no | Extra URLs for the same file (JSON body only) |
| `start` | no | `false` = add queued but don't start (default `true`) |

The download is added **silently** — no dialog — and starts immediately (subject to the queue's
concurrency cap). Responses: `201` with `{"id","name","status"}`, or `400` with `{"error"}`.

```bash
curl -X POST http://127.0.0.1:15151/api/add \
  -d '{"url":"https://example.com/file.zip","filename":"file.zip","path":"/home/me/Downloads"}'
```

```js
// Node.js / Bun
const res = await fetch("http://127.0.0.1:15151/api/add", {
  method: "POST",
  body: JSON.stringify({
    url: "https://example.com/file.zip",
    filename: "file.zip",          // optional
    path: "/home/me/Downloads",    // optional
    start: true                    // optional (default true)
  })
});
const { id } = await res.json();   // keep the id to track or control it later
```

### List downloads — `GET /api/list`

Returns a JSON array; each entry has `id`, `name`, `url`, `status` (`Created`, `Running`, `Paused`,
`Stopped`, `Completed`, `Failed`), `progress` (0–100), `size`, `downloaded`, `speed` (bytes/s),
`folder`, `filePath` and `queue`.

```js
const downloads = await (await fetch("http://127.0.0.1:15151/api/list")).json();
const done = downloads.filter(d => d.status === "Completed");
```

### Control a download — `POST /api/pause|resume|cancel|retry|remove`

Send `{"id":"<id>"}` (or `GET …?id=<id>`). Returns `200 {"ok":true}` on success, `404` for an
unknown id. Actions that don't apply to the item's current state (e.g. pausing a finished download)
are safe no-ops and still return `200`.

```bash
curl -X POST http://127.0.0.1:15151/api/pause -d '{"id":"9f8b4c1e-…"}'
```

### Endpoints used by the browser extension

`GET /add?url=…` (opens the Add dialog pre-filled) and `GET /ping` (health check) are unchanged and
remain available.

## Command line

The same verbs are available from a terminal using the app's executable (installed as
`Downloader`/`Downloader.exe`; substitute the full path if it's not on your PATH):

```bash
Downloader add --url https://example.com/file.zip [--filename file.zip] [--path /folder] [--queue Main] [--no-start]
Downloader list
Downloader pause|resume|cancel|retry|remove <id>
```

- **`add` always works**: it hands the link to the running app, or starts the app (in the
  background) and adds it there. Your script never blocks on the GUI.
- **`list` and the control verbs** talk to the local API, so the app must be running with the
  Settings toggle on; otherwise they print a one-line error.
- `list` prints the raw JSON array — pipe it to `jq` for pretty output.

Exit codes: `0` success · `1` error (app not running / API off / unknown id) · `2` usage error.

> **Windows note:** the app is a windowed program, so CLI output can appear *after* the prompt
> returns in `cmd`/PowerShell. The command still worked — press Enter to get a clean prompt.

## Batch example

```js
// Queue a list of links from anywhere (database, file, …) — Node.js or Bun.
const links = ["https://example.com/a.zip", "https://example.com/b.zip"];
for (const url of links) {
  await fetch("http://127.0.0.1:15151/api/add", { method: "POST", body: JSON.stringify({ url }) });
}
```
