<script setup>
/**
 * The collection: configs QuickRun keeps for repositories that ship none.
 *
 * Read from configs/index.json at load rather than baked in at build time, so adding a config is
 * adding a file - no page to touch, no list to keep in step with the directory.
 *
 * Every card's Run link goes to /run?repo=..., the same page a README badge lands on: it looks for
 * QuickRun on the reader's machine and carries on there, or offers the download. Nothing on this
 * page can start anything - it cannot reach a local listener, and that is deliberate.
 */
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { useData, withBase } from 'vitepress';
import ConfigView from './ConfigView.vue';
import { answering, hand } from './local.js';

const { lang } = useData();
const german = computed(() => lang.value.startsWith('de'));

const t = computed(() => (german.value
  ? {
      eyebrow: 'Sammlung',
      title: 'Configs, die QuickRun bereithält',
      lead: 'Für diese Projekte hält QuickRun eine Config bereit. Wer eines davon startet, bekommt '
        + 'sie automatisch — vor der Erkennung, aber niemals vor einer quickrun.yml, die das '
        + 'Repository selbst mitbringt. Von hier aus kannst du sie trotzdem ausdrücklich starten.',
      search: 'Suchen',
      searchLabel: 'Nach Name oder Repository filtern',
      run: 'Starten',
      config: 'Config ansehen',
      repo: 'Repository',
      count: (shown, all) => shown === all
        ? `${all} Configs`
        : `${shown} von ${all} Configs`,
      empty: 'Nichts gefunden.',
      loading: 'Sammlung wird geladen…',
      failed: 'Die Sammlung konnte nicht geladen werden.',
      note: 'Jede dieser Configs startet das offizielle Container-Image des Projekts und speichert '
        + 'nichts: Ein Lauf lässt nichts zurück. Die Befehle stehen vor dem Start im '
        + 'Bestätigungsfenster, und dort steht auch, dass die Config aus dieser Sammlung kommt.',
      contribute: 'Eine Config beitragen',
      close: 'Schließen',
      copy: 'Kopieren',
      copied: 'Kopiert',
      viewing: 'Config aus der QuickRun-Sammlung',
      onGitHub: 'Auf GitHub ansehen',
      runOurs: 'Mit dieser Config',
      runTheirs: 'Mit Repo-Config',
      shipsOwn: 'Dieses Repository bringt inzwischen eine eigene Config mit. Du kannst beides '
        + 'starten — automatisch würde die des Repositories gewinnen.',
      startingHere: 'QuickRun läuft — das Fenster öffnet sich mit dem Plan.',
      startingThere: 'QuickRun antwortet hier nicht — die Startseite erklärt den Rest.',
      configFailed: 'Diese Config konnte nicht geladen werden.',
    }
  : {
      eyebrow: 'Collection',
      title: 'Configs QuickRun keeps',
      lead: 'QuickRun keeps a config for each of these projects. Start one and it is used '
        + 'automatically - ahead of detection, and never ahead of a quickrun.yml the repository '
        + 'ships itself. From here you can still ask for it by name.',
      search: 'Search',
      searchLabel: 'Filter by name or repository',
      run: 'Run',
      config: 'View config',
      repo: 'Repository',
      count: (shown, all) => shown === all
        ? `${all} configs`
        : `${shown} of ${all} configs`,
      empty: 'Nothing matches.',
      loading: 'Loading the collection…',
      failed: 'The collection could not be loaded.',
      note: 'Each of these starts the project\'s official container image and stores nothing, so a '
        + 'run leaves nothing behind. The commands are shown before anything starts, and the '
        + 'confirmation window says the config came from this collection.',
      contribute: 'Contribute a config',
      close: 'Close',
      copy: 'Copy',
      copied: 'Copied',
      viewing: "A config from QuickRun's collection",
      onGitHub: 'View on GitHub',
      runOurs: 'With this config',
      runTheirs: "With the repository's",
      shipsOwn: 'This repository now ships a config of its own. Either can be started - left to '
        + "itself, the repository's would win.",
      startingHere: 'QuickRun is running - its window opens with the plan.',
      startingThere: 'QuickRun is not answering here - the start page explains the rest.',
      configFailed: 'This config could not be loaded.',
    }));

const entries = ref(null);
const failed = ref(false);
const query = ref('');

/** Whether a QuickRun on this machine is listening. Null while nobody has asked yet. */
const running = ref(null);

/** The card whose Run menu is open, by repository. Only ever one. */
const menuFor = ref(null);

