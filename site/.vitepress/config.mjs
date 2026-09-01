import { defineConfig } from 'vitepress';

const repo = 'https://github.com/fgilde/QuickRun';

export default defineConfig({
  title: 'QuickRun',
  // quickrun.org serves the site at the root. It was /QuickRun/ while the site lived at
  // fgilde.github.io, and every stylesheet and script is addressed from here - so the day the
  // domain was pointed here, the pages arrived without any of their CSS.
  base: '/',
  srcDir: '.',
  outDir: '.vitepress/dist',
  cleanUrls: true,
  lastUpdated: true,
  ignoreDeadLinks: false,

  head: [
    ['link', { rel: 'icon', href: '/icon.png' }],
    ['meta', { name: 'theme-color', content: '#1f883d' }],
  ],

  locales: {
    root: {
      label: 'English',
      lang: 'en',
      link: '/',
      themeConfig: {
        nav: [
          { text: 'Home', link: '/' },
          { text: 'Download', link: '/get' },
          { text: 'Screenshots', link: '/tour' },
          { text: 'Collection', link: '/collection' },
          { text: 'First run', link: '/install' },
          { text: 'Config reference', link: '/config' },
          { text: 'Security', link: '/security' },
        ],
        sidebar: [
          {
            text: 'QuickRun',
            items: [
              { text: 'Home', link: '/' },
              { text: 'Download', link: '/get' },
              { text: 'Screenshots', link: '/tour' },
              { text: 'Collection', link: '/collection' },
              { text: 'Questions', link: '/faq' },
            ],
          },
          {
            text: 'Documentation',
            items: [
              { text: 'Download in detail', link: '/download' },
              { text: 'First run', link: '/install' },
              { text: 'Config reference', link: '/config' },
              { text: 'Config builder', link: '/builder' },
              { text: 'Without a config', link: '/no-config' },
              { text: 'Samples', link: '/samples' },
              { text: 'CLI', link: '/cli' },
              { text: 'Browser extension', link: '/extension' },
              { text: 'README badge', link: '/badge' },
              { text: 'Security', link: '/security' },
              { text: 'Privacy', link: '/privacy' },
            ],
          },
        ],
        editLink: { pattern: `${repo}/edit/main/site/:path`, text: 'Edit this page' },
      },
    },

    de: {
      label: 'Deutsch',
      lang: 'de',
      link: '/de/',
      themeConfig: {
        nav: [
          { text: 'Startseite', link: '/de/' },
          { text: 'Download', link: '/de/get' },
          { text: 'Screenshots', link: '/de/tour' },
          { text: 'Sammlung', link: '/de/collection' },
          { text: 'Erster Start', link: '/de/install' },
          { text: 'Config-Referenz', link: '/de/config' },
          { text: 'Sicherheit', link: '/de/security' },
        ],
        sidebar: [
          {
            text: 'QuickRun',
            items: [
              { text: 'Startseite', link: '/de/' },
              { text: 'Download', link: '/de/get' },
              { text: 'Screenshots', link: '/de/tour' },
              { text: 'Sammlung', link: '/de/collection' },
              { text: 'Fragen', link: '/de/faq' },
            ],
          },
          {
            text: 'Dokumentation',
            items: [
              { text: 'Download im Detail', link: '/de/download' },
              { text: 'Erster Start', link: '/de/install' },
              { text: 'Config-Referenz', link: '/de/config' },
              { text: 'Config-Builder', link: '/de/builder' },
              { text: 'Ohne Config', link: '/de/no-config' },
              { text: 'Beispiele', link: '/de/samples' },
              { text: 'CLI', link: '/de/cli' },
              { text: 'Browser-Erweiterung', link: '/de/extension' },
              { text: 'README-Badge', link: '/de/badge' },
              { text: 'Sicherheit', link: '/de/security' },
              { text: 'Datenschutz', link: '/de/privacy' },
            ],
          },
        ],
        editLink: { pattern: `${repo}/edit/main/site/:path`, text: 'Diese Seite bearbeiten' },
        docFooter: { prev: 'Zurück', next: 'Weiter' },
        outline: { label: 'Auf dieser Seite' },
        lastUpdatedText: 'Zuletzt geändert',
        returnToTopLabel: 'Nach oben',
        darkModeSwitchLabel: 'Darstellung',
      },
    },
  },

  themeConfig: {
    logo: '/icon.png',
    socialLinks: [{ icon: 'github', link: repo }],
    search: { provider: 'local' },
    // The footer comes from SiteFooter.vue through the layout-bottom slot: one footer for the
    // landing pages and the documentation, with the same links in both.
  },
});
