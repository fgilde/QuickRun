import { defineConfig } from 'vitepress';

const repo = 'https://github.com/fgilde/QuickRun';

export default defineConfig({
  title: 'QuickRun',
  // GitHub Pages serves the repository at /QuickRun/.
  base: '/QuickRun/',
  srcDir: '.',
  outDir: '.vitepress/dist',
  cleanUrls: true,
  lastUpdated: true,
  ignoreDeadLinks: false,

  head: [
    ['link', { rel: 'icon', href: '/QuickRun/icon.png' }],
    ['meta', { name: 'theme-color', content: '#1f883d' }],
  ],

  locales: {
    root: {
      label: 'English',
      lang: 'en',
      link: '/',
      themeConfig: {
        nav: [
          { text: 'Install', link: '/install' },
          { text: 'Config reference', link: '/config' },
          { text: 'Samples', link: '/samples' },
          { text: 'Security', link: '/security' },
        ],
        sidebar: [
          {
            text: 'QuickRun',
            items: [
              { text: 'Overview', link: '/' },
              { text: 'Install', link: '/install' },
              { text: 'Config reference', link: '/config' },
              { text: 'Samples', link: '/samples' },
              { text: 'CLI', link: '/cli' },
              { text: 'Browser extension', link: '/extension' },
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
          { text: 'Installation', link: '/de/install' },
          { text: 'Config-Referenz', link: '/de/config' },
          { text: 'Beispiele', link: '/de/samples' },
          { text: 'Sicherheit', link: '/de/security' },
        ],
        sidebar: [
          {
            text: 'QuickRun',
            items: [
              { text: 'Überblick', link: '/de/' },
              { text: 'Installation', link: '/de/install' },
              { text: 'Config-Referenz', link: '/de/config' },
              { text: 'Beispiele', link: '/de/samples' },
              { text: 'CLI', link: '/de/cli' },
              { text: 'Browser-Erweiterung', link: '/de/extension' },
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
    footer: { message: 'MIT licensed', copyright: 'QuickRun' },
  },
});
