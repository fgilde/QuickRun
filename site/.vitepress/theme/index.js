import DefaultTheme from 'vitepress/theme';
import DownloadButtons from './DownloadButtons.vue';

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    // Used from both language trees, so it is registered globally rather than imported per page.
    app.component('DownloadButtons', DownloadButtons);
  },
};
