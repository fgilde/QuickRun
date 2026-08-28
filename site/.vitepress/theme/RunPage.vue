<script setup>
import { computed, onMounted, ref } from 'vue';
import { useData, withBase } from 'vitepress';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));

// What the badge in a README carries: a repository, and optionally which ref or pull request.
const repo = ref('');
const reference = ref('');
const pr = ref('');

// null while asking, true when QuickRun answered on loopback, false when it did not.
const running = ref(null);
const pressed = ref(false);

const DEFAULT_PORT = 9876;

const carried = computed(() => {
  const query = new URLSearchParams({ repo: repo.value });
  if (reference.value) query.set('ref', reference.value);
  if (pr.value) query.set('pr', pr.value);
  return query.toString();
});

/**
 * Where the Run button goes.
 *
 * QuickRun answering means its own page is right there, so that is where this goes: a plain http
 * address that every browser opens, with no URL scheme, no handler registration and no permission
 * dialog in the way. quickrun:// is only for the other case - nothing answered, so there is a
 * daemon to start before there is a page to open - and it is exactly the case where a browser
 * silently ignores the click when the scheme has no handler, which is why it is not the first
 * choice any more.
 */
const target = computed(() => (running.value
  ? `http://127.0.0.1:${DEFAULT_PORT}/#run?${carried.value}`
  : `quickrun://run?${carried.value}`));

/** The snippet a repository owner pastes, for whatever is in the fields above. */
const snippet = computed(() => {
  const page = `https://quickrun.org/${de.value ? 'de/' : ''}run?repo=${encodeURIComponent(repo.value || 'owner/repo')}`;
  return `[![QuickRun](https://quickrun.org/badge.svg)](${page})`;
});

const t = computed(() => (de.value
  ? {
      title: 'Dieses Repository starten',
      asking: 'Suche QuickRun auf diesem Rechner…',
      found: 'QuickRun läuft auf diesem Rechner.',
      missing: 'QuickRun antwortet hier nicht — installiert und nicht gestartet sieht von einer '
        + 'Webseite genauso aus wie gar nicht installiert. Der Versuch klärt es.',
      run: 'In QuickRun öffnen',
      runNote: 'Es öffnet sich das QuickRun-Fenster mit dem Plan. Gestartet wird erst, wenn du dort '
        + 'bestätigst — diese Seite kann nichts auf deinem Rechner ausführen.',
      tryNote: 'Öffnen startet QuickRun, wenn es installiert ist. Passiert nichts, führt der '
        + 'Download-Button zur passenden Version.',
      download: 'QuickRun herunterladen',
      nothing: 'Kein Repository angegeben. Diese Seite wird von einem Badge in einer README '
        + 'aufgerufen und bekommt das Repository von dort.',
      badgeTitle: 'Badge für dein README',
      badgeText: 'Trag dein Repository ein und kopiere die Zeile in dein README. Wer darauf klickt, '
        + 'landet hier — mit QuickRun geht es direkt weiter, ohne führt der Weg zum Download.',
      repoLabel: 'Repository',
      copy: 'Kopieren',
      copied: 'Kopiert',
    }
  : {
      title: 'Run this repository',
      asking: 'Looking for QuickRun on this machine…',
      found: 'QuickRun is running on this machine.',
      missing: 'QuickRun is not answering here - installed but not running looks the same from a '
        + 'web page as not installed at all. Opening it settles that.',
      run: 'Open in QuickRun',
      runNote: 'The QuickRun window opens with the plan. Nothing starts until you confirm it there '
        + '- this page cannot run anything on your machine.',
      tryNote: 'Opening starts QuickRun when it is installed. If nothing happens, the download '
        + 'button has the version for your machine.',
      download: 'Download QuickRun',
      nothing: 'No repository given. This page is opened by a badge in a README, which is where the '
        + 'repository comes from.',
      badgeTitle: 'A badge for your README',
      badgeText: 'Put your repository in and copy the line into your README. Anyone who clicks it '
        + 'lands here - with QuickRun installed they carry straight on, without it they get the '
        + 'download.',
      repoLabel: 'Repository',
      copy: 'Copy',
      copied: 'Copied',
    }));

onMounted(async () => {
  const query = new URLSearchParams(location.search);
  repo.value = query.get('repo') ?? '';
  reference.value = query.get('ref') ?? '';
  pr.value = query.get('pr') ?? '';

  running.value = await isRunning();
});

/**
 * Whether QuickRun answers on this machine.
 *
 * http from an https page is allowed for loopback, which browsers treat as trustworthy. The answer
 * says only that QuickRun exists - every endpoint that could start or read a run refuses a page.
 *
 * Two attempts, because a QuickRun older than this page allows only extension origins to read the
 * answer: the first attempt reads it, and the second only asks whether anything answered at all.
 * An opaque response tells us nothing except that something is listening there, which is exactly
 * the question.
 */
