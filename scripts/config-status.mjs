// Which configs the smoke run checks, and what it found.
//
//   node scripts/config-status.mjs pick   --status status.json [--count 10]
//   node scripts/config-status.mjs record --status status.json --results <dir>
//
// pick prints a JSON list of paths under configs/: the ones tested longest ago first, never-tested
// ones before all of them, and nothing listed in configs/CI-SKIP. Ten a week reaches every config in
// about four months with one job a week, which is the point: nothing runs that does not need to.
//
// record folds the smoke jobs' result files into status.json, which lives on the `status` branch so
// that a test result is never a commit on main.
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

export function allConfigs(root) {
  const found = [];
  for (const owner of readdirSync(root, { withFileTypes: true })) {
    if (!owner.isDirectory()) continue;
    for (const file of readdirSync(join(root, owner.name)))
      if (file.endsWith('.yml')) found.push(`${owner.name}/${file}`);
  }
  return found.sort();
}

export function skipped(text) {
  return new Set(String(text ?? '').split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith('#')));
}

export function pick(configs, status, skip, count) {
  const when = (config) => status[config]?.date ?? '';
  return configs
    .filter((config) => !skip.has(config))
    .sort((a, b) => when(a).localeCompare(when(b)) || a.localeCompare(b))
    .slice(0, count);
}

export function record(status, results) {
  const next = { ...status };
  for (const r of results)
    next[r.config] = { ok: r.ok, date: r.date, commit: r.commit ?? null, ...(r.ok ? {} : { error: r.error ?? null }) };
  return Object.fromEntries(Object.entries(next).sort(([a], [b]) => a.localeCompare(b)));
}

const read = (path, fallback) => (existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : fallback);

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const arg = (name, fallback) => {
    const at = process.argv.indexOf(name);
    return at > 0 ? process.argv[at + 1] : fallback;
  };
  const statusPath = arg('--status', 'status.json');
  const status = read(statusPath, {});

  if (process.argv[2] === 'pick') {
    const skip = skipped(existsSync('configs/CI-SKIP') ? readFileSync('configs/CI-SKIP', 'utf8') : '');
    console.log(JSON.stringify(pick(allConfigs('configs'), status, skip, Number(arg('--count', 10)))));
  } else if (process.argv[2] === 'record') {
    const dir = arg('--results');
    const results = readdirSync(dir, { recursive: true })
      .filter((f) => String(f).endsWith('result.json'))
      .map((f) => read(join(dir, String(f))));
    writeFileSync(statusPath, JSON.stringify(record(status, results), null, 2) + '\n');
    console.log(`recorded ${results.length} result(s)`);
  } else {
    console.error('usage: config-status.mjs pick|record ...');
    process.exit(2);
  }
}
