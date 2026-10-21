// Minimal Chrome DevTools Protocol driver (Node 22+, no dependencies): launches headless Chrome on a free port with a
// temporary profile, connects to its first page and exposes evaluate/navigate helpers. Used by verify.mjs and
// verify-modes.mjs. Chrome is killed and the profile removed when close() runs, when the process exits and on SIGINT/SIGTERM,
// so a killed run leaves no Chrome behind for the next run to attach to.
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// CHROME_PATH overrides the default: chrome.exe on Windows, the app bundle on macOS, `google-chrome` on the PATH elsewhere.
const CHROME = process.env.CHROME_PATH ?? ({
  win32: 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  darwin: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
}[process.platform] ?? 'google-chrome');

export const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

// A blocking sleep for the exit handler, which cannot await.
const sleepSync = ms => Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);

/**
 * Launches Chrome. Without `port` Chrome picks a free one (`--remote-debugging-port=0`) and reports it in the profile's
 * DevToolsActivePort file, so two runs never share a port and a stale Chrome is never mistaken for the new one.
 */
export async function launch(port = 0) {
  const profile = mkdtempSync(join(tmpdir(), 'modrouter-chrome-'));
  const process_ = spawn(CHROME, [
    '--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
    '--window-size=1200,800', '--no-first-run', '--disable-gpu',
    // On a CI runner Chrome's sandbox is often unavailable (user namespaces are restricted), and the runner is disposable anyway.
    ...(process.env.CI ? ['--no-sandbox'] : []),
    'about:blank',
  ], { stdio: 'ignore' });

  let cleaned = false;
  const cleanup = () => {
    if (cleaned) return;
    cleaned = true;
    // Synchronous and for the whole tree: the exit handler cannot wait, and Chrome's renderer and GPU processes hold the
    // profile open until they are gone too. rmSync then retries while the handles are released.
    if (process.platform === 'win32') spawnSync('taskkill', ['/PID', String(process_.pid), '/T', '/F'], { stdio: 'ignore' });
    else { try { process_.kill('SIGKILL'); } catch { /* already gone */ } }
    // rmSync's own retries do not cover the EPERM a just-killed Chrome leaves on the directory, so retry by hand (about 3 s).
    for (let attempt = 0; attempt < 30; attempt++) {
      try { rmSync(profile, { recursive: true, force: true }); break; } catch { sleepSync(100); }
    }
  };
  const onSignal = signal => { cleanup(); process.exit(signal === 'SIGINT' ? 130 : 143); };
  process.on('exit', cleanup);
  process.on('SIGINT', onSignal);
  process.on('SIGTERM', onSignal);
  const forget = () => { process.off('exit', cleanup); process.off('SIGINT', onSignal); process.off('SIGTERM', onSignal); };

  try {
    let target;
    for (let attempt = 0; attempt < 50 && !target; attempt++) {
      try {
        if (!port) port = Number(readFileSync(join(profile, 'DevToolsActivePort'), 'utf8').split(/\r?\n/)[0]);
        if (port) {
          const targets = await (await fetch(`http://localhost:${port}/json`)).json();
          target = targets.find(t => t.type === 'page');
        }
      } catch { /* Chrome is not up yet */ }
      if (!target) await sleep(200);
    }
    if (!target) throw new Error('Chrome did not start (set CHROME_PATH to the Chrome executable)');

    const socket = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });

    let nextId = 1;
    const pending = new Map();
    const messages = [];
    socket.onmessage = event => {
      const message = JSON.parse(event.data);
      if (message.id && pending.has(message.id)) {
        const { resolve, reject } = pending.get(message.id);
        pending.delete(message.id);
        if (message.error) reject(new Error(JSON.stringify(message.error)));
        else resolve(message.result);
      } else if (message.method === 'Runtime.exceptionThrown') {
        messages.push('EXCEPTION ' + (message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text));
      } else if (message.method === 'Runtime.consoleAPICalled') {
        messages.push(message.params.args.map(a => a.value ?? a.description ?? '').join(' '));
      }
    };

    // Every call has a deadline: a Chrome that stalls (or a page whose evaluate never settles) fails the run instead of
    // hanging it. CDP_TIMEOUT_MS overrides the default 30 s.
    const timeoutMs = Number(process.env.CDP_TIMEOUT_MS ?? 30000);
    const send = (method, params = {}) => new Promise((resolve, reject) => {
      const id = nextId++;
      const timer = setTimeout(() => {
        if (pending.delete(id)) reject(new Error(`CDP call ${method} (#${id}) timed out after ${timeoutMs} ms`));
      }, timeoutMs);
      pending.set(id, {
        resolve: value => { clearTimeout(timer); resolve(value); },
        reject: error => { clearTimeout(timer); reject(error); },
      });
      socket.send(JSON.stringify({ id, method, params }));
    });
    await send('Runtime.enable');
    await send('Page.enable');

    return {
      messages,
      port,
      /** A raw CDP call (keyboard input, screenshots). */
      send,
      /** Evaluates a function body in the page and returns its (JSON-serializable) result. */
      async evaluate(body) {
        const result = await send('Runtime.evaluate', { expression: `(async()=>{${body}})()`, awaitPromise: true, returnByValue: true });
        if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
        return result.result.value;
      },
      async navigate(url) { await send('Page.navigate', { url }); await sleep(300); },
      async close() {
        try { socket.close(); } catch { /* ignore */ }
        const exited = new Promise(resolve => { if (process_.exitCode !== null) resolve(); else process_.once('exit', resolve); });
        try { process_.kill(); } catch { /* already gone */ }
        await Promise.race([exited, sleep(3000)]);
        cleanup();
        forget();
      },
    };
  } catch (error) {
    cleanup();
    forget();
    throw error;
  }
}

export async function waitFor(page, condition, timeout = 15000) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    // Each poll gets only the time left, so a stalled evaluate cannot overrun the deadline by the per-call CDP timeout.
    let timer;
    const expired = new Promise(resolve => { timer = setTimeout(() => resolve(false), Math.max(0, end - Date.now())); });
    try {
      if (await Promise.race([page.evaluate(`return (${condition});`), expired])) return;
    } catch { /* page still loading */ } finally {
      clearTimeout(timer);
    }
    await sleep(100);
  }
  throw new Error('timed out waiting for: ' + condition);
}
