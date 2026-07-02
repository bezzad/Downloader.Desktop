# Local API & command line

Downloader can be driven by other programs on your computer: a **local HTTP API** and a
**command-line interface (CLI)** let scripts (Node.js, Bun, Python, shell — anything that can make
an HTTP request or run a program) add and manage downloads without touching the window.

- Everything is **local-only**: the app listens on `127.0.0.1:15151` (loopback). Nothing is exposed
  to your network or the internet.
- It is controlled by **Settings → Browser extension & local API** (on by default; turn it off if
  you don't want other local programs adding downloads).
- There is no authentication token — any program running on your own machine may use it.

## HTTP API

Base URL: `http://127.0.0.1:15151`

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