async function isRunning() {
  const url = `http://127.0.0.1:${DEFAULT_PORT}/api/ping`;

  try {
    return (await fetch(url, { cache: 'no-store' })).ok;
  } catch {
    // Not readable. Either nothing is there, or it is a version that does not let a page read it.
  }

  try {
    await fetch(url, { mode: 'no-cors', cache: 'no-store' });
    return true;
  } catch {
    return false;
  }
}

function open() {
  pressed.value = true;

  // A new tab when QuickRun answered, because that is an ordinary page and this one should stay
  // where it is; the same tab for quickrun://, where a blank tab would be all that is left if no
  // handler picks it up.
  if (running.value) window.open(target.value, '_blank', 'noopener');
  else location.href = target.value;
}

const copyLabel = ref('');

async function copy() {
  try {
    await navigator.clipboard.writeText(snippet.value);
    copyLabel.value = t.value.copied;
  } catch {
    copyLabel.value = '';
  }

  setTimeout(() => { copyLabel.value = ''; }, 1500);
}
</script>

<template>
  <div class="qr-run m3">
    <section class="m3-wrap qr-run-card">
      <template v-if="repo">
        <span class="m3-label">QuickRun</span>
        <h1 class="m3-headline">{{ t.title }}</h1>
        <p class="qr-run-repo m3-code">{{ repo }}<span v-if="reference"> · {{ reference }}</span><span v-if="pr"> · PR #{{ pr }}</span></p>

        <p class="qr-run-state" :class="running === false ? 'is-missing' : running ? 'is-found' : ''">
          {{ running === null ? t.asking : running ? t.found : t.missing }}
        </p>

        <div class="qr-run-actions">
          <!-- Opening is always the first offer, whatever the ping said. It costs nothing when
               QuickRun is not there, and it starts it when it is installed but not running. -->
          <button class="m3-button" type="button" @click="open">{{ t.run }}</button>
          <a v-if="running === false" class="m3-button m3-button--outlined"
             :href="withBase(de ? '/de/get' : '/get')">{{ t.download }}</a>
        </div>

        <p class="m3-body qr-run-note">{{ running === false && !pressed ? t.tryNote : t.runNote }}</p>
      </template>

      <template v-else>
        <span class="m3-label">QuickRun</span>
        <h1 class="m3-headline">{{ t.title }}</h1>
        <p class="m3-body">{{ t.nothing }}</p>
      </template>
    </section>

    <section class="m3-wrap qr-run-card">
      <h2 class="m3-title">{{ t.badgeTitle }}</h2>
      <p class="m3-body">{{ t.badgeText }}</p>

      <p>
        <label class="qr-run-field">
          <span class="m3-label">{{ t.repoLabel }}</span>
          <input v-model="repo" type="text" placeholder="owner/repo" spellcheck="false">
        </label>
      </p>

      <p><img :src="withBase('/badge.svg')" alt="QuickRun: Run this" width="150" height="20"></p>

      <pre class="qr-run-snippet"><code>{{ snippet }}</code></pre>

      <p>
        <button class="m3-button m3-button--outlined" type="button" @click="copy">
          {{ copyLabel || t.copy }}
        </button>
      </p>
    </section>
  </div>
</template>

<style scoped>
.qr-run {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 48px 0 80px;
  background: var(--m3-surface);
  color: var(--m3-on-surface);
  min-height: 70vh;
}

.qr-run-card {
  padding: 28px 24px;
  border: 1px solid var(--m3-outline-variant);
  border-radius: var(--m3-radius-l);
  background: var(--m3-surface-container-low);
}

.qr-run-repo {
  display: inline-block;
  margin: 14px 0 18px;
  padding: 8px 14px;
  border-radius: var(--m3-radius-s);
  background: var(--m3-surface-container);
  font-size: 15px;
}

.qr-run-state {
  margin: 0 0 18px;
  font-size: 14px;
  color: var(--m3-on-surface-variant);
}

.qr-run-state.is-found { color: #2f7a5c; }
.qr-run-state.is-missing { color: #bd6b1a; }

.qr-run-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 16px;
}

.qr-run-note { max-width: 62ch; color: var(--m3-on-surface-variant); }

.qr-run-field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-width: 30rem;
}

.qr-run-field input {
  padding: 9px 12px;
  border: 1px solid var(--m3-outline-variant);
  border-radius: var(--m3-radius-s);
  background: var(--m3-surface);
  color: inherit;
  font: inherit;
}

.qr-run-snippet {
  margin: 16px 0;
  padding: 14px 16px;
  overflow-x: auto;
  border-radius: var(--m3-radius-m);
  background: var(--m3-surface-container);
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 13px;
}
</style>
