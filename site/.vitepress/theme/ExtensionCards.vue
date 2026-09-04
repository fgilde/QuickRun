<script setup>
import { computed, onMounted, ref } from 'vue';
import { withBase } from 'vitepress';

import { currentBrowser, listingFor } from './stores.js';

const props = defineProps({
  lang: { type: String, default: 'en' },
});

const base = 'https://github.com/fgilde/QuickRun/releases/latest/download';
const logos = withBase('/logos');

const t = computed(() => (props.lang === 'de'
  ? {
      pending: 'Store-Prüfung läuft',
      download: 'Build herunterladen',
      shared: 'Chromium-Build',
      install: 'Installieren',
      viaChrome: 'aus dem Chrome Web Store',
      manual: 'oder von Hand laden',
      yours: 'Dein Browser',
      installHere: 'Für {browser} installieren',
      noStoreYet: 'Für {browser} gibt es die Store-Version noch nicht — der Build unten lässt sich '
        + 'von Hand laden.',
      safariState: 'ohne Store, von Hand',
      safariInvite: 'Für Safari gibt es keine Store-Version: der Build unten lädt sich temporär, '
        + 'wenn unsignierte Erweiterungen erlaubt sind.',
    }
  : {
      pending: 'store review pending',
      download: 'Download the build',
      shared: 'Chromium build',
      install: 'Install',
      viaChrome: 'from the Chrome Web Store',
      manual: 'or load it by hand',
      yours: 'Your browser',
      installHere: 'Install for {browser}',
      noStoreYet: 'There is no store build for {browser} yet — the download below loads by hand.',
      safariState: 'no store, by hand',
      safariInvite: 'Safari has no store build: the download below loads temporarily, once unsigned '
        + 'extensions are allowed.',
    }));

// The official logos, kept as separate files: each carries gradients with ids a-d, and inlining
// four of them on one page would make those ids collide.
const browsers = computed(() => [
  { name: 'Chrome', logo: 'chrome', tint: '#f9ab00', asset: 'quickrun-extension-chromium.zip', shared: false },
  { name: 'Edge', logo: 'edge', tint: '#0f7ebf', asset: 'quickrun-extension-chromium.zip', shared: true },
  { name: 'Opera', logo: 'opera', tint: '#e5233c', asset: 'quickrun-extension-chromium.zip', shared: true },
  { name: 'Firefox', logo: 'firefox', tint: '#ff7139', asset: 'quickrun-extension-firefox.zip', shared: false },
  // Apple's own mark rather than Safari's compass: this repository ships the one it already had for
  // the macOS download, and drawing somebody else's logo from memory is not a thing to do.
  //
  // "review pending" would be a lie here. Nothing has been submitted and nothing can be until there
  // is a signed app to submit, so this card says what is actually true - the build is downloadable
  // and Safari will load it once it is told to accept unsigned extensions.
  {
    name: 'Safari',
    logo: 'apple',
    tint: '#0071e3',
    asset: 'quickrun-extension-safari.zip',
    shared: false,
    state: 'safariState',
    invite: 'safariInvite',
  },
].map((browser) => ({ ...browser, listing: listingFor(browser.logo) })));

/**
 * The browser reading this page.
 *
 * Read after mounting rather than while rendering: this page is built to static HTML, and a build
 * machine has no browser to ask - deciding during the render would bake one answer into the file
 * everybody gets.
 */
const mine = ref(null);
onMounted(() => { mine.value = currentBrowser(); });

const yours = computed(() => browsers.value.find((browser) => browser.logo === mine.value) ?? null);

const invitation = computed(() => {
  if (!yours.value) return null;
  if (yours.value.invite) return t.value[yours.value.invite];

  const template = yours.value.listing ? t.value.installHere : t.value.noStoreYet;
  return template.replace('{browser}', yours.value.name);
});
</script>

