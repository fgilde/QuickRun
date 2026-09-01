<script setup>
import { computed, ref } from 'vue';
import { Content, useData, useRoute, withBase } from 'vitepress';
import SiteFooter from './SiteFooter.vue';

const { lang, isDark, site } = useData();
const route = useRoute();

const de = computed(() => lang.value.startsWith('de'));
const home = computed(() => (de.value ? '/de/' : '/'));
const open = ref(false);

const t = computed(() => (de.value
  ? {
      nav: [
        { text: 'Was es macht', href: '#features' },
        { text: 'Screenshots', href: `${home.value}tour` },
        { text: 'Sammlung', href: `${home.value}collection` },
        { text: 'Fragen', href: `${home.value}faq` },
        { text: 'Dokumentation', href: `${home.value}install` },
      ],
      download: 'Herunterladen',
      menu: 'Menü',
      theme: 'Farbschema wechseln',
      other: 'English',
    }
  : {
      nav: [
        { text: 'What it does', href: '#features' },
        { text: 'Screenshots', href: `${home.value}tour` },
        { text: 'Collection', href: `${home.value}collection` },
        { text: 'Questions', href: `${home.value}faq` },
        { text: 'Documentation', href: `${home.value}install` },
      ],
      download: 'Download',
      menu: 'Menu',
      theme: 'Switch colour scheme',
      other: 'Deutsch',
    }));

