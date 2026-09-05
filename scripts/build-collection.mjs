// Builds configs/ from AspireUI's container catalogue.
//
// The collection is QuickRun's answer for repositories that will never commit a quickrun.yml: a
// config written for the repository beats QuickRun reading its files and deciding. AspireUI already
// knows how a hundred and sixty of these applications start - which image, which port, which
// companion - so the catalogue is the source and this turns it into configs.
//
//   node scripts/build-collection.mjs --presets <path to container-presets.json> [--out configs]
//
// Generated rather than hand-written, so a change in the catalogue is one command away from being a
// change here. Anything hand-written for a repository the catalogue does not know goes in the same
// tree and is left alone: this only ever writes files it generated, listed in configs/GENERATED.
//
// The configs start the application's official image. That is what these repositories publish and
// what actually works; building a hundred and sixty projects from source is a different promise.
// Nothing is kept: no volumes, so a run leaves nothing behind - said in every description.

import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';

const argument = (name, fallback = null) => {
  const at = process.argv.indexOf(name);
  return at > 0 ? process.argv[at + 1] : fallback;
};

const presetsPath = argument('--presets');
const outDir = argument('--out', 'configs');

if (!presetsPath) {
  console.error('usage: node scripts/build-collection.mjs --presets <container-presets.json> [--out configs]');
  process.exit(2);
}

const presets = JSON.parse(readFileSync(presetsPath, 'utf8'));

