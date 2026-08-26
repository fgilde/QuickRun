<script setup>
import { computed } from 'vue';
import { useData, withBase } from 'vitepress';
import Gallery from './Gallery.vue';
import { shots } from './shots.js';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));
const home = computed(() => (de.value ? '/de/' : '/'));
const link = (path) => withBase(`${home.value}${path}`);

const gallery = computed(() => shots(de.value));

const t = computed(() => (de.value
  ? {
      eyebrow: 'Screenshots',
      title: 'So sieht es aus',
      lead: 'Aufgenommen aus einem echten Lauf von fgilde/MudBlazor.Extensions gegen einen '
        + 'veröffentlichten Build. Klicken zum Vergrößern, Pfeiltasten zum Durchschalten.',
      detailsTitle: 'Was in den Bildern zu sehen ist',
      cta: 'Herunterladen und selbst ansehen',
      docs: 'Dokumentation lesen',
    }
  : {
      eyebrow: 'Screenshots',
      title: 'What it looks like',
      lead: 'Captured from a real run of fgilde/MudBlazor.Extensions against a released build. Click to '
        + 'enlarge, arrow keys to move through them.',
      detailsTitle: 'What the pictures show',
      cta: 'Download and see for yourself',
      docs: 'Read the documentation',
    }));
</script>

<template>
  <div class="qr-tour">
    <section class="qr-tour-head">
      <div class="m3-wrap">
        <span class="m3-label">{{ t.eyebrow }}</span>
        <h1 class="m3-display qr-tour-title">{{ t.title }}</h1>
        <p class="m3-body-lg qr-tour-lead">{{ t.lead }}</p>
      </div>
    </section>

    <section class="m3-wrap qr-tour-gallery">
      <Gallery :shots="gallery" />
    </section>

    <section class="m3-wrap qr-tour-details">
      <h2 class="m3-headline">{{ t.detailsTitle }}</h2>
      <div class="qr-tour-grid">
        <article v-for="shot in gallery" :key="shot.file" class="m3-card">
          <h3 class="m3-title">{{ shot.title }}</h3>
          <p class="m3-body">{{ shot.text }}</p>
        </article>
      </div>

      <div class="qr-tour-actions">
        <a class="m3-button" :href="link('get')">{{ t.cta }}</a>
        <a class="m3-button m3-button--tonal" :href="link('install')">{{ t.docs }}</a>
      </div>
    </section>
  </div>
</template>

<style scoped>
.qr-tour-head { padding: 60px 0 26px; }
.qr-tour-title { margin-top: 12px; }
.qr-tour-lead { margin: 16px 0 0; max-width: 60ch; }
.qr-tour-gallery { padding: 18px 0 10px; }
.qr-tour-details { padding: 40px 0 64px; }

.qr-tour-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 16px;
  margin: 22px 0 0;
}

.qr-tour-actions { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 34px; }
</style>
