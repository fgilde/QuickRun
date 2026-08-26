<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { withBase } from 'vitepress';

const props = defineProps({
  shots: { type: Array, required: true },
  start: { type: Number, default: 0 },
});

const index = ref(props.start);
const zoomed = ref(false);

const current = computed(() => props.shots[index.value] ?? props.shots[0]);
const source = (shot) => withBase(`/screenshots/${shot.file}`);

function go(delta) {
  const count = props.shots.length;
  index.value = (index.value + delta + count) % count;
}

function onKey(event) {
  if (event.key === 'ArrowRight') go(1);
  else if (event.key === 'ArrowLeft') go(-1);
  else if (event.key === 'Escape') zoomed.value = false;
}

onMounted(() => window.addEventListener('keydown', onKey));
onBeforeUnmount(() => window.removeEventListener('keydown', onKey));
</script>

<template>
  <div class="qr-gallery">
    <figure class="qr-stage">
      <button type="button" class="qr-nav qr-nav--prev" aria-label="Previous" @click="go(-1)">
        <svg viewBox="0 0 24 24" width="24" height="24" aria-hidden="true">
          <path fill="currentColor" d="M15.4 7.4 14 6l-6 6 6 6 1.4-1.4L10.8 12l4.6-4.6Z"/>
        </svg>
      </button>

      <button type="button" class="qr-shot" @click="zoomed = true">
        <img :src="source(current)" :alt="current.alt ?? current.title" loading="lazy" decoding="async">
        <span class="qr-zoom-hint">
          <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
            <path fill="currentColor" d="M15.5 14h-.8l-.3-.3a6.5 6.5 0 1 0-.7.7l.3.3v.8l5 5 1.5-1.5-5-5Zm-6 0a4.5 4.5 0 1 1 0-9 4.5 4.5 0 0 1 0 9Z"/>
          </svg>
        </span>
      </button>

      <button type="button" class="qr-nav qr-nav--next" aria-label="Next" @click="go(1)">
        <svg viewBox="0 0 24 24" width="24" height="24" aria-hidden="true">
          <path fill="currentColor" d="M8.6 16.6 10 18l6-6-6-6-1.4 1.4L13.2 12l-4.6 4.6Z"/>
        </svg>
      </button>

      <figcaption>
        <strong>{{ current.title }}</strong>
        <span>{{ current.text }}</span>
      </figcaption>
    </figure>

    <div class="qr-thumbs" role="tablist">
      <button v-for="(shot, at) in shots" :key="shot.file" type="button" role="tab"
              class="qr-thumb" :class="{ 'qr-thumb--active': at === index }"
              :aria-selected="at === index" :title="shot.title" @click="index = at">
        <img :src="source(shot)" :alt="shot.title" loading="lazy" decoding="async">
        <span>{{ shot.title }}</span>
      </button>
    </div>

    <!-- Full size, because a screenshot of a log window is unreadable at a third of the width. -->
    <div v-if="zoomed" class="qr-lightbox" @click="zoomed = false">
      <img :src="source(current)" :alt="current.alt ?? current.title">
      <div class="qr-lightbox-bar" @click.stop>
        <button type="button" aria-label="Previous" @click="go(-1)">‹</button>
        <span>{{ current.title }}</span>
        <button type="button" aria-label="Next" @click="go(1)">›</button>
        <button type="button" class="qr-lightbox-close" aria-label="Close" @click="zoomed = false">✕</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.qr-gallery { display: grid; gap: 18px; }

.qr-stage {
  position: relative;
  margin: 0;
  padding: 14px 14px 0;
  border-radius: var(--m3-radius-l);
  background: linear-gradient(140deg,
    color-mix(in srgb, var(--m3-brand-lavender) 34%, var(--m3-surface-container)),
    color-mix(in srgb, var(--m3-brand-periwinkle) 26%, var(--m3-surface-container)));
  box-shadow: var(--m3-elevation-2);
}

.qr-shot {
  display: block;
  width: 100%;
  padding: 0;
  border: 0;
  background: none;
  cursor: zoom-in;
  position: relative;
}

.qr-shot img {
  display: block;
  width: 100%;
  height: auto;
  border-radius: var(--m3-radius-s);
  border: 1px solid color-mix(in srgb, var(--m3-outline) 40%, transparent);
  box-shadow: var(--m3-elevation-1);
  background: var(--m3-surface);
}

.qr-zoom-hint {
  position: absolute;
  right: 12px;
  bottom: 12px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: var(--m3-radius-full);
  background: color-mix(in srgb, var(--m3-surface) 88%, transparent);
  color: var(--m3-on-surface-variant);
  box-shadow: var(--m3-elevation-1);
}

.qr-nav {
  position: absolute;
  top: 50%;
  transform: translateY(-50%);
  z-index: 2;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border: 0;
  border-radius: var(--m3-radius-full);
  background: var(--m3-surface);
  color: var(--m3-on-surface);
  box-shadow: var(--m3-elevation-2);
  cursor: pointer;
}

.qr-nav--prev { left: -6px; }
.qr-nav--next { right: -6px; }
.qr-nav:hover { background: var(--m3-primary-container); color: var(--m3-on-primary-container); }

figcaption {
  display: grid;
  gap: 2px;
  padding: 14px 6px 16px;
  color: var(--m3-on-surface);
}

figcaption strong { font-size: 1rem; }
figcaption span { color: var(--m3-on-surface-variant); font-size: .94rem; }

.qr-thumbs {
  display: flex;
  gap: 10px;
  overflow-x: auto;
  padding-bottom: 6px;
  scrollbar-width: thin;
}

.qr-thumb {
  flex: 0 0 152px;
  display: grid;
  gap: 6px;
  padding: 6px;
  border: 1px solid var(--m3-outline-variant);
  border-radius: var(--m3-radius-s);
  background: var(--m3-surface-container-low);
  color: var(--m3-on-surface-variant);
  font: inherit;
  font-size: .78rem;
  text-align: left;
  cursor: pointer;
}

.qr-thumb img { width: 100%; height: 80px; object-fit: cover; object-position: top left; border-radius: 6px; }
.qr-thumb--active {
  border-color: var(--m3-primary);
  background: var(--m3-primary-container);
  color: var(--m3-on-primary-container);
}

.qr-lightbox {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: grid;
  place-items: center;
  padding: 28px;
  background: color-mix(in srgb, #0d0b12 86%, transparent);
  cursor: zoom-out;
}

.qr-lightbox img {
  max-width: min(1400px, 96vw);
  max-height: 84vh;
  width: auto;
  height: auto;
  border-radius: var(--m3-radius-s);
  box-shadow: var(--m3-elevation-3);
  background: #fff;
}

.qr-lightbox-bar {
  position: fixed;
  bottom: 22px;
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 8px 10px 8px 14px;
  border-radius: var(--m3-radius-full);
  background: var(--m3-surface);
  color: var(--m3-on-surface);
  box-shadow: var(--m3-elevation-3);
  cursor: default;
  font-size: .94rem;
}

.qr-lightbox-bar button {
  width: 34px;
  height: 34px;
  border: 0;
  border-radius: var(--m3-radius-full);
  background: var(--m3-surface-container-high);
  color: var(--m3-on-surface);
  font-size: 1.1rem;
  line-height: 1;
  cursor: pointer;
}

.qr-lightbox-bar button:hover { background: var(--m3-primary-container); color: var(--m3-on-primary-container); }

@media (max-width: 700px) {
  .qr-nav { display: none; }
  .qr-stage { padding: 10px 10px 0; }
}
</style>
