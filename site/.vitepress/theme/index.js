import DefaultTheme from 'vitepress/theme';
import DownloadHero from './DownloadHero.vue';
import ExtensionCards from './ExtensionCards.vue';

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    // Used from both language trees, so it is registered globally rather than imported per page.
    app.component('DownloadHero', DownloadHero);
    app.component('ExtensionCards', ExtensionCards);
  },
};
