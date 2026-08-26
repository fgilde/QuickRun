<script setup>
import { computed, onMounted, ref } from 'vue';
import { useData, withBase } from 'vitepress';
import { PLATFORMS, RELEASE_BASE, detectOs } from './platforms.js';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));
const home = computed(() => (de.value ? '/de/' : '/'));
const link = (path) => withBase(`${home.value}${path}`);
const logos = withBase('/logos');

const mine = ref(null);
const version = ref('');
const copied = ref('');

const t = computed(() => (de.value
  ? {
      eyebrow: 'Download',
      title: 'Ein Binary, und es läuft',
      lead: 'Keine Runtime, kein Installer, kein Adminrecht. Entpacken und starten — oder den '
        + 'Paketmanager nehmen, dann kommen Updates von dort.',
      yours: 'Für dein System',
      recommended: 'Empfohlen',
      alternative: 'Alternative',
      direct: 'Direkter Download',
      others: 'Andere Plattformen',
      copy: 'Kopieren',
      copied: 'Kopiert',
      arch: { x64: 'Intel/AMD 64-bit', arm64: 'ARM64', app: 'App-Bundle' },
      extensionTitle: 'Browser-Erweiterung',
      extensionText: 'Sie setzt den Run-Button auf GitHub. Bis die Store-Freigaben durch sind, wird sie '
        + 'entpackt geladen — die Schritte stehen auf der Doku-Download-Seite.',
      extensionCta: 'Erweiterung und Store-Stand',
      verifyTitle: 'Prüfen und aktualisieren',
      verifyText: 'Jedes Release hat eine SHA256SUMS-Datei. Ist QuickRun installiert, aktualisiert es '
        + 'sich selbst — an derselben Stelle, aus derselben Quelle.',
      verifySums: 'SHA256SUMS ansehen',
      docsTitle: 'Alle Details',
      docsText: 'Paketmanager, App-Bundle für macOS, unsignierte Binaries, Deinstallation: die '
        + 'ausführliche Download-Seite in der Dokumentation.',
      docsCta: 'Zur Doku-Downloadseite',
      requirement: 'Voraussetzung',
      unsigned: 'Die Binaries sind nicht signiert. Windows zeigt deshalb einmal SmartScreen, macOS '
        + 'verlangt Rechtsklick → Öffnen; mit Homebrew oder winget entfällt das.',
    }
  : {
      eyebrow: 'Download',
      title: 'One binary, and it runs',
      lead: 'No runtime, no installer, no administrator rights. Unpack and start it - or use a package '
        + 'manager and let updates come from there.',
      yours: 'For your system',
      recommended: 'Recommended',
      alternative: 'Alternative',
      direct: 'Direct download',
      others: 'Other platforms',
      copy: 'Copy',
      copied: 'Copied',
      arch: { x64: 'Intel/AMD 64-bit', arm64: 'ARM64', app: 'App bundle' },
      extensionTitle: 'Browser extension',
      extensionText: 'It puts the Run button on GitHub. Until the store listings are approved it is '
        + 'loaded unpacked - the steps are on the documentation download page.',
      extensionCta: 'Extension and store status',
      verifyTitle: 'Verify and update',
      verifyText: 'Every release ships a SHA256SUMS file. Once installed, QuickRun updates itself - in '
        + 'the same place, from the same source.',
      verifySums: 'View SHA256SUMS',
      docsTitle: 'Every detail',
      docsText: 'Package managers, the macOS app bundle, unsigned binaries, uninstalling: the long '
        + 'download page in the documentation.',
      docsCta: 'Documentation download page',
      requirement: 'Requires',
      unsigned: 'The binaries are unsigned, so Windows shows SmartScreen once and macOS needs '
        + 'right-click → Open. Homebrew or winget avoid both.',
    }));

const detected = computed(() => PLATFORMS.find((p) => p.os === mine.value) ?? null);
const others = computed(() => PLATFORMS.filter((p) => p.os !== mine.value));

const browsers = [
  { name: 'Chrome', logo: 'googlechrome' },
  { name: 'Edge', logo: 'edge' },
  { name: 'Firefox', logo: 'firefoxbrowser' },
  { name: 'Opera', logo: 'opera' },
];

