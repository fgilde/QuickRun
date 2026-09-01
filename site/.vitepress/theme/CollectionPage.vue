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
import { computed, onMounted, ref } from 'vue';
import { useData, withBase } from 'vitepress';

const { lang } = useData();
const german = computed(() => lang.value.startsWith('de'));

const t = computed(() => (german.value
  ? {
      eyebrow: 'Sammlung',
      title: 'Configs für Repositories, die keine mitbringen',
      lead: 'Für diese Projekte hält QuickRun eine Config bereit. Wer eines davon startet, bekommt sie '
        + 'automatisch — vor der Erkennung, aber niemals vor einer quickrun.yml, die das Repository '
        + 'selbst mitbringt.',
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
    }
  : {
      eyebrow: 'Collection',
      title: 'Configs for repositories that ship none',
      lead: 'QuickRun keeps a config for each of these projects. Start one and it is used '
        + 'automatically - ahead of detection, and never ahead of a quickrun.yml the repository '
        + 'ships itself.',
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
    }));

const entries = ref(null);
const failed = ref(false);
const query = ref('');

onMounted(async () => {
  try {
    const answer = await fetch(withBase('/configs/index.json'), { cache: 'no-cache' });
    if (!answer.ok) throw new Error(String(answer.status));
    entries.value = await answer.json();
  } catch {
    failed.value = true;
  }
});

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
            <a class="qr-collection-repo" :href="`https://github.com/${entry.repo}`"
               target="_blank" rel="noreferrer">{{ entry.repo }}</a>
          </div>
        </div>

        <p v-if="entry.description" class="qr-collection-text">{{ entry.description }}</p>

        <div class="qr-collection-actions">
          <a class="m3-button" :href="runLink(entry.repo)">{{ t.run }}</a>
          <a class="m3-button m3-button--text" :href="configLink(entry)"
             target="_blank" rel="noreferrer">{{ t.config }}</a>
          <span v-if="entry.port" class="m3-label qr-collection-port">:{{ entry.port }}</span>
        </div>
      </article>
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

.qr-collection-text {
  margin: 0; font-size: 13.5px; opacity: 0.85; flex: 1;
  display: -webkit-box; -webkit-line-clamp: 4; -webkit-box-orient: vertical; overflow: hidden;
}

.qr-collection-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.qr-collection-port { opacity: 0.6; margin-left: auto; }

.qr-collection-note { margin: 30px 0 18px; max-width: 78ch; opacity: 0.85; }
</style>
