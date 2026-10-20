// A static server for a published wwwroot with SPA fallback (a deep link serves index.html), for the trimmed-publish
// check: the dev server is not what a published app runs under. A malformed percent-escape is a 400 and a path that
// resolves outside the root is a 403; neither reaches the file system.
//   node tools/serve.mjs <wwwroot dir> <port> [base path such as /myapp]
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, resolve, sep } from 'node:path';

const [root, port = '5177', basePath = ''] = process.argv.slice(2);
const rootDir = resolve(root);
const types = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
  '.wasm': 'application/wasm', '.css': 'text/css', '.dll': 'application/octet-stream', '.dat': 'application/octet-stream',
  '.pdb': 'application/octet-stream', '.ico': 'image/x-icon', '.svg': 'image/svg+xml', '.woff2': 'font/woff2',
  '.br': 'application/octet-stream', '.gz': 'application/gzip',
};

const reply = (res, status, body = '') => { res.writeHead(status, { 'content-type': 'text/plain' }); res.end(body); };

createServer(async (req, res) => {
  let pathname;
  try {
    const url = new URL(req.url, 'http://x');
    pathname = basePath && url.pathname.startsWith(basePath) ? url.pathname.slice(basePath.length) : url.pathname;
    pathname = decodeURIComponent(pathname);
  } catch {
    reply(res, 400, 'malformed URL');
    return;
  }

  // Resolved against the root and confined to it: "..", a backslash segment or a rooted path cannot escape.
  let path = resolve(rootDir, '.' + (pathname.startsWith('/') ? pathname : '/' + pathname));
  if (path !== rootDir && !path.startsWith(rootDir + sep)) {
    reply(res, 403, 'outside the served root');
    return;
  }

  let ok = false;
  try { ok = (await stat(path)).isFile(); } catch { /* fallback */ }
  if (!ok) path = resolve(rootDir, 'index.html');
  try {
    const body = await readFile(path);
    res.writeHead(200, { 'content-type': types[extname(path)] ?? 'application/octet-stream', 'cache-control': 'no-store' });
    res.end(body);
  } catch {
    reply(res, 404);
  }
}).listen(Number(port), () => console.log(`serving ${rootDir} on http://localhost:${port}${basePath}/`));
