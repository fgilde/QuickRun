<script setup>
import { computed } from 'vue';
import { withBase } from 'vitepress';

import { listingFor } from './stores.js';

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
    }
  : {
      pending: 'store review pending',
      download: 'Download the build',
      shared: 'Chromium build',
      install: 'Install',
      viaChrome: 'from the Chrome Web Store',
      manual: 'or load it by hand',
    }));

// The official logos, kept as separate files: each carries gradients with ids a-d, and inlining
// four of them on one page would make those ids collide.
const browsers = computed(() => [
  { name: 'Chrome', logo: 'chrome', tint: '#f9ab00', asset: 'quickrun-extension-chromium.zip', shared: false },
  { name: 'Edge', logo: 'edge', tint: '#0f7ebf', asset: 'quickrun-extension-chromium.zip', shared: true },
  { name: 'Opera', logo: 'opera', tint: '#e5233c', asset: 'quickrun-extension-chromium.zip', shared: true },
  { name: 'Firefox', logo: 'firefox', tint: '#ff7139', asset: 'quickrun-extension-firefox.zip', shared: false },
].map((browser) => ({ ...browser, listing: listingFor(browser.logo) })));
</script>

<template>
  <div class="qr-browsers">
    <article v-for="browser in browsers" :key="browser.name" :style="{ '--tint': browser.tint }">
      <img class="qr-browser-logo" :src="`${logos}/${browser.logo}.svg`" :alt="browser.name">
      <h4>{{ browser.name }}</h4>

      <template v-if="browser.listing">
        <span class="qr-state">{{ browser.logo === 'opera' ? t.viaChrome : '&nbsp;' }}</span>
        <a class="qr-store" :href="browser.listing" target="_blank" rel="noreferrer">{{ t.install }}</a>
        <a class="qr-fallback" :href="`${base}/${browser.asset}`">{{ t.manual }}</a>
      </template>

      <template v-else>
        <span class="qr-state">{{ t.pending }}</span>
        <a :href="`${base}/${browser.asset}`">
          {{ browser.shared ? t.shared : t.download }}
        </a>
      </template>
    </article>
  </div>
</template>

<style scoped>
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
