<script setup>
import { computed } from 'vue';
import { useData, withBase } from 'vitepress';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));
const home = computed(() => (de.value ? '/de/' : '/'));

const t = computed(() => (de.value
  ? {
      tagline: 'Jedes Git-Repository mit einem Klick starten.',
      product: 'Produkt',
      docs: 'Dokumentation',
      project: 'Projekt',
      links: {
        landing: 'Startseite',
        download: 'Download',
        tour: 'Screenshots',
        faq: 'Fragen',
        install: 'Erster Start',
        config: 'Config-Referenz',
        builder: 'Config-Builder',
        cli: 'Kommandozeile',
        extension: 'Browser-Erweiterung',
        badge: 'README-Badge',
        security: 'Sicherheit',
        privacy: 'Datenschutz',
        releases: 'Releases',
        issues: 'Probleme melden',
      },
      madeBy: 'made by',
      profile: 'GitHub-Profil',
      license: 'Quelloffen auf GitHub',
    }
  : {
      tagline: 'Run any git repository with one click.',
      product: 'Product',
      docs: 'Documentation',
      project: 'Project',
      links: {
        landing: 'Home',
        download: 'Download',
        tour: 'Screenshots',
        faq: 'Questions',
        install: 'First run',
        config: 'Config reference',
        builder: 'Config builder',
        cli: 'Command line',
        extension: 'Browser extension',
        badge: 'README badge',
        security: 'Security',
        privacy: 'Privacy',
        releases: 'Releases',
        issues: 'Report an issue',
      },
      madeBy: 'made by',
      profile: 'GitHub profile',
      license: 'Open source on GitHub',
    }));

const columns = computed(() => {
  const base = home.value;
  const l = t.value.links;

  return [
    {
      title: t.value.product,
      items: [
        { text: l.landing, href: withBase(base) },
        { text: l.download, href: withBase(`${base}get`) },
        { text: l.tour, href: withBase(`${base}tour`) },
        { text: l.faq, href: withBase(`${base}faq`) },
      ],
    },
    {
      title: t.value.docs,
      items: [
        { text: l.install, href: withBase(`${base}install`) },
        { text: l.config, href: withBase(`${base}config`) },
        { text: l.builder, href: withBase(`${base}builder`) },
        { text: l.cli, href: withBase(`${base}cli`) },
        { text: l.extension, href: withBase(`${base}extension`) },
        { text: l.badge, href: withBase(`${base}badge`) },
      ],
    },
    {
      title: t.value.project,
      items: [
        { text: l.security, href: withBase(`${base}security`) },
        { text: l.privacy, href: withBase(`${base}privacy`) },
        { text: l.releases, href: 'https://github.com/fgilde/QuickRun/releases', external: true },
        { text: l.issues, href: 'https://github.com/fgilde/QuickRun/issues', external: true },
      ],
    },
  ];
});
</script>

<template>
  <footer class="qr-footer m3">
    <div class="m3-wrap qr-footer-inner">
      <div class="qr-footer-brand">
        <img class="qr-logo-light" :src="withBase('/logo.png')" alt="QuickRun" width="150" height="50">
        <img class="qr-logo-dark" :src="withBase('/logo-dark.png')" alt="" width="150" height="50">
        <p class="m3-body">{{ t.tagline }}</p>
        <a class="qr-footer-github" href="https://github.com/fgilde/QuickRun"
           target="_blank" rel="noreferrer">
          <svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true">
            <path fill="currentColor" d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38
              0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01
              1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95
              0-.87.31-1.59.82-2.15-.07-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.4 7.4 0 0 1 2-.27c.68 0 1.36.09 2 .27
              1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54
              1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A7.995 7.995 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/>
          </svg>
          {{ t.license }}
        </a>
      </div>

      <nav v-for="column in columns" :key="column.title" class="qr-footer-column">
        <h3>{{ column.title }}</h3>
        <ul>
          <li v-for="item in column.items" :key="item.text">
            <a :href="item.href"
               :target="item.external ? '_blank' : undefined"
               :rel="item.external ? 'noreferrer' : undefined">{{ item.text }}</a>
          </li>
        </ul>
      </nav>
    </div>

    <div class="m3-wrap qr-footer-base">
      <span>
        {{ t.madeBy }}
        <a href="https://www.gilde.org" target="_blank" rel="noreferrer">gilde.org</a>
      </span>
      <a href="https://github.com/fgilde" target="_blank" rel="noreferrer">
        github.com/fgilde · {{ t.profile }}
      </a>
    </div>
  </footer>
</template>

<style scoped>
.qr-footer {
  margin-top: 64px;
  padding: 56px 0 28px;
  background: var(--m3-surface-container);
  border-top: 1px solid var(--m3-outline-variant);
}

.qr-footer-inner {
  display: grid;
  grid-template-columns: 1.5fr repeat(3, 1fr);
  gap: 40px;
  align-items: start;
}

.qr-footer-brand img { max-width: 150px; height: auto; }
.qr-logo-dark { display: none; }
html.dark .qr-logo-light { display: none; }
html.dark .qr-logo-dark { display: block; }
.qr-footer-brand p { margin: 12px 0 16px; max-width: 34ch; }

.qr-footer-github {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: var(--m3-primary);
  font-size: .92rem;
  font-weight: 600;
  text-decoration: none;
}

.qr-footer-github:hover { text-decoration: underline; }

.qr-footer-column h3 {
  margin: 0 0 12px;
  font-size: .78rem;
  font-weight: 700;
  letter-spacing: .09em;
  text-transform: uppercase;
  color: var(--m3-on-surface);
}

.qr-footer-column ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 9px; }

.qr-footer-column a {
  color: var(--m3-on-surface-variant);
  font-size: .95rem;
  text-decoration: none;
}

.qr-footer-column a:hover { color: var(--m3-primary); }

.qr-footer-base {
  display: flex;
  flex-wrap: wrap;
  gap: 12px 24px;
  justify-content: space-between;
  margin-top: 44px;
  padding-top: 20px;
  border-top: 1px solid var(--m3-outline-variant);
  font-size: .9rem;
  color: var(--m3-on-surface-variant);
}

.qr-footer-base a { color: var(--m3-primary); text-decoration: none; font-weight: 600; }
.qr-footer-base a:hover { text-decoration: underline; }

@media (max-width: 900px) {
  .qr-footer-inner { grid-template-columns: 1fr 1fr; }
}

@media (max-width: 560px) {
  .qr-footer-inner { grid-template-columns: 1fr; gap: 28px; }
}
</style>