onMounted(async () => {
  mine.value = detectOs();

  // The tag is a nicety, not a dependency: no network, no version chip, everything else still works.
  try {
    const answer = await fetch('https://api.github.com/repos/fgilde/QuickRun/releases/latest');
    if (answer.ok) version.value = (await answer.json()).tag_name ?? '';
  } catch {
    version.value = '';
  }
});

async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
    copied.value = text;
    setTimeout(() => { copied.value = ''; }, 1600);
  } catch {
    // Clipboard denied: the command is on screen and selectable.
  }
}

function label(build) {
  return build.label ?? t.value.arch[build.arch] ?? build.arch;
}
</script>

<template>
  <div class="qr-get">
    <section class="qr-get-head">
      <div class="qr-get-glow" aria-hidden="true"></div>
      <div class="m3-wrap">
        <span class="m3-label">{{ t.eyebrow }}</span>
        <h1 class="m3-display qr-get-title">{{ t.title }}</h1>
        <p class="m3-body-lg qr-get-lead">{{ t.lead }}</p>
        <span v-if="version" class="m3-chip qr-get-version">{{ version }}</span>
      </div>
    </section>

    <section class="m3-wrap qr-get-body">
      <article v-if="detected" class="m3-card m3-card--elevated qr-mine" :style="{ '--tint': detected.tint }">
        <header>
          <img class="qr-os" :class="{ 'qr-os--mono': detected.mono }"
               :src="`${logos}/${detected.logo}.svg`" :alt="detected.name">
          <div>
            <span class="m3-label">{{ t.yours }}</span>
            <h2 class="m3-headline">{{ detected.name }}</h2>
            <p class="m3-body qr-req">{{ t.requirement }}: {{ detected.requirement }}</p>
          </div>
        </header>

        <div class="qr-command">
          <span class="m3-chip qr-tag">{{ t.recommended }}</span>
          <code class="m3-code">{{ detected.command }}</code>
          <button type="button" class="m3-button m3-button--tonal" @click="copy(detected.command)">
            {{ copied === detected.command ? t.copied : t.copy }}
          </button>
        </div>

        <div v-if="detected.alternative" class="qr-command qr-command--quiet">
          <span class="m3-chip qr-tag">{{ t.alternative }}</span>
          <code class="m3-code">{{ detected.alternative }}</code>
          <button type="button" class="m3-button m3-button--text" @click="copy(detected.alternative)">
            {{ copied === detected.alternative ? t.copied : t.copy }}
          </button>
        </div>

        <div class="qr-builds">
          <span class="m3-label">{{ t.direct }}</span>
          <div>
            <a v-for="build in detected.builds" :key="build.asset" class="m3-button m3-button--outlined"
               :href="`${RELEASE_BASE}/${build.asset}`">{{ label(build) }}</a>
          </div>
        </div>
      </article>

      <h2 class="m3-headline qr-others-title">{{ detected ? t.others : t.direct }}</h2>
      <div class="qr-others">
        <article v-for="platform in others" :key="platform.os" class="m3-card qr-other"
                 :style="{ '--tint': platform.tint }">
          <h3 class="m3-title">
            <img class="qr-os qr-os--small" :class="{ 'qr-os--mono': platform.mono }"
                 :src="`${logos}/${platform.logo}.svg`" alt="">
            {{ platform.name }}
          </h3>
          <p class="m3-body qr-req">{{ t.requirement }}: {{ platform.requirement }}</p>
          <code class="m3-code qr-other-command">{{ platform.command }}</code>
          <div class="qr-other-links">
            <a v-for="build in platform.builds" :key="build.asset"
               :href="`${RELEASE_BASE}/${build.asset}`">{{ label(build) }}</a>
          </div>
        </article>
      </div>

      <p class="m3-body qr-unsigned">{{ t.unsigned }}</p>

      <div class="qr-get-cards">
        <article class="m3-card qr-side">
          <h3 class="m3-title">{{ t.extensionTitle }}</h3>
          <p class="m3-body">{{ t.extensionText }}</p>
          <div class="qr-browsers">
            <span v-for="browser in browsers" :key="browser.name" class="m3-chip">
              <img :src="`${logos}/${browser.logo}.svg`" alt="" width="16" height="16">
              {{ browser.name }}
            </span>
          </div>
          <a class="m3-button m3-button--tonal" :href="link('download')">{{ t.extensionCta }}</a>
        </article>

        <article class="m3-card qr-side">
          <h3 class="m3-title">{{ t.verifyTitle }}</h3>
          <p class="m3-body">{{ t.verifyText }}</p>
          <code class="m3-code qr-other-command">quickrun update</code>
          <a class="m3-button m3-button--outlined" :href="`${RELEASE_BASE}/SHA256SUMS`">{{ t.verifySums }}</a>
        </article>

        <article class="m3-card qr-side">
          <h3 class="m3-title">{{ t.docsTitle }}</h3>
          <p class="m3-body">{{ t.docsText }}</p>
          <a class="m3-button m3-button--outlined" :href="link('download')">{{ t.docsCta }}</a>
        </article>
      </div>
    </section>
  </div>
