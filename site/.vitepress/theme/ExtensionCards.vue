<script setup>
import { computed } from 'vue';

const props = defineProps({
  lang: { type: String, default: 'en' },
});

const base = 'https://github.com/fgilde/QuickRun/releases/latest/download';

const t = computed(() => (props.lang === 'de'
  ? { pending: 'Store-Prüfung läuft', download: 'Build herunterladen', shared: 'Chromium-Build' }
  : { pending: 'store review pending', download: 'Download the build', shared: 'Chromium build' }));

const browsers = [
  { name: 'Chrome', tint: '#f9ab00', asset: 'quickrun-extension-chromium.zip', shared: false },
  { name: 'Edge', tint: '#0f7ebf', asset: 'quickrun-extension-chromium.zip', shared: true },
  { name: 'Opera', tint: '#e5233c', asset: 'quickrun-extension-chromium.zip', shared: true },
  { name: 'Firefox', tint: '#ff7139', asset: 'quickrun-extension-firefox.zip', shared: false },
];
</script>

<template>
  <div class="qr-browsers">
    <article v-for="browser in browsers" :key="browser.name" :style="{ '--tint': browser.tint }">
      <!-- A ring in each browser's own colour rather than its logo: an approximated brand mark
           looks worse than none, and logos come with trademark strings attached. -->
      <span class="qr-ring" aria-hidden="true"></span>
      <h4>{{ browser.name }}</h4>
      <span class="qr-state">{{ t.pending }}</span>
      <a :href="`${base}/${browser.asset}`">
        {{ browser.shared ? t.shared : t.download }}
      </a>
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
  border: 1px solid var(--vp-c-divider);
  border-radius: 10px;
  background: var(--vp-c-bg-soft);
  text-align: center;
}

.qr-ring {
  display: block;
  width: 30px;
  height: 30px;
  margin: 0 auto 10px;
  border-radius: 50%;
  border: 4px solid var(--tint);
}

.qr-browsers h4 { margin: 0 0 2px; font-size: 15px; }

.qr-state {
  display: block;
  font-size: 11.5px;
  color: var(--vp-c-text-3);
  margin-bottom: 10px;
}

.qr-browsers a { font-size: 13px; }
</style>