/** The config being read, its text, and what went wrong if anything did. */
const viewing = ref(null);
const text = ref('');
const problem = ref('');

onMounted(async () => {
  try {
    const answer = await fetch(withBase('/configs/index.json'), { cache: 'no-cache' });
    if (!answer.ok) throw new Error(String(answer.status));
    entries.value = await answer.json();
  } catch {
    failed.value = true;
  }

  // Asked once, so a Run press goes straight to the window that is already open rather than through
  // a page explaining what QuickRun is.
  running.value = await answering();

  window.addEventListener('keydown', onKey);
  window.addEventListener('click', onClickAway, true);
});

onUnmounted(() => {
  window.removeEventListener('keydown', onKey);
  window.removeEventListener('click', onClickAway, true);
});

/** A press anywhere that is not this menu closes it. */
function onClickAway(event) {
  if (!menuFor.value) return;
  if (event.target.closest?.('.qr-collection-run')) return;

  menuFor.value = null;
}

function onKey(event) {
  if (event.key !== 'Escape') return;

  // The menu first: with a config open over the page, Escape means "close the thing on top".
  if (menuFor.value) { menuFor.value = null; return; }
  close();
}

/**
 * Run, or ask which config first.
 *
 * With only one config there is nothing to ask, so the press starts it. With two, a menu - rather
 * than two buttons of equal weight, which made the card look like a form to fill in.
 */
function pressRun(entry) {
  if (!entry.shipsOwn) { run(entry); return; }

  menuFor.value = menuFor.value === entry.repo ? null : entry.repo;
}

function choose(entry, fromCollection) {
  menuFor.value = null;
  run(entry, { fromCollection });
}

async function view(entry) {
  viewing.value = entry;
  text.value = '';
  problem.value = '';

  try {
    const answer = await fetch(withBase('/' + entry.config), { cache: 'no-cache' });
    if (!answer.ok) throw new Error(String(answer.status));
    text.value = await answer.text();
  } catch {
    problem.value = t.value.configFailed;
  }
}

function close() {
  viewing.value = null;
}

/**
 * Starts a repository from here.
 *
 * With QuickRun running this hands the repository to its own window, which prepares the plan and
 * asks - the same thing the button on GitHub ends up doing, without a page in between. With nothing
 * listening there is something to install first, and that is what /run is for.
 */
function run(entry, { fromCollection = false } = {}) {
  if (running.value) {
    // Its own window where this site is trusted - quickrun.org is, out of the box - and a tab on
    // QuickRun's page where it is not. Both end at the same plan, waiting for the same approval.
    hand(true, { repo: entry.repo, fromCollection });
    return;
  }

  // Nothing listening: the start page explains what to install. The source travels with it, so the
  // press is not forgotten on the way.
  location.href = runLink(entry.repo) + (fromCollection ? '&executeQuickRun=true&config=collection' : '');
}

const shown = computed(() => {
  const all = entries.value ?? [];
  const needle = query.value.trim().toLowerCase();

  if (needle.length === 0) return all;

  return all.filter((entry) =>
    entry.name.toLowerCase().includes(needle)
    || entry.repo.toLowerCase().includes(needle)
    || (entry.description ?? '').toLowerCase().includes(needle));
});

const runLink = (repo) => withBase(`/${german.value ? 'de/' : ''}run?repo=${encodeURIComponent(repo)}`);
const configLink = (entry) => withBase('/' + entry.config);
</script>

