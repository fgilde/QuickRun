<script setup>
import { computed, onMounted, ref } from 'vue';
import { useData } from 'vitepress';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));

/**
 * The two panels come from connect.gilde.org, loaded when this page is opened.
 *
 * Here rather than in the site's head: it is 128KB that only this page and the one button in the
 * questions page need, and a reader looking at the config reference should not be paying for it.
 */
const ready = ref(false);
const failed = ref(false);

onMounted(() => {
  if (customElements.get('gilde-contact')) { ready.value = true; return; }

  const script = document.createElement('script');
  script.type = 'module';
  script.src = 'https://connect.gilde.org/widgets/v1.js';
  script.onload = () => { ready.value = true; };
  script.onerror = () => { failed.value = true; };
  document.head.append(script);
});

/** The site's own violet, so the panels are part of the page rather than guests on it. */
const ACCENT = '#5a45d6';

const t = computed(() => (de.value
  ? {
      eyebrow: 'Kontakt und Unterstützung',
      title: 'Schreib uns, oder halte QuickRun am Laufen',
      lead: 'QuickRun ist ein Ein-Personen-Projekt und quelloffen. Eine Nachricht kommt direkt an — '
        + 'für einen Fehler ist ein Issue auf GitHub oft der schnellere Weg, alles andere gern hier.',
      contact: 'Nachricht schreiben',
      contactText: 'Die Nachricht landet direkt im Postfach — kein Formularsammler dazwischen.',
      support: 'QuickRun unterstützen',
      supportText: 'Nichts davon wird erwartet. Es bezahlt die Domain, die Signaturen und die '
        + 'Abende, an denen die nächste Version entsteht.',
      issue: 'Fehler und Vorschläge gehören auf GitHub:',
      issueLink: 'Issue öffnen',
      loading: 'Wird geladen…',
      offline: 'Die Panels kommen von connect.gilde.org und ließen sich gerade nicht laden. '
        + 'Über GitHub geht es auch:',
    }
  : {
      eyebrow: 'Contact and support',
      title: 'Say something, or keep QuickRun going',
      lead: 'QuickRun is a one-person project and open source. A message here arrives directly — '
        + 'for a bug an issue on GitHub is usually faster, and anything else is welcome here.',
      contact: 'Send a message',
      contactText: 'A message lands in the inbox directly — no form collector in between.',
      support: 'Support QuickRun',
      supportText: 'None of this is expected. It pays for the domain, the signing certificates and '
        + 'the evenings the next version comes out of.',
      issue: 'Bugs and ideas belong on GitHub:',
      issueLink: 'Open an issue',
      loading: 'Loading…',
      offline: 'The panels come from connect.gilde.org and could not be loaded just now. '
        + 'GitHub works too:',
    }));
</script>

<template>
  <div class="qr-support">
    <span class="m3-label">{{ t.eyebrow }}</span>
    <h1 class="m3-display qr-support-title">{{ t.title }}</h1>
    <p class="m3-body-lg qr-support-lead">{{ t.lead }}</p>

    <p v-if="failed" class="m3-body qr-support-offline">
      {{ t.offline }}
      <a href="https://github.com/fgilde/QuickRun/issues" target="_blank" rel="noreferrer">{{ t.issueLink }}</a>
    </p>

    <div v-else class="qr-support-grid">
      <section>
        <p class="m3-body qr-support-note">{{ t.contactText }}</p>
        <gilde-contact v-if="ready"
                       project="fgilde/QuickRun" widget="contact" :inline.attr="''" theme="auto"
                       :accent="ACCENT" :language="de ? 'de' : 'en'" :title="t.contact"
                       width="560" radius="16" padding="26"
                       show-logo="true" show-description="false" show-homepage="true"
                       show-preview-notice="false" show-footer="true"
                       footer-brand="QuickRun" footer-tagline="gilde.org" />
        <p v-else class="m3-body qr-support-waiting">{{ t.loading }}</p>
      </section>

      <section>
        <p class="m3-body qr-support-note">{{ t.supportText }}</p>
        <gilde-support v-if="ready"
                       project="fgilde/QuickRun" widget="support" :inline.attr="''" theme="auto"
                       :accent="ACCENT" :language="de ? 'de' : 'en'" :title="t.support"
                       width="560" radius="16" padding="26"
                       show-logo="true" show-description="false" show-homepage="true"
                       show-preview-notice="false" show-footer="true"
                       footer-brand="QuickRun" footer-tagline="gilde.org"
                       show-support-hint="false" support-layout="rows"
                       show-support-icons="true" show-support-qr="true" />
        <p v-else class="m3-body qr-support-waiting">{{ t.loading }}</p>
      </section>
    </div>

    <p class="m3-body qr-support-issue">
      {{ t.issue }}
      <a href="https://github.com/fgilde/QuickRun/issues" target="_blank" rel="noreferrer">{{ t.issueLink }}</a>
    </p>
  </div>
</template>

<style scoped>
.qr-support { max-width: 1080px; margin: 0 auto; padding: 40px 20px 72px; }
.qr-support-title { margin: 8px 0 12px; }
.qr-support-lead { max-width: 62ch; margin: 0 0 36px; }

.qr-support-grid {
  display: grid; gap: 28px; align-items: start;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 380px), 1fr));
}

.qr-support-note { margin: 0 0 14px; max-width: 46ch; opacity: .85; }
.qr-support-waiting { opacity: .6; }
.qr-support-offline { margin: 0 0 24px; }
.qr-support-issue { margin-top: 40px; opacity: .85; }

.qr-support-grid :deep(gilde-contact),
.qr-support-grid :deep(gilde-support) { display: block; min-width: 0; }
</style>
