// Tiny static file server with Range support, used only by the e2e tests to serve fixtures/ at a
// fixed local port. No dependencies — plain Node http.
"use strict";
const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");

const ROOT = path.join(__dirname, "fixtures");
const PORT = 8991;

const MIME = { ".html": "text/html", ".m3u8": "application/vnd.apple.mpegurl", ".mp4": "video/mp4", ".ts": "video/mp2t", ".zip": "application/zip" };

function start() {
  const server = http.createServer((req, res) => {
    const urlPath = decodeURIComponent(req.url.split("?")[0]);
    const filePath = path.join(ROOT, urlPath);
    if (!filePath.startsWith(ROOT)) { res.writeHead(403).end(); return; }
    fs.stat(filePath, (err, stat) => {
      if (err || !stat.isFile()) { res.writeHead(404).end("not found"); return; }
      const ext = path.extname(filePath);
      const type = MIME[ext] || "application/octet-stream";
      const range = req.headers.range;
      // ?attach=1 makes the browser DOWNLOAD the file rather than render it — that is what fires
      // chrome.downloads.onCreated, which the interception tests need.
      if (/[?&]attach=1/.test(req.url)) {
        res.setHeader("Content-Disposition", `attachment; filename="${path.basename(filePath)}"`);
      }
      if (req.method === "HEAD") {
        res.writeHead(200, { "Content-Type": type, "Content-Length": stat.size });
        res.end();
        return;
      }
      if (range) {
        const m = /bytes=(\d+)-(\d*)/.exec(range);
        const start = m ? parseInt(m[1], 10) : 0;
        const end = m && m[2] ? parseInt(m[2], 10) : stat.size - 1;
        res.writeHead(206, {
          "Content-Type": type,
          "Content-Range": `bytes ${start}-${end}/${stat.size}`,
          "Content-Length": end - start + 1
        });
        fs.createReadStream(filePath, { start, end }).pipe(res);
        return;
      }
      // ?slow=1 trickles the body out over a few seconds. The interception tests need a download
      // that is still IN PROGRESS when the extension gets around to cancelling it — a local 200 KB
      // file otherwise completes before the hand-off (port discovery + cookie capture + POST) even
      // finishes, and cancelling a finished download is a no-op.
      if (/[?&]slow=1/.test(req.url)) {
        res.writeHead(200, { "Content-Type": type, "Content-Length": stat.size });
        const stream = fs.createReadStream(filePath, { highWaterMark: 8 * 1024 });
        stream.on("data", chunk => {
          stream.pause();
          res.write(chunk);
          setTimeout(() => stream.resume(), 120);
        });
        stream.on("end", () => res.end());
        return;
      }
      res.writeHead(200, { "Content-Type": type, "Content-Length": stat.size });
      fs.createReadStream(filePath).pipe(res);
    });
  });
  return new Promise(resolve => server.listen(PORT, () => resolve(server)));
}

if (require.main === module) start();

module.exports = { start, PORT };
