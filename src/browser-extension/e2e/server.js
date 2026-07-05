// Tiny static file server with Range support, used only by the e2e tests to serve fixtures/ at a
// fixed local port. No dependencies — plain Node http.
"use strict";
const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");

const ROOT = path.join(__dirname, "fixtures");
const PORT = 8991;

const MIME = { ".html": "text/html", ".m3u8": "application/vnd.apple.mpegurl", ".mp4": "video/mp4", ".ts": "video/mp2t" };

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
      res.writeHead(200, { "Content-Type": type, "Content-Length": stat.size });
      fs.createReadStream(filePath).pipe(res);
    });
  });
  return new Promise(resolve => server.listen(PORT, () => resolve(server)));
}

if (require.main === module) start();

module.exports = { start, PORT };