<template>
  <!-- One button for the browser actually reading this, above the grid of all of them. Someone on
       Edge should not have to work out which of four cards is theirs. -->
  <a
    v-if="yours && yours.listing"
    class="qr-mine"
    :href="yours.listing"
    :style="{ '--tint': yours.tint }"
    target="_blank"
    rel="noreferrer"
  >
    <img class="qr-mine-logo" :src="`${logos}/${yours.logo}.svg`" :alt="yours.name">
    <span>
      <small>{{ t.yours }}</small>
      <strong>{{ invitation }}</strong>
    </span>
  </a>

  <p v-else-if="yours" class="qr-mine-none">{{ invitation }}</p>

  <div class="qr-browsers">
    <article
      v-for="browser in browsers"
      :key="browser.name"
      :class="{ 'qr-is-yours': browser.logo === mine }"
      :style="{ '--tint': browser.tint }"
    >
      <img class="qr-browser-logo" :src="`${logos}/${browser.logo}.svg`" :alt="browser.name">
      <h4>{{ browser.name }}</h4>

      <template v-if="browser.listing">
        <span class="qr-state">{{ browser.logo === 'opera' ? t.viaChrome : '&nbsp;' }}</span>
        <a class="qr-store" :href="browser.listing" target="_blank" rel="noreferrer">{{ t.install }}</a>
        <a class="qr-fallback" :href="`${base}/${browser.asset}`">{{ t.manual }}</a>
      </template>

      <template v-else>
        <span class="qr-state">{{ browser.state ? t[browser.state] : t.pending }}</span>
        <a :href="`${base}/${browser.asset}`">
          {{ browser.shared ? t.shared : t.download }}
        </a>
      </template>
    </article>
  </div>
</template>

<style scoped>
.qr-mine {
  display: flex;
  align-items: center;
  gap: 14px;
  margin: 20px 0 0;
  padding: 14px 18px;
  border: 1px solid var(--tint);
  border-radius: 12px;
  background: var(--vp-c-bg-soft);
  text-decoration: none;
  color: inherit;
}

.qr-mine:hover { background: var(--vp-c-bg-elv); }

.qr-mine-logo { width: 34px; height: 34px; object-fit: contain; }

.qr-mine small {
  display: block;
  font-size: 11.5px;
  color: var(--vp-c-text-3);
}

.qr-mine strong { font-size: 15px; }

.qr-mine-none {
  margin: 20px 0 0;
  font-size: 13px;
  color: var(--vp-c-text-2);
}

/* The one the visitor is using, so the button above and the grid below say the same thing. */
.qr-browsers .qr-is-yours {
  outline: 2px solid var(--tint);
  outline-offset: -1px;
}

.qr-browsers {
  display: grid;
  gap: 12px;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  margin: 20px 0;
}

.qr-browsers article {
  padding: 16px;
  /* The shorthand has to come first: it would otherwise reset the tinted top border. */
  border: 1px solid var(--vp-c-divider);
  border-top: 3px solid var(--tint);
  border-radius: 10px;
  background: var(--vp-c-bg-soft);
  text-align: center;
}

.qr-browser-logo {
  display: block;
  width: 40px;
  height: 40px;
  margin: 0 auto 10px;
  object-fit: contain;
}

.qr-browsers h4 { margin: 0 0 2px; font-size: 15px; }

.qr-state {
  display: block;
  font-size: 11.5px;
  color: var(--vp-c-text-3);
  margin-bottom: 10px;
}

.qr-browsers a { font-size: 13px; }

.qr-store {
  display: inline-block;
  padding: 6px 14px;
  border-radius: 999px;
  background: var(--tint);
  color: #fff;
  font-weight: 600;
  text-decoration: none;
}

.qr-store:hover { filter: brightness(1.08); }

/* The store is the way in; the zip is for anyone who cannot or would rather not use it. */
.qr-fallback {
  display: block;
  margin-top: 8px;
  font-size: 12px;
  color: var(--vp-c-text-3);
}
</style>