<template>
  <div class="qr-collection">
    <span class="m3-label">{{ t.eyebrow }}</span>
    <h1 class="m3-display qr-collection-title">{{ t.title }}</h1>
    <p class="m3-body-lg qr-collection-lead">{{ t.lead }}</p>

    <div class="qr-collection-bar">
      <label class="qr-collection-search">
        <span class="m3-label">{{ t.search }}</span>
        <input v-model="query" type="search" :aria-label="t.searchLabel" spellcheck="false">
      </label>
      <span v-if="entries" class="m3-label qr-collection-count">
        {{ t.count(shown.length, entries.length) }}
      </span>
    </div>

    <p v-if="failed" class="m3-body">{{ t.failed }}</p>
    <p v-else-if="!entries" class="m3-body">{{ t.loading }}</p>
    <p v-else-if="shown.length === 0" class="m3-body">{{ t.empty }}</p>

    <div v-if="entries" class="qr-collection-grid">
      <article v-for="entry in shown" :key="entry.repo" class="m3-card qr-collection-card">
        <div class="qr-collection-head">
          <!-- The owner's avatar, loaded from GitHub: the project's own mark, and no artwork of
               anyone else's copied into this repository. -->
          <img :src="entry.icon" :alt="''" width="40" height="40" loading="lazy" decoding="async">
          <div class="qr-collection-name">
            <strong>{{ entry.name }}</strong>
            <span class="qr-collection-repo">{{ entry.repo }}</span>
          </div>

          <!-- The repository itself, in its own tab. Top right of the card, where a link off the
               page belongs - the buttons at the bottom are the ones that do something here. -->
          <a class="qr-collection-gh" :href="`https://github.com/${entry.repo}`"
             target="_blank" rel="noreferrer" :title="t.onGitHub" :aria-label="t.onGitHub">
            <svg viewBox="0 0 16 16" width="18" height="18" aria-hidden="true">
              <path fill="currentColor" d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.05-.13-.36-.95.08-1.98 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.03.13 1.85.08 1.98.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.94-.01 2.2 0 .21.15.46.55.38A7.995 7.995 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/>
            </svg>
          </a>
        </div>

        <p v-if="entry.description" class="qr-collection-text">{{ entry.description }}</p>

        <!-- A repository that has since committed its own config: both are offered, because the
             card shows ours and pressing Run has to be the thing the reader expects. -->
        <p v-if="entry.shipsOwn" class="qr-collection-both">{{ t.shipsOwn }}</p>

        <div class="qr-collection-actions">
          <!-- One Run, and where there are two configs it asks which. A button rather than a link:
               with QuickRun running this hands the repository straight to its window, and only
               otherwise falls back to the page that explains it. -->
          <div class="qr-collection-run">
            <button class="m3-button" type="button"
                    :aria-expanded="menuFor === entry.repo ? 'true' : 'false'"
                    :aria-haspopup="entry.shipsOwn ? 'menu' : undefined"
                    @click="pressRun(entry)">
              {{ t.run }}<span v-if="entry.shipsOwn" class="qr-collection-caret" aria-hidden="true">▾</span>
            </button>

            <div v-if="menuFor === entry.repo" class="qr-collection-menu" role="menu">
              <button type="button" role="menuitem" @click="choose(entry, true)">
                {{ t.runOurs }}
              </button>
              <button type="button" role="menuitem" @click="choose(entry, false)">
                {{ t.runTheirs }}
              </button>
            </div>
          </div>

          <button class="m3-button m3-button--text" type="button" @click="view(entry)">
            {{ t.config }}
          </button>
          <span v-if="entry.port" class="m3-label qr-collection-port">:{{ entry.port }}</span>
        </div>
      </article>
    </div>

    <!-- The config, read before anything is started with it. Closed with Escape, the backdrop, or
         the button - and the Run inside it does exactly what the card's does. -->
    <div v-if="viewing" class="qr-modal" @click.self="close">
      <div class="qr-modal-panel" role="dialog" aria-modal="true">
        <div class="qr-modal-head">
          <img :src="viewing.icon" :alt="''" width="32" height="32">
          <div class="qr-modal-name">
            <strong>{{ viewing.name }}</strong>
            <span class="qr-modal-repo">{{ viewing.repo }}</span>
          </div>
          <button class="m3-button m3-button--text" type="button" @click="close">{{ t.close }}</button>
        </div>

        <p v-if="problem" class="m3-body">{{ problem }}</p>

        <ConfigView v-else-if="text" :yaml="text" :source="t.viewing"
                    :run-label="viewing.shipsOwn ? t.runOurs : t.run"
                    :copy-label="t.copy" :copied-label="t.copied"
                    @run="run(viewing, { fromCollection: viewing.shipsOwn })" />

        <p v-if="viewing.shipsOwn" class="m3-body qr-modal-note">{{ t.shipsOwn }}</p>

        <p v-else class="m3-body">{{ t.loading }}</p>

        <p class="m3-body qr-modal-note">
          {{ running === null ? '' : running ? t.startingHere : t.startingThere }}
        </p>
      </div>
    </div>

    <p class="m3-body qr-collection-note">{{ t.note }}</p>

    <p>
      <a class="m3-button m3-button--outlined"
         href="https://github.com/fgilde/QuickRun/tree/main/configs"
         target="_blank" rel="noreferrer">{{ t.contribute }}</a>
    </p>
  </div>
</template>

<style scoped>
.qr-collection { max-width: 1180px; margin: 0 auto; padding: 40px 22px 80px; }
.qr-collection-title { margin: 8px 0 0; }
.qr-collection-lead { margin: 14px 0 0; max-width: 76ch; }

