<script setup>
import { computed, nextTick, onMounted, ref } from 'vue';
import { useData } from 'vitepress';
import { loadConnect, skinAll } from './connect';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));

const ready = ref(false);
const failed = ref(false);

onMounted(async () => {
  const arrived = await loadConnect();

  if (!arrived) { failed.value = true; return; }

  ready.value = true;
  await nextTick();
  skinAll();
});

const ISSUES = 'https://github.com/fgilde/QuickRun/issues';

const t = computed(() => (de.value
  ? {
      eyebrow: 'Kontakt und Unterstützung',
      title: 'Schreib uns, oder halte QuickRun am Laufen',
      lead: 'QuickRun ist ein Ein-Personen-Projekt und quelloffen. Eine Nachricht kommt direkt bei '
        + 'mir an. Für Fehler ist ein Issue auf GitHub meist schneller, alles andere gern hier.',
      contact: 'Nachricht schreiben',
      contactText: 'Einfach Kontakt aufnehmen.',
      support: 'QuickRun unterstützen',
      supportText: 'Nichts davon wird erwartet. Es bezahlt die Domain, die Signaturen und die '
        + 'Abende, an denen die nächste Version entsteht.',
      issueTitle: 'Etwas kaputt?',
      issueText: 'Fehler und Vorschläge gehören auf GitHub. Dort steht auch, was schon gemeldet ist '
        + 'und woran gerade gearbeitet wird.',
      issueLink: 'Issue öffnen',
      loading: 'Wird geladen…',
      offline: 'Die Panels kommen von connect.gilde.org und ließen sich gerade nicht laden. '
        + 'Über GitHub geht es auch.',
    }
  : {
      eyebrow: 'Contact and support',
      title: 'Say something, or keep QuickRun going',
      lead: 'QuickRun is a one-person project and open source. A message here reaches me directly. '
        + 'For a bug an issue on GitHub is usually faster, anything else is welcome here.',
      contact: 'Send a message',
      contactText: 'Just get in touch.',
      support: 'Support QuickRun',
      supportText: 'None of this is expected. It pays for the domain, the signing certificates and '
        + 'the evenings the next version comes out of.',
      issueTitle: 'Something broken?',
      issueText: 'Bugs and ideas belong on GitHub. That is also where you can see what has been '
        + 'reported already and what is being worked on.',
      issueLink: 'Open an issue',
      loading: 'Loading…',
      offline: 'The panels come from connect.gilde.org and could not be loaded just now. '
        + 'GitHub works too.',
    }));
</script>

<template>
  <div class="qr-support">
    <header class="qr-support-head">
      <span class="m3-label">{{ t.eyebrow }}</span>
      <h1 class="m3-display qr-support-title">{{ t.title }}</h1>
      <p class="m3-body-lg qr-support-lead">{{ t.lead }}</p>
    </header>

    <div v-if="!failed" class="qr-support-grid">
      <section>
        <p class="m3-body qr-support-note">{{ t.contactText }}</p>
        <gilde-contact v-if="ready"
                       project="fgilde/QuickRun" widget="contact" :inline.attr="''" theme="auto"
                       :language.attr="de ? 'de' : 'en'" :title.attr="t.contact"
                       width="560" radius="16" padding="26"
                       show-logo="true" show-description="false" show-homepage="true"
                       show-preview-notice="false" show-footer="false" />
        <p v-else class="m3-body qr-support-waiting">{{ t.loading }}</p>
      </section>

      <section>
        <p class="m3-body qr-support-note">{{ t.supportText }}</p>
        <gilde-support v-if="ready"
                       project="fgilde/QuickRun" widget="support" :inline.attr="''" theme="auto"
                       :language.attr="de ? 'de' : 'en'" :title.attr="t.support"
                       width="560" radius="16" padding="26"
                       show-logo="true" show-description="false" show-homepage="true"
                       show-preview-notice="false" show-footer="false"
                       show-support-hint="false" support-layout="rows"
                       show-support-icons="true" show-support-qr="true" />
        <p v-else class="m3-body qr-support-waiting">{{ t.loading }}</p>
      </section>
    </div>

    <p v-else class="m3-body qr-support-note">{{ t.offline }}</p>

    <aside class="m3-card qr-support-issue">
      <h2 class="m3-title">{{ t.issueTitle }}</h2>
      <p class="m3-body">{{ t.issueText }}</p>
      <a class="m3-button" :href="ISSUES" target="_blank" rel="noreferrer">{{ t.issueLink }}</a>
    </aside>
  </div>
</template>

<style scoped>
.qr-support { max-width: 1120px; margin: 0 auto; padding: 56px 20px 88px; }

.qr-support-head { margin-bottom: 44px; }
.qr-support-title { margin: 10px 0 14px; max-width: 26ch; }
.qr-support-lead { margin: 0; max-width: 62ch; color: var(--m3-on-surface-variant); }

.qr-support-grid {
  display: grid; gap: 32px; align-items: start;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 340px), 1fr));
}

.qr-support-note { margin: 0 0 16px; max-width: 46ch; color: var(--m3-on-surface-variant); }

/* Side by side the two panels should start on the same line, and the text above them is one
   sentence on one side and three on the other. */
@media (min-width: 760px) {
  .qr-support-grid .qr-support-note { min-height: 4.6em; }
}
.qr-support-waiting { opacity: .6; }

.qr-support-grid :deep(gilde-contact),
.qr-support-grid :deep(gilde-support) { display: block; min-width: 0; }

.qr-support-issue { margin-top: 56px; display: grid; justify-items: start; gap: 10px; }
.qr-support-issue h2, .qr-support-issue p { margin: 0; }
.qr-support-issue p { max-width: 58ch; color: var(--m3-on-surface-variant); }
.qr-support-issue .m3-button { margin-top: 6px; }
</style>
