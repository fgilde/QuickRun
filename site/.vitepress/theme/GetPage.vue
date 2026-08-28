<script setup>
import { computed, onMounted, ref } from 'vue';
import { useData, withBase } from 'vitepress';
import { PLATFORMS, RELEASE_BASE, detectOs } from './platforms.js';
import { currentBrowser, listingFor } from './stores.js';

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
      extensionText: 'Sie setzt den Run-Button auf GitHub. Erforderlich ist sie nicht — QuickRun '
        + 'läuft auch allein — aber sie ist der Grund, aus dem es existiert.',
      extensionCta: 'Wie die Erweiterung funktioniert',
      extensionInstall: 'Für {browser} installieren',
      extensionYours: 'Dein Browser',
      extensionPending: 'Store-Prüfung läuft',
      extensionListed: 'im Store',
      extensionViaChrome: 'über den Chrome Web Store',
      extensionGet: 'Installieren',
      extensionZip: 'Build laden',
      extensionByHandTitle: 'Von Hand laden, wo es den Store noch nicht gibt',
      extensionByHandChromium: 'Chrome, Edge, Opera: Build entzippen, dann '
        + 'chrome://extensions → Entwicklermodus → Entpackte Erweiterung laden → der Ordner.',
      extensionByHandFirefox: 'Firefox: about:debugging → Dieser Firefox → Temporäres Add-on laden '
        + '→ die manifest.json im Ordner.',
      extensionNothingToPair: 'Mehr ist nicht zu tun: es gibt nichts zu koppeln. QuickRun nimmt nur '
        + 'Anfragen von einer Browser-Erweiterung an.',
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
        + 'fragt vor dem ersten Start nach; über Homebrew oder winget entfällt beides.',
      macGateTitle: 'macOS: heruntergeladen und geöffnet',
      macGateText: 'Der Browser setzt das Quarantäne-Flag, und weil QuickRun keine Apple-Signatur '
        + 'hat, nennt macOS die App dann beschädigt. Einmal entfernen, danach startet sie normal:',
      macGateBrew: 'Über Homebrew passiert das automatisch:',
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
      extensionText: 'It puts the Run button on GitHub. It is not required - QuickRun works on its '
        + 'own - but it is the reason it exists.',
      extensionCta: 'How the extension works',
      extensionInstall: 'Install for {browser}',
      extensionYours: 'Your browser',
      extensionPending: 'store review pending',
      extensionListed: 'in the store',
      extensionViaChrome: 'from the Chrome Web Store',
      extensionGet: 'Install',
      extensionZip: 'Download the build',
      extensionByHandTitle: 'Loading it by hand, where there is no store listing yet',
      extensionByHandChromium: 'Chrome, Edge, Opera: unzip the build, then '
        + 'chrome://extensions → Developer mode → Load unpacked → the folder.',
      extensionByHandFirefox: 'Firefox: about:debugging → This Firefox → Load Temporary Add-on → '
        + 'the manifest.json inside the folder.',
      extensionNothingToPair: 'That is all there is to it: there is nothing to pair. QuickRun accepts '
        + 'requests only from a browser extension.',
      verifyTitle: 'Verify and update',
      verifyText: 'Every release ships a SHA256SUMS file. Once installed, QuickRun updates itself - in '
        + 'the same place, from the same source.',
      verifySums: 'View SHA256SUMS',
      docsTitle: 'Every detail',
      docsText: 'Package managers, the macOS app bundle, unsigned binaries, uninstalling: the long '
        + 'download page in the documentation.',
      docsCta: 'Documentation download page',
      requirement: 'Requires',
      unsigned: 'The binaries are unsigned, so Windows shows SmartScreen once and macOS asks before '
        + 'the first launch. Homebrew or winget avoid both.',
      macGateTitle: 'macOS: downloaded, then opened',
      macGateText: 'The browser sets the quarantine flag, and with no Apple signature to check '
        + 'against, macOS calls the app damaged. Clear it once and it opens normally:',
      macGateBrew: 'Homebrew does this for you:',
    }));

const detected = computed(() => PLATFORMS.find((p) => p.os === mine.value) ?? null);
const others = computed(() => PLATFORMS.filter((p) => p.os !== mine.value));

// `store` is the name the listing table knows it by, `logo` the icon file, `asset` the build to load
// by hand where a store has nothing yet. Chromium browsers share one build.
const browsers = computed(() => [
  { name: 'Chrome', logo: 'chrome', store: 'chrome', asset: 'quickrun-extension-chromium.zip' },
  { name: 'Edge', logo: 'edge', store: 'edge', asset: 'quickrun-extension-chromium.zip' },
  { name: 'Firefox', logo: 'firefox', store: 'firefox', asset: 'quickrun-extension-firefox.zip' },
  { name: 'Opera', logo: 'opera', store: 'opera', asset: 'quickrun-extension-chromium.zip' },
].map((browser) => ({
  ...browser,
  listing: listingFor(browser.store),
  // Opera installs from Chrome's listing, which is worth saying rather than leaving as a surprise.
  note: browser.store === 'opera' && listingFor('opera') ? t.value.extensionViaChrome : null,
})));