/** owner/repo out of whatever the catalogue put in `github`. */
function repoOf(url) {
  const match = String(url ?? '').match(/github\.com[/:]([^/]+)\/([^/#?]+)/i);
  if (!match) return null;

  const [, owner, repo] = match;
  const name = repo.replace(/\.git$/i, '');

  // The same narrow alphabet the daemon accepts, because these become URLs and file names.
  if (!/^[A-Za-z0-9._-]+$/.test(owner) || !/^[A-Za-z0-9._-]+$/.test(name)) return null;

  return { owner, repo: name };
}

/** YAML for a single string value, quoted only when it has to be. */
function scalar(value) {
  const text = String(value ?? '');

  if (text.length === 0) return "''";

  // Anything that could be read as structure, a number, or a boolean gets quoted.
  if (/^[A-Za-z][A-Za-z0-9 ._/-]*$/.test(text) && !/^(true|false|yes|no|on|off|null|~)$/i.test(text))
    return text;

  return `'${text.replace(/'/g, "''")}'`;
}

/** A description folded into one YAML block, so a long sentence stays readable in the file. */
function block(text, indent = '  ') {
  const words = String(text ?? '').replace(/\s+/g, ' ').trim().split(' ');
  const lines = [];
  let line = '';

  for (const word of words) {
    if ((line + ' ' + word).trim().length > 92) { lines.push(line.trim()); line = word; }
    else line = (line + ' ' + word).trim();
  }
  if (line) lines.push(line.trim());

  return '>-\n' + lines.map((l) => indent + l).join('\n');
}

/** The container name a task uses, unique per app so two runs cannot collide. */
const containerName = (app, suffix = null) =>
  `quickrun-${app.id}${suffix ? '-' + suffix : ''}`;

/**
 * The docker command for one container.
 *
 * --rm and no volume on purpose: this is "try it", not "install it". Every description says so, so
 * nobody puts their household budget in one of these and loses it on stop.
 */
function dockerRun(app, { name, image, port, env }) {
  const parts = ['docker run --rm', `--name ${name}`, `--network ${containerName(app, 'net')}`];

  if (port) parts.push(`-p ${port}:${port}`);

  for (const [key, value] of env ?? []) parts.push(`-e ${key}=${shellValue(value)}`);

  parts.push(image);
  return parts.join(' ');
}

/**
 * An environment value as it goes on a command line: quoted only where it has to be.
 *
 * These used to be quoted always, and a version of QuickRun that handed cmd.exe an escaped argument
 * list turned the quotes into part of the value - a database whose password was "passbolt", quotes
 * and all, and an application looking for a host whose name had punctuation in it. That is fixed in
 * the tool, but these configs are served to whatever version somebody already has installed, so the
 * fewer quotes they carry the fewer machines they can break on.
 *
 * Anything a shell would read as more than text still gets them, because being unquoted there is a
 * command that does something else entirely.
 */
function shellValue(value) {
  const text = String(value ?? '');

  return /^[A-Za-z0-9_.:/@=+-]+$/.test(text) && text.length > 0
    ? text
    : JSON.stringify(text);
}

/** Companion hostnames: the catalogue writes ${key}, and the container is named after it. */
function resolveNames(app, env) {
  const names = new Map((app.companions ?? []).map((c) => [c.key, containerName(app, c.key)]));

  return (env ?? []).map(([key, value]) => [
    key,
    String(value).replace(/\$\{([^}]+)\}/g, (whole, ref) => names.get(ref) ?? whole),
  ]);
}

const resolveEnv = (app) => resolveNames(app, app.env);

/**
 * When a companion counts as ready.
 *
 * Not by port: a companion has no published port - the application reaches it inside the container
 * network - so a port check would be looking at this machine's own 5432 and would either never
 * answer or, worse, answer from somebody else's database. What it says in its log is the real
 * signal, and every one of these images says it.
 */
function readyFor(companion) {
  const marks = {
    postgres: 'database system is ready to accept connections',
    mysql: 'ready for connections',
    mariadb: 'ready for connections',
    mongo: 'Waiting for connections',
    redis: 'Ready to accept connections',
  };

  const role = String(companion.role ?? '').toLowerCase();
  const image = String(companion.image ?? '').toLowerCase();

  for (const [name, mark] of Object.entries(marks))
    if (role.includes(name) || image.includes(name))
      return `{log: ${scalar(mark)}}`;

  if (companion.port) return `{port: ${companion.port}}`;

  // Nothing known to wait for. A moment is still better than nothing: with no readyWhen the task
  // counts as ready the instant docker was asked to start it, and the application then opens its
  // connection to something that is not listening yet.
  return '{delay: 5s}';
}

function configFor(app, target) {
  const port = app.port;
  const companions = app.companions ?? [];
  const lines = [];

  lines.push('# Generated by scripts/build-collection.mjs from AspireUI\'s container catalogue.');
  lines.push('# Edit by hand only if you also remove this file from configs/GENERATED.');
  lines.push('#');
  lines.push('# yaml-language-server: $schema=https://quickrun.org/quickrun.schema.json');
  lines.push('version: 1');
  lines.push(`name: ${scalar(app.label ?? app.id)}`);
  lines.push(`repository: ${scalar(`${target.owner}/${target.repo}`)}`);

  const description = `${app.description ?? app.label} Starts the official image (${app.image}); `
    + 'nothing is stored, so stopping the run leaves nothing behind.';
  lines.push(`description: ${block(description)}`);

  if (app.website) lines.push(`docs: ${scalar(app.website)}`);

  lines.push('');
  lines.push('requires:');
  lines.push('  - tool: docker');
  lines.push('    install: https://docs.docker.com/get-docker/');
  lines.push('');

  // One network per app, so companions can reach each other by name. continueOnError, because the
  // second run of the same app finds it already there.
  lines.push('setup:');
  lines.push(`  - run: docker network create ${containerName(app, 'net')}`);
  lines.push('    continueOnError: true');
  lines.push('');
  lines.push('tasks:');

  for (const companion of companions) {
    lines.push(`  - name: ${scalar(companion.key ?? companion.role ?? 'companion')}`);

    // The companion's own environment, which is where a database gets its user, password and
    // database name. Dropping it - which this did - starts a postgres that refuses to initialise
    // and an application that cannot connect to it, which is 29 of these configs.
    lines.push(`    run: ${dockerRun(app, {
      name: containerName(app, companion.key),
      image: companion.image,
      port: companion.port,
      env: resolveNames(app, companion.env),
    })}`);

    const ready = readyFor(companion);
    if (ready) lines.push(`    readyWhen: ${ready}`);
  }

  lines.push(`  - name: ${scalar(app.id)}`);
  lines.push(`    run: ${dockerRun(app, {
    name: containerName(app),
    image: app.image,
    port,
    env: resolveEnv(app),
  })}`);

  if (companions.length > 0)
    lines.push(`    dependsOn: [${companions.map((c) => scalar(c.key)).join(', ')}]`);

  if (port) {
    lines.push(`    readyWhen: {http: 'http://localhost:${port}'}`);
    lines.push('    open: true');
  }

  lines.push('');

  // Stopping has to end the containers: killing the docker client leaves them running, which is
  // exactly the leftover this project has already been bitten by once.
  lines.push('stop:');
  for (const companion of companions) {
    lines.push(`  - run: docker rm -f ${containerName(app, companion.key)}`);
    lines.push('    continueOnError: true');
  }
  lines.push(`  - run: docker rm -f ${containerName(app)}`);
  lines.push('    continueOnError: true');
  lines.push(`  - run: docker network rm ${containerName(app, 'net')}`);
  lines.push('    continueOnError: true');
  lines.push('');

  return lines.join('\n');
}

// ---- write ------------------------------------------------------------------------------------

const listing = join(outDir, 'GENERATED');
const previous = existsSync(listing)
  ? readFileSync(listing, 'utf8').split('\n').map((l) => l.trim()).filter(Boolean)
  : [];

// Only ever removes what it wrote before, so a hand-written config in the same tree survives.
for (const relative of previous) {
  const path = join(outDir, relative);
  if (existsSync(path)) rmSync(path);
}

/**
 * Configs already here that this script did not write, which it must not overwrite.
 *
 * The catalogue grows, and the day it learns about a repository somebody had already written a
 * config for by hand, generating over it would replace a tested config with a templated one - which
 * is exactly what happened to passbolt: the generated version publishes port 80, sets no address
 * and creates no first user, so it starts and cannot be logged into. Hand-written wins, and the run
 * says which ones it left alone.
 *
 * Computed after the removal above, so a previously generated file does not count as hand-written.
 */
const protectedFiles = existsSync(outDir)
  ? readdirSync(outDir, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .flatMap((owner) => readdirSync(join(outDir, owner.name))
        .filter((file) => file.endsWith('.yml'))
        .map((file) => `${owner.name}/${file}`))
  : [];

const written = [];
const skipped = [];

for (const app of presets) {
  const target = repoOf(app.github);

  if (!target) { skipped.push(`${app.id}: no usable github url (${app.github ?? 'none'})`); continue; }
  if (!app.image) { skipped.push(`${app.id}: no image`); continue; }

  const relative = `${target.owner}/${target.repo}.yml`;
  const path = join(outDir, relative);

  if (written.includes(relative)) { skipped.push(`${app.id}: ${relative} already written`); continue; }

  if (protectedFiles.includes(relative)) {
    skipped.push(`${app.id}: ${relative} is hand-written and was left alone`);
    continue;
  }

  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, configFor(app, target));
  written.push(relative);
}

writeFileSync(listing, written.sort().join('\n') + '\n');

// An index, so the site can show the collection without reading a hundred and sixty files. Written
// in the same pass, so it cannot describe something that is not there.
//
// The icon is the owner's GitHub avatar rather than the catalogue's artwork: that artwork belongs to
// the projects, and copying it into this repository is not ours to do. An avatar is public, usually
// is the project's own mark, and costs this repository nothing.
// Which of these repositories have since committed a quickrun.yml of their own.
//
// Asked here rather than in the browser: the page would need one request per card, and a hundred and
// sixty of those to load a list is not a trade worth making. A repository that commits one after this
// ran is caught when its card is opened, which checks again.
console.log('asking which repositories now ship their own config');

async function shipsOwn(repo) {
  for (const name of ['quickrun.yml', 'quickrun.yaml']) {
    try {
      const answer = await fetch(`https://raw.githubusercontent.com/${repo}/HEAD/${name}`,
        { method: 'HEAD' });
      if (answer.ok) return true;
    } catch { /* offline, or GitHub having a moment: treated as "not that we know of" */ }
  }
  return false;
}

/** In batches, so a hundred and sixty lookups do not arrive as a hundred and sixty at once. */
async function inBatches(items, size, work) {
  const results = [];

  for (let at = 0; at < items.length; at += size)
    results.push(...await Promise.all(items.slice(at, at + size).map(work)));

  return results;
}

/**
 * Configs somebody wrote by hand, which this script did not produce and must not touch.
 *
 * They were already served and already ran - the fallback chain fetches configs/<owner>/<repo>.yml
 * without asking who wrote it - but the collection page reads index.json, and that was built from
 * the generated list alone. So a hand-written config worked everywhere except the one place people
 * would find it.
 */
function handWritten(root, generated) {
  const found = [];

  for (const owner of readdirSync(root, { withFileTypes: true })) {
    if (!owner.isDirectory()) continue;

    for (const file of readdirSync(join(root, owner.name))) {
      if (!file.endsWith('.yml')) continue;

      const relative = `${owner.name}/${file}`;
      if (!generated.includes(relative)) found.push(relative);
    }
  }

  return found.sort();
}

/**
 * The few fields the card needs, out of a config's own text.
 *
 * Not a YAML parser: these are top-level scalars and one folded block, and the configs that reach
 * here are checked by CollectionTests with the real parser anyway. A field this cannot read comes
 * back null and the card falls back, which is the right failure for a card.
 */
function describe(text) {
  const scalar = (key) => {
    const match = text.match(new RegExp(`^${key}:[ 	]*(.+)$`, 'm'));
    if (!match) return null;

    const value = match[1].trim();
    if (value === '>-' || value === '>' || value === '|' || value === '|-') return null;

    return value.replace(/^['"]|['"]$/g, '');
  };

  // A folded block: the indented lines under the key, joined the way YAML folds them.
  const block = (key) => {
    const start = text.match(new RegExp(`^${key}:[ 	]*[>|]-?[ 	]*$`, 'm'));
    if (!start) return null;

    const after = text.slice(start.index + start[0].length).split('\n').slice(1);
    const lines = [];

    for (const line of after) {
      if (!/^\s+\S/.test(line)) break;
      lines.push(line.trim());
    }

    return lines.join(' ').trim() || null;
  };

  return {
    name: scalar('name'),
    description: block('description') ?? scalar('description'),
    docs: scalar('docs'),
  };
}

const extra = handWritten(outDir, written).map((relative) => {
  const [owner, file] = relative.split('/');
  const repo = file.replace(/\.yml$/, '');
  const read = describe(readFileSync(join(outDir, relative), 'utf8'));

  return {
    relative,
    repo: `${owner}/${repo}`,
    name: read.name || repo,
    description: read.description ?? '',
    docs: read.docs ?? `https://github.com/${owner}/${repo}`,
    // A hand-written config often asks for its port rather than fixing one, and a number the card
    // states has to be one the run will actually use.
    port: null,
    icon: `https://github.com/${owner}.png?size=80`,
    config: `configs/${relative}`,
  };
});

if (extra.length > 0)
  console.log(`  ${extra.length} hand-written config(s): ${extra.map((e) => e.repo).join(', ')}`);

const repos = [...written.map((relative) => relative.replace(/\.yml$/, '')),
  ...extra.map((e) => e.repo)];
const own = new Set();
const flags = await inBatches(repos, 8, async (repo) => [repo, await shipsOwn(repo)]);

for (const [repo, has] of flags) if (has) own.add(repo);

console.log(`  ${own.size} of ${repos.length} ship their own config`);

const index = written.map((relative) => {
  const [owner, file] = relative.split('/');
  const repo = file.replace(/\.yml$/, '');

  const app = presets.find((a) => {
    const target = repoOf(a.github);
    return target && `${target.owner}/${target.repo}.yml` === relative;
  });

  return {
    repo: `${owner}/${repo}`,
    name: app?.label ?? repo,
    description: String(app?.description ?? '').replace(/\s+/g, ' ').trim(),
    docs: app?.website ?? `https://github.com/${owner}/${repo}`,
    port: app?.port ?? null,
    icon: `https://github.com/${owner}.png?size=80`,
    config: `configs/${relative}`,
    // True when the repository has a quickrun.yml of its own, so the card can offer both: this
    // config, or the one the repository ships.
    shipsOwn: own.has(`${owner}/${repo}`),
  };
}).concat(extra.map((e) => ({
  repo: e.repo,
  name: e.name,
  description: e.description,
  docs: e.docs,
  port: e.port,
  icon: e.icon,
  config: e.config,
  shipsOwn: own.has(e.repo),
}))).sort((a, b) => a.name.localeCompare(b.name));

writeFileSync(join(outDir, 'index.json'), JSON.stringify(index, null, 2) + '\n');

console.log(`wrote ${written.length} configs into ${outDir}`);
console.log(`  the index lists ${index.length}, hand-written ones included`);
for (const note of skipped) console.log(`  skipped ${note}`);