</template>

<style scoped>
.qr-get-head { position: relative; padding: 64px 0 34px; }

.qr-get-glow {
  position: absolute;
  inset: -200px 0 auto 0;
  height: 480px;
  background:
    radial-gradient(40% 60% at 20% 24%, color-mix(in srgb, var(--m3-brand-lavender) 44%, transparent), transparent 70%),
    radial-gradient(38% 52% at 80% 8%, color-mix(in srgb, var(--m3-brand-periwinkle) 36%, transparent), transparent 70%);
  pointer-events: none;
}

.qr-get-head > .m3-wrap { position: relative; }
.qr-get-title { margin-top: 12px; }
.qr-get-lead { margin: 16px 0 0; max-width: 56ch; }
.qr-get-version { margin-top: 18px; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }

.qr-get-body { padding-bottom: 40px; }

.qr-mine { border-left: 5px solid var(--tint); }
.qr-mine header { display: flex; gap: 18px; align-items: flex-start; }
.qr-mine header h2 { margin: 4px 0 0; }
.qr-req { font-size: .9rem; margin-top: 6px; }

.qr-os { width: 44px; height: 44px; flex: 0 0 44px; object-fit: contain; }
.qr-os--small { width: 20px; height: 20px; flex: 0 0 20px; vertical-align: -4px; margin-right: 8px; }
html.dark .qr-os--mono { filter: invert(1) brightness(1.6); }

.qr-command {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
  margin-top: 20px;
  padding: 14px 16px;
  border-radius: var(--m3-radius-s);
  background: var(--m3-surface-container-high);
}

.qr-command code { flex: 1; min-width: 220px; overflow-wrap: anywhere; }
.qr-command--quiet { background: var(--m3-surface-container); }
.qr-tag { background: var(--m3-primary-container); color: var(--m3-on-primary-container); border-color: transparent; }

.qr-builds { margin-top: 20px; }
.qr-builds > div { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 8px; }

.qr-others-title { margin: 42px 0 18px; }

.qr-others {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 16px;
}

.qr-other { border-top: 4px solid var(--tint); }
.qr-other-command {
  display: block;
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: var(--m3-radius-xs);
  background: var(--m3-surface-container-high);
  overflow-wrap: anywhere;
}

.qr-other-links { display: flex; flex-wrap: wrap; gap: 14px; margin-top: 12px; }
.qr-other-links a { color: var(--m3-primary); font-size: .92rem; font-weight: 600; text-decoration: none; }
.qr-other-links a:hover { text-decoration: underline; }

.qr-unsigned { margin: 26px 0 0; max-width: 74ch; }

.qr-get-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 16px;
  margin-top: 42px;
}

.qr-side { display: grid; gap: 12px; align-content: start; justify-items: start; }
.qr-side p { margin: 0; }
.qr-browsers { display: flex; flex-wrap: wrap; gap: 8px; }
.qr-browsers img { display: block; }

@media (max-width: 620px) {
  .qr-mine header { flex-direction: column; gap: 12px; }
}
</style>