// Which browser is reading this - `mine` is already the operating system. Read after mounting: this
// page is built to static HTML, and a build machine has no browser to ask.
const myBrowser = ref(null);

const myEntry = computed(() => browsers.value.find((browser) => browser.store === myBrowser.value) ?? null);
const myName = computed(() => myEntry.value?.name ?? '');
const myListing = computed(() => (myBrowser.value ? listingFor(myBrowser.value) : null));

onMounted(async () => {
  mine.value = detectOs();
  myBrowser.value = currentBrowser();

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

      <!-- The one thing a Mac actually stops on, with the command that ends it. It was a line on
           the documentation download page, which is no help to somebody staring at "QuickRun.app is
           damaged" here. Shown to everyone rather than only to a detected Mac: people download for
           the machine they are not sitting at. -->
      <details class="qr-macgate" :open="mine === 'macos'">
        <summary class="m3-label">{{ t.macGateTitle }}</summary>
        <p class="m3-body">{{ t.macGateText }}</p>
        <pre class="qr-macgate-code"><code>xattr -dr com.apple.quarantine /Applications/QuickRun.app</code></pre>
        <p class="m3-body">{{ t.macGateBrew }}</p>
        <pre class="qr-macgate-code"><code>brew install --cask fgilde/tap/quickrun</code></pre>
      </details>

      <!-- Everything about the extension, here: the store for the browser reading this, a row per
           browser with what it can do today, and the two steps for loading it by hand. Sending
           somebody to another page to install a thing they are looking at is not a download page. -->
      <h2 class="m3-headline qr-others-title">{{ t.extensionTitle }}</h2>
      <p class="m3-body qr-extension-intro">{{ t.extensionText }}</p>

      <a
        v-if="myListing"
        class="m3-button qr-extension-mine"
        :href="myListing"
        target="_blank"
        rel="noreferrer"
      >
        <img :src="`${logos}/${myEntry.logo}.svg`" alt="" width="20" height="20">
        <span>
          <small>{{ t.extensionYours }}</small>
          <strong>{{ t.extensionInstall.replace('{browser}', myName) }}</strong>
        </span>
      </a>

      <div class="qr-extension-rows">
        <article
          v-for="browser in browsers"
          :key="browser.name"
          class="m3-card qr-extension-row"
          :class="{ 'qr-extension-row--yours': browser.store === myBrowser }"
        >
          <img :src="`${logos}/${browser.logo}.svg`" alt="" width="22" height="22">
          <span class="qr-extension-name">{{ browser.name }}</span>

          <span class="m3-label qr-extension-state">
            {{ browser.note ?? (browser.listing ? t.extensionListed : t.extensionPending) }}
          </span>

          <a
            v-if="browser.listing"
            class="m3-button"
            :href="browser.listing"
            target="_blank"
            rel="noreferrer"
          >{{ t.extensionGet }}</a>

          <a class="m3-button m3-button--outlined" :href="`${RELEASE_BASE}/${browser.asset}`">
            {{ t.extensionZip }}
          </a>
        </article>
      </div>

      <details class="qr-extension-hand">
        <summary class="m3-body">{{ t.extensionByHandTitle }}</summary>
        <p class="m3-body">{{ t.extensionByHandChromium }}</p>
        <p class="m3-body">{{ t.extensionByHandFirefox }}</p>
        <p class="m3-body">{{ t.extensionNothingToPair }}</p>
        <a class="m3-button m3-button--text" :href="link('extension')">{{ t.extensionCta }}</a>
      </details>

      <div class="qr-get-cards">
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
.qr-extension-intro { margin: 0 0 18px; max-width: 74ch; }

/* The store for the browser reading this - one button, above the row for every browser. */
.qr-extension-mine {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 18px;
  text-align: left;
}

.qr-extension-mine small { display: block; font-size: 11.5px; opacity: 0.75; }
.qr-extension-mine strong { font-size: 15px; }

.qr-extension-rows { display: grid; gap: 10px; }

.qr-extension-row {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 16px;
  flex-wrap: wrap;
}

/* The one in use, so the button above and the row below agree. */
.qr-extension-row--yours { outline: 2px solid var(--m3-primary); outline-offset: -1px; }

.qr-extension-name { font-weight: 600; min-width: 74px; }
.qr-extension-state { flex: 1; opacity: 0.75; }

.qr-extension-hand { margin: 18px 0 0; max-width: 78ch; }
.qr-extension-hand summary { cursor: pointer; }
.qr-extension-hand p { margin: 10px 0 0; }

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

.qr-macgate { margin: 14px 0 0; max-width: 78ch; }
.qr-macgate summary { cursor: pointer; }
.qr-macgate p { margin: 10px 0 0; }
.qr-macgate-code {
  margin: 8px 0 0;
  padding: 10px 12px;
  overflow-x: auto;
  border-radius: 8px;
  background: var(--m3-surface-2, rgba(127, 127, 127, 0.12));
  font-size: 13px;
}

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
