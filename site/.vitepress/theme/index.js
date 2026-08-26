import { h } from 'vue';
import DefaultTheme from 'vitepress/theme';
import { useData } from 'vitepress';

import './m3.css';

import BrandLayout from './BrandLayout.vue';
import DownloadHero from './DownloadHero.vue';
import ExtensionCards from './ExtensionCards.vue';
import FaqPage from './FaqPage.vue';
import Gallery from './Gallery.vue';
import GetPage from './GetPage.vue';
import LandingPage from './LandingPage.vue';
import SiteFooter from './SiteFooter.vue';
import TourPage from './TourPage.vue';

/**
 * Two kinds of page, one site.
 *
 * A page with `brand: true` in its front matter is a landing page: its own app bar, its own footer,
 * no sidebar. Everything else is the documentation, which keeps VitePress's layout and gets the same
 * footer underneath - so the links to the author and the project are on every page either way.
 */
const Layout = {
  setup() {
    const { frontmatter } = useData();

    return () => (frontmatter.value.brand
      ? h(BrandLayout)
      : h(DefaultTheme.Layout, null, { 'layout-bottom': () => h(SiteFooter) }));
  },
};

export default {
  extends: DefaultTheme,
  Layout,
  enhanceApp({ app }) {
    // Used from both language trees, so these are registered globally rather than imported per page.
    app.component('DownloadHero', DownloadHero);
    app.component('ExtensionCards', ExtensionCards);
    app.component('LandingPage', LandingPage);
    app.component('GetPage', GetPage);
    app.component('TourPage', TourPage);
    app.component('FaqPage', FaqPage);
    app.component('Gallery', Gallery);
  },
};