/** The same page in the other language, or that language's landing page. */
const swap = computed(() => {
  const path = route.path.replace(site.value.base, '/');
  return de.value ? withBase(path.replace(/^\/de\//, '/')) : withBase(path.replace(/^\//, '/de/'));
});

/**
 * The appearance switch, written the way VitePress reads it back on the next load: the class on the
 * root element decides what is painted now, and the stored preference decides what is painted next.
 */
function toggleTheme() {
  const root = document.documentElement;
  const dark = !root.classList.contains('dark');

  root.classList.toggle('dark', dark);
  try {
    localStorage.setItem('vitepress-theme-appearance', dark ? 'dark' : 'light');
  } catch {
    // Private mode. The class is already set, so this session is fine either way.
  }
}

function href(target) {
  return target.startsWith('#') ? target : withBase(target);
}
</script>

<template>
  <div class="m3 qr-brand">
    <a class="qr-skip" :href="'#content'">{{ de ? 'Zum Inhalt' : 'Skip to content' }}</a>

    <header class="qr-bar">
      <div class="m3-wrap qr-bar-inner">
        <a class="qr-bar-logo" :href="withBase(home)">
          <!-- Two files rather than a filter: half the wordmark is near-black type and the other
               half is a violet gradient, and no single filter is kind to both. -->
          <img class="qr-logo-light" :src="withBase('/logo.png')" alt="QuickRun" width="132" height="44">
          <img class="qr-logo-dark" :src="withBase('/logo-dark.png')" alt="" width="132" height="44">
        </a>

        <nav class="qr-bar-nav" :class="{ 'qr-bar-nav--open': open }">
          <a v-for="item in t.nav" :key="item.text" :href="href(item.href)" @click="open = false">
            {{ item.text }}
          </a>
          <a class="qr-bar-lang" :href="swap">{{ t.other }}</a>
        </nav>

        <div class="qr-bar-actions">
          <button type="button" class="qr-icon-button" :title="t.theme" :aria-label="t.theme"
                  @click="toggleTheme">
            <svg v-if="isDark" viewBox="0 0 24 24" width="20" height="20" aria-hidden="true">
              <path fill="currentColor" d="M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10Zm0 2a7 7 0 1 1 0-14 7 7 0 0 1 0 14Zm0-18a1 1 0 0 1 1 1v2a1 1 0 0 1-2 0V2a1 1 0 0 1 1-1Zm0 18a1 1 0 0 1 1 1v2a1 1 0 0 1-2 0v-2a1 1 0 0 1 1-1ZM1 12a1 1 0 0 1 1-1h2a1 1 0 0 1 0 2H2a1 1 0 0 1-1-1Zm18 0a1 1 0 0 1 1-1h2a1 1 0 0 1 0 2h-2a1 1 0 0 1-1-1ZM4.2 4.2a1 1 0 0 1 1.4 0l1.5 1.5a1 1 0 0 1-1.4 1.4L4.2 5.6a1 1 0 0 1 0-1.4Zm12.7 12.7a1 1 0 0 1 1.4 0l1.5 1.5a1 1 0 0 1-1.4 1.4l-1.5-1.5a1 1 0 0 1 0-1.4ZM19.8 4.2a1 1 0 0 1 0 1.4l-1.5 1.5a1 1 0 0 1-1.4-1.4l1.5-1.5a1 1 0 0 1 1.4 0ZM7.1 16.9a1 1 0 0 1 0 1.4l-1.5 1.5a1 1 0 0 1-1.4-1.4l1.5-1.5a1 1 0 0 1 1.4 0Z"/>
            </svg>
            <svg v-else viewBox="0 0 24 24" width="20" height="20" aria-hidden="true">
              <path fill="currentColor" d="M12.7 2.1a1 1 0 0 1 .3 1.5A7 7 0 0 0 20.4 15a1 1 0 0 1 1.4 1.2A9 9 0 1 1 11.5 2.2a1 1 0 0 1 1.2-.1Z"/>
            </svg>
          </button>

          <a class="m3-button qr-bar-cta" :href="withBase(`${home}get`)">{{ t.download }}</a>

          <button type="button" class="qr-icon-button qr-bar-burger" :aria-label="t.menu"
                  :aria-expanded="open" @click="open = !open">
            <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
              <path fill="currentColor" d="M3 6h18v2H3V6Zm0 5h18v2H3v-2Zm0 5h18v2H3v-2Z"/>
            </svg>
          </button>
        </div>
      </div>
    </header>

    <main id="content" class="qr-main">
      <Content />
    </main>

    <SiteFooter />
  </div>
</template>

<style scoped>
.qr-brand {
  background: var(--m3-surface);
  min-height: 100vh;
}

.qr-skip {
  position: absolute;
  left: -9999px;
  top: 0;
  padding: 10px 16px;
  background: var(--m3-primary);
  color: var(--m3-on-primary);
  border-radius: 0 0 var(--m3-radius-s) 0;
  z-index: 40;
}

.qr-skip:focus { left: 0; }

.qr-bar {
  position: sticky;
  top: 0;
  z-index: 30;
  backdrop-filter: blur(14px);
  background: color-mix(in srgb, var(--m3-surface) 84%, transparent);
  border-bottom: 1px solid var(--m3-outline-variant);
}

.qr-bar-inner {
  display: flex;
  align-items: center;
  gap: 20px;
  height: 72px;
}

.qr-bar-logo { display: flex; align-items: center; }
.qr-bar-logo img { height: 34px; width: auto; }
.qr-logo-dark { display: none; }
html.dark .qr-logo-light { display: none; }
html.dark .qr-logo-dark { display: block; }

.qr-bar-nav { display: flex; align-items: center; gap: 4px; margin-left: auto; }

.qr-bar-nav a {
  padding: 9px 14px;
  border-radius: var(--m3-radius-full);
  color: var(--m3-on-surface-variant);
  font-size: .95rem;
  font-weight: 560;
  text-decoration: none;
}

.qr-bar-nav a:hover { background: var(--m3-surface-container-high); color: var(--m3-on-surface); }
.qr-bar-lang { color: var(--m3-primary) !important; }

.qr-bar-actions { display: flex; align-items: center; gap: 10px; }

.qr-icon-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  border: 0;
  border-radius: var(--m3-radius-full);
  background: transparent;
  color: var(--m3-on-surface-variant);
  cursor: pointer;
}

.qr-icon-button:hover { background: var(--m3-surface-container-high); color: var(--m3-on-surface); }

.qr-bar-burger { display: none; }
.qr-main { display: block; }

@media (max-width: 860px) {
  .qr-bar-inner { height: 64px; gap: 10px; }
  .qr-bar-cta { display: none; }
  .qr-bar-burger { display: inline-flex; }

  .qr-bar-nav {
    position: absolute;
    top: 64px;
    left: 0;
    right: 0;
    display: none;
    flex-direction: column;
    align-items: stretch;
    gap: 2px;
    padding: 10px 16px 18px;
    background: var(--m3-surface);
    border-bottom: 1px solid var(--m3-outline-variant);
    box-shadow: var(--m3-elevation-2);
  }

  .qr-bar-nav--open { display: flex; }
  .qr-bar-nav a { padding: 12px 14px; }
}
</style>
