// Runs every real-browser check against one running sample, one after the other, and fails if any of them does:
//   node tools/verify-all.mjs http://localhost:5199/
// The sample can be the dev server (dotnet run) or a publish served by tools/serve.mjs. CI runs it against the trimmed Release
// publish. Needs Node 22+ and Chrome (CHROME_PATH overrides where to find it).
import { spawnSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const url = process.argv[2] ?? 'http://localhost:5199/';
const here = dirname(fileURLToPath(import.meta.url));

const runs = [
  ['navigation, guards, loaders, links, title, focus and scroll', 'verify.mjs', []],
  ['the pages and markup route sources', 'verify-modes.mjs', ['all']],
  ['the compatibility layer', 'verify-compat.mjs', []],
];

const failed = [];

for (const [name, script, extra] of runs) {
  console.log(`\n=== ${name} (${script})`);
  const result = spawnSync(process.execPath, [join(here, script), url, ...extra], { stdio: 'inherit' });

  if (result.status !== 0) {
    failed.push(script);
  }
}

if (failed.length > 0) {
  console.error(`\nFAILED: ${failed.join(', ')}`);
  process.exit(1);
}

console.log('\nAll browser checks passed.');