.qr-collection-bar {
  display: flex; align-items: flex-end; gap: 16px; flex-wrap: wrap; margin: 26px 0 18px;
}
.qr-collection-search { display: grid; gap: 4px; flex: 1; min-width: 240px; max-width: 420px; }
.qr-collection-search input {
  padding: 9px 12px; border-radius: 8px;
  border: 1px solid var(--vp-c-divider); background: var(--vp-c-bg);
  color: inherit; font: inherit;
}
.qr-collection-count { opacity: 0.75; }

.qr-collection-grid {
  display: grid; gap: 14px;
  grid-template-columns: repeat(auto-fill, minmax(290px, 1fr));
}
.qr-collection-card { display: flex; flex-direction: column; gap: 10px; padding: 16px; }

.qr-collection-head { display: flex; align-items: center; gap: 12px; }
.qr-collection-head img { border-radius: 8px; flex: none; }
.qr-collection-name { display: grid; min-width: 0; }
.qr-collection-repo { font-size: 12px; opacity: 0.75; overflow-wrap: anywhere; }

.qr-collection-gh {
  /* Pushed right rather than trailing the name: with a short name the icon otherwise sits in the
     middle of the card, which reads as part of the title. */
  flex: none; margin-left: auto; align-self: flex-start; display: inline-flex;
  padding: 4px; border-radius: 6px; color: inherit; opacity: 0.55;
  transition: opacity .15s, background-color .15s;
}
.qr-collection-gh:hover { opacity: 1; background: rgba(127, 127, 127, 0.14); }

.qr-collection-text {
  margin: 0; font-size: 13.5px; opacity: 0.85; flex: 1;
  display: -webkit-box; -webkit-line-clamp: 4; -webkit-box-orient: vertical; overflow: hidden;
}

.qr-collection-both {
  margin: 0; padding: 8px 10px; border-radius: 8px;
  font-size: 12.5px; line-height: 1.45;
  border-left: 3px solid var(--vp-c-warning-1, #d29922);
  background: rgba(210, 153, 34, 0.09);
}

/* Run, with the choice of config hanging under it where there is a choice to make. */
.qr-collection-run { position: relative; display: inline-flex; }
.qr-collection-caret { margin-left: 7px; font-size: 13px; line-height: 1; opacity: 0.85; }

.qr-collection-menu {
  position: absolute; top: calc(100% + 6px); left: 0; z-index: 20;
  display: grid; min-width: 230px; padding: 6px;
  border-radius: 10px;
  /* Lifted off the page rather than blending into it: on the dark theme the panel colour and the
     page colour are close enough that a menu looked like text floating over the cards. */
  border: 1px solid var(--vp-c-brand-soft, var(--vp-c-divider));
  background: var(--vp-c-bg-soft, var(--vp-c-bg));
  box-shadow: 0 18px 44px rgba(0, 0, 0, 0.45);
}
.qr-collection-menu button {
  padding: 8px 10px; border: 0; border-radius: 7px;
  background: none; color: inherit; font: inherit; text-align: left; cursor: pointer;
}
.qr-collection-menu button:hover { background: rgba(127, 127, 127, 0.14); }

.qr-collection-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.qr-collection-port { opacity: 0.6; margin-left: auto; }

.qr-collection-note { margin: 30px 0 18px; max-width: 78ch; opacity: 0.85; }

/* The config, over the page. Fixed so a long list does not scroll it away, and the panel itself is
   what scrolls when a config is long. */
.qr-modal {
  position: fixed; inset: 0; z-index: 60;
  display: flex; align-items: center; justify-content: center; padding: 24px;
  background: rgba(0, 0, 0, 0.55);
}
.qr-modal-panel {
  display: flex; flex-direction: column; gap: 12px; min-height: 0;
  width: min(920px, 100%); max-height: min(80vh, 900px);
  padding: 18px; border-radius: 14px;
  background: var(--vp-c-bg, #fff); border: 1px solid var(--vp-c-divider);
  box-shadow: 0 24px 60px rgba(0, 0, 0, 0.35);
}
.qr-modal-head { display: flex; align-items: center; gap: 12px; }
.qr-modal-head img { border-radius: 8px; flex: none; }
.qr-modal-name { display: grid; flex: 1; min-width: 0; }
.qr-modal-repo { font-size: 12px; opacity: 0.7; overflow-wrap: anywhere; }
.qr-modal-note { margin: 0; font-size: 12.5px; opacity: 0.7; }

@media (max-width: 640px) {
  .qr-modal { padding: 10px; }
  .qr-modal-panel { max-height: 92vh; }
}
</style>
