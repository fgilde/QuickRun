<script setup>
import { computed } from 'vue';
import { useData, withBase } from 'vitepress';
import Gallery from './Gallery.vue';
import { shots } from './shots.js';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));
const home = computed(() => (de.value ? '/de/' : '/'));
const link = (path) => withBase(`${home.value}${path}`);

const gallery = computed(() => shots(de.value));

const t = computed(() => (de.value
  ? {
      eyebrow: 'Windows · macOS · Linux · quelloffen',
      headline: 'Fremdes Repository. Ein Klick. Es läuft.',
      lead: 'QuickRun klont, prüft die Werkzeuge, fragt die Werte ab, startet die Tasks und öffnet die '
        + 'Adresse — von der GitHub-Seite aus, auf der du es gefunden hast. Ohne die Setup-Doku zu lesen.',
      primary: 'Herunterladen',
      secondary: 'Dokumentation',
      heroNote: 'Oder direkt im Terminal:',
      trust: [
        'Kein Adminrecht nötig',
        'Nichts läuft ohne Bestätigung',
        'Config optional',
        'Ein einzelnes Binary',
      ],
      featuresLabel: 'Was es macht',
      featuresTitle: 'Vom Repository zur laufenden Anwendung',
      features: [
        {
          icon: 'bolt',
          title: 'Ein Klick auf GitHub',
          text: 'Die Erweiterung setzt einen Run-Button neben den Branch-Dropdown, in PR-Header und in '
            + 'jede Zeile der Branch-Liste. Läuft der Branch schon, bietet derselbe Button Stop, die '
            + 'Adresse und das Log an.',
        },
        {
          icon: 'shield',
          title: 'Erst zeigen, dann laufen',
          text: 'Vor dem Start steht die Befehlsliste in einem Fenster, das keine Webseite überlagern '
            + 'kann. Bestätigt wird genau das, was dann läuft — Secrets kommen nie zur Seite zurück.',
        },
        {
          icon: 'wand',
          title: 'Auch ohne Config',
          text: 'Kein quickrun.yml? QuickRun erkennt Docker Compose, npm-Skripte, .NET, Python, Rust, '
            + 'Go, Java, Procfile, Pinokio und mehr — inklusive Port, auf den es dann wartet.',
        },
        {
          icon: 'code',
          title: 'Config-Builder',
          text: 'Editor mit Schema-Vervollständigung, Prüfung durch denselben Parser wie ein Lauf, '
            + 'Testlauf gegen das echte Repository — und Stop, ohne den Tab zu verlassen.',
        },
        {
          icon: 'stop',
          title: 'Stop, das wirklich stoppt',
          text: 'Alles, was ein Lauf gestartet hat, hängt in einer Gruppe: auch die Anwendung, deren '
            + 'Elternprozess längst weg ist. Bleibt etwas übrig, sagt es das — und beendet es.',
        },
        {
          icon: 'terminal',
          title: 'Fenster oder Terminal',
          text: 'Tray-Icon mit lokaler UI, oder quickrun run owner/repo in der Shell. Dieselben Läufe, '
            + 'dieselben Workspaces, dieselbe Bestätigung.',
        },
      ],
      stepsLabel: 'In drei Schritten',
      stepsTitle: 'Einmal einrichten, dann klicken',
      steps: [
        {
          title: 'QuickRun installieren',
          text: 'Ein Binary, kein Installer nötig. Optional Autostart und quickrun im PATH — beides '
            + 'ein Schalter in den Einstellungen.',
          code: 'winget install fgilde.QuickRun',
        },
        {
          title: 'Erweiterung laden',
          text: 'Chrome, Edge, Firefox oder Opera. Sie redet nur mit 127.0.0.1 und wird an ihrer '
            + 'Herkunft erkannt, die keine Webseite fälschen kann.',
          code: null,
        },
        {
          title: 'Run klicken',
          text: 'Plan lesen, bestätigen, zusehen. Die Adresse öffnet sich, wenn die Anwendung steht.',
          code: 'quickrun run owner/repo',
        },
      ],
      galleryLabel: 'Screenshots',
      galleryTitle: 'Die lokale Oberfläche',
      galleryText: 'Alles aus einem echten Lauf eines echten Repositories. Zum Vergrößern klicken.',
      galleryMore: 'Alle Screenshots',
      securityLabel: 'Sicherheit',
      securityTitle: 'Fremder Code, mit Absicht sichtbar',
      securityText: 'QuickRun startet Code aus Repositories, die du nicht geschrieben hast. Deshalb ist '
        + 'die Bestätigung nicht überspringbar, die Befehlsliste steht in einem Fenster außerhalb der '
        + 'Seite, jede Zeile aus einem Repository wird als Text behandelt und nie als HTML, und der '
        + 'Listener auf 127.0.0.1 nimmt nur Aufrufe an, deren Herkunft der Browser selbst setzt.',
      securityLink: 'Was genau geprüft wird',
      ctaTitle: 'Bereit?',
      ctaText: 'Ein Binary, ein Klick, und das Repository läuft.',
      ctaPrimary: 'Download',
      ctaSecondary: 'Auf GitHub ansehen',
    }
  : {
      eyebrow: 'Windows · macOS · Linux · open source',
      headline: "Someone else's repository. One click. Running.",
      lead: 'QuickRun clones it, checks the tools, asks for the values, starts the tasks and opens the '
        + 'address - from the GitHub page you found it on, without reading a line of setup documentation.',
      primary: 'Download',
      secondary: 'Documentation',
      heroNote: 'Or straight from a terminal:',
      trust: [
        'No administrator rights',
        'Nothing runs unconfirmed',
        'Config optional',
        'A single binary',
      ],
      featuresLabel: 'What it does',
      featuresTitle: 'From a repository to a running application',
      features: [
        {
          icon: 'bolt',
          title: 'One click on GitHub',
          text: 'The extension puts a Run button next to the branch dropdown, in pull request headers '
            + 'and on every row of the branch list. While that branch is running, the same button '
            + 'offers Stop, the address, and the log.',
        },
        {
          icon: 'shield',
          title: 'Shown first, run second',
          text: 'The command list appears in a window a web page cannot overlay. What you approve is '
            + 'what runs, and a secret is never sent back to the page that asked for it.',
        },
        {
          icon: 'wand',
          title: 'Works without a config',
          text: 'No quickrun.yml? QuickRun detects Docker Compose, npm scripts, .NET, Python, Rust, Go, '
            + 'Java, Procfile, Pinokio and more - including the port it should then wait for.',
        },
        {
          icon: 'code',
          title: 'Config builder',
          text: 'An editor with schema completion, checked by the same parser a run uses, and a test '
            + 'run against the real repository - with a Stop that works, without leaving the tab.',
        },
        {
          icon: 'stop',
          title: 'A Stop that stops',
          text: 'Everything a run started belongs to one group, including an application whose parent '
            + 'process is long gone. If something is left running, it says so - and ends it.',
        },
        {
          icon: 'terminal',
          title: 'A window or a terminal',
          text: 'A tray icon with a local UI, or quickrun run owner/repo in a shell. The same runs, the '
            + 'same workspaces, the same confirmation.',
        },
      ],
      stepsLabel: 'Three steps',
      stepsTitle: 'Set it up once, then click',
      steps: [
        {
          title: 'Install QuickRun',
          text: 'One binary, no installer required. Autostart and quickrun on your PATH are optional - '
            + 'each is a switch in Settings.',
          code: 'winget install fgilde.QuickRun',
        },
        {
          title: 'Load the extension',
          text: 'Chrome, Edge, Firefox or Opera. It talks only to 127.0.0.1 and is recognised by its '
            + 'origin, which no web page can forge.',
          code: null,
        },
        {
          title: 'Press Run',
          text: 'Read the plan, approve it, watch it. The address opens once the application is up.',
          code: 'quickrun run owner/repo',
        },
      ],
      galleryLabel: 'Screenshots',
      galleryTitle: 'The local interface',
      galleryText: 'All from a real run of a real repository. Click to enlarge.',
      galleryMore: 'Every screenshot',
      securityLabel: 'Security',
      securityTitle: 'Code you did not write, visible on purpose',
      securityText: 'QuickRun runs code from repositories you did not write. So the confirmation cannot '
        + 'be skipped, the command list lives in a window outside the page, every line that comes from '
        + 'a repository is treated as text and never as HTML, and the listener on 127.0.0.1 accepts only '
        + 'callers whose origin the browser itself sets.',
      securityLink: 'What exactly is guarded',
      ctaTitle: 'Ready?',
      ctaText: 'One binary, one click, and the repository is running.',
      ctaPrimary: 'Download',
      ctaSecondary: 'View on GitHub',
    }));

const icons = {
  bolt: 'M13 2 4.5 13.5H11l-1 8.5L19.5 10H13l0-8Z',
  shield: 'M12 2 4 5v6.1c0 5 3.4 9.7 8 10.9 4.6-1.2 8-5.9 8-10.9V5l-8-3Zm-1 14-3.5-3.5L9 11l2 2 4.5-4.5 1.5 1.5L11 16Z',
  wand: 'M7.5 5.6 6 2 4.5 5.6.9 7.1l3.6 1.5L6 12.2l1.5-3.6 3.6-1.5-3.6-1.5Zm9.9 2.9-1.9 1.9 3.1 3.1 1.9-1.9-3.1-3.1ZM3 19.1 13.1 9l1.4 1.4L4.4 20.5 3 19.1Zm14.6-.6L16 22l-1.6-3.5L11 17l3.5-1.6L16 12l1.6 3.4L21 17l-3.4 1.5Z',
  code: 'M9.4 16.6 4.8 12l4.6-4.6L8 6l-6 6 6 6 1.4-1.4Zm5.2 0L19.2 12l-4.6-4.6L16 6l6 6-6 6-1.4-1.4Z',
  stop: 'M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 18a8 8 0 1 1 0-16 8 8 0 0 1 0 16ZM8.5 8.5h7v7h-7v-7Z',
  terminal: 'M3 3h18v18H3V3Zm2 2v14h14V5H5Zm2.3 3.3 1.4-1.4L12 10.2l-3.3 3.3-1.4-1.4L9.2 10 7.3 8.3ZM12 15h5v-1.6h-5V15Z',
};
</script>

<template>
  <div class="qr-landing">
    <!-- hero -->
    <section class="qr-hero">
      <div class="qr-hero-glow" aria-hidden="true"></div>
      <div class="m3-wrap qr-hero-inner">
        <div class="qr-hero-copy">
          <span class="m3-label">{{ t.eyebrow }}</span>
          <h1 class="m3-display">{{ t.headline }}</h1>
          <p class="m3-body-lg qr-lead">{{ t.lead }}</p>

          <div class="qr-hero-actions">
            <a class="m3-button" :href="link('get')">
              <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true">
                <path fill="currentColor" d="M12 16 6 10l1.4-1.4L11 12.2V3h2v9.2l3.6-3.6L18 10l-6 6Zm-7 3h14v2H5v-2Z"/>
              </svg>
              {{ t.primary }}
            </a>
            <a class="m3-button m3-button--tonal" :href="link('install')">{{ t.secondary }}</a>
          </div>

          <p class="qr-hero-note">{{ t.heroNote }}</p>
          <code class="qr-hero-code m3-code">quickrun run owner/repo</code>

          <ul class="qr-trust">
            <li v-for="item in t.trust" :key="item" class="m3-chip">
              <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true">
                <path fill="currentColor" d="M9.6 16.6 5 12l1.4-1.4 3.2 3.2 8-8L19 7.2l-9.4 9.4Z"/>
              </svg>
              {{ item }}
            </li>
          </ul>
        </div>

        <div class="qr-hero-shot">
          <img :src="withBase('/screenshots/runs.png')" alt="" loading="eager" decoding="async">
        </div>
      </div>
    </section>

    <!-- features -->
    <section id="features" class="m3-section">
      <div class="m3-wrap">
        <span class="m3-label">{{ t.featuresLabel }}</span>
        <h2 class="m3-headline qr-section-title">{{ t.featuresTitle }}</h2>

        <div class="qr-features">
          <article v-for="feature in t.features" :key="feature.title" class="m3-card qr-feature">
            <span class="qr-feature-icon">
              <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
                <path fill="currentColor" :d="icons[feature.icon]"/>
              </svg>
            </span>
            <h3 class="m3-title">{{ feature.title }}</h3>
            <p class="m3-body">{{ feature.text }}</p>
          </article>
        </div>
      </div>
    </section>

    <!-- how it works -->
    <section class="m3-section qr-steps-section">
      <div class="m3-wrap">
        <span class="m3-label">{{ t.stepsLabel }}</span>
        <h2 class="m3-headline qr-section-title">{{ t.stepsTitle }}</h2>

        <ol class="qr-steps">
          <li v-for="(step, at) in t.steps" :key="step.title">
            <span class="qr-step-number">{{ at + 1 }}</span>
            <div>
              <h3 class="m3-title">{{ step.title }}</h3>
              <p class="m3-body">{{ step.text }}</p>
              <code v-if="step.code" class="m3-code qr-step-code">{{ step.code }}</code>
            </div>
          </li>
        </ol>
      </div>
    </section>

    <!-- screenshots -->
    <section class="m3-section">
      <div class="m3-wrap">
        <span class="m3-label">{{ t.galleryLabel }}</span>
        <h2 class="m3-headline qr-section-title">{{ t.galleryTitle }}</h2>
        <p class="m3-body qr-section-lead">{{ t.galleryText }}</p>

        <Gallery :shots="gallery" />

        <p class="qr-gallery-more">
          <a class="m3-button m3-button--outlined" :href="link('tour')">{{ t.galleryMore }}</a>
        </p>
      </div>
    </section>

    <!-- security -->
    <section class="m3-section qr-security-section">
      <div class="m3-wrap qr-security">
        <div>
          <span class="m3-label">{{ t.securityLabel }}</span>
          <h2 class="m3-headline qr-section-title">{{ t.securityTitle }}</h2>
          <p class="m3-body-lg">{{ t.securityText }}</p>
          <p><a class="m3-button m3-button--tonal" :href="link('security')">{{ t.securityLink }}</a></p>
        </div>
        <div class="qr-security-mark" aria-hidden="true">
          <img :src="withBase('/icon.png')" alt="" width="180" height="180">
        </div>
      </div>
    </section>

    <!-- call to action -->
    <section class="qr-cta">
      <div class="m3-wrap qr-cta-inner">
        <div>
          <h2 class="m3-headline">{{ t.ctaTitle }}</h2>
          <p class="m3-body qr-cta-text">{{ t.ctaText }}</p>
        </div>
        <div class="qr-cta-actions">
          <a class="m3-button" :href="link('get')">{{ t.ctaPrimary }}</a>
          <a class="m3-button m3-button--outlined" href="https://github.com/fgilde/QuickRun"
             target="_blank" rel="noreferrer">{{ t.ctaSecondary }}</a>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.qr-landing { overflow-x: clip; }

/* --- hero --- */
.qr-hero { position: relative; padding: 72px 0 40px; }

.qr-hero-glow {
  position: absolute;
  inset: -180px 0 auto 0;
  height: 620px;
  background:
    radial-gradient(46% 60% at 18% 30%, color-mix(in srgb, var(--m3-brand-lavender) 48%, transparent), transparent 70%),
    radial-gradient(42% 54% at 78% 12%, color-mix(in srgb, var(--m3-brand-periwinkle) 40%, transparent), transparent 70%);
  pointer-events: none;
}

.qr-hero-inner {
  position: relative;
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1.02fr);
  gap: 56px;
  align-items: center;
}

.qr-lead { margin: 18px 0 26px; max-width: 52ch; }
.qr-hero-copy .m3-display { margin-top: 14px; }
.qr-hero-actions { display: flex; flex-wrap: wrap; gap: 12px; }
.qr-hero-note { margin: 26px 0 8px; font-size: .86rem; color: var(--m3-on-surface-variant); }

.qr-hero-code {
  display: inline-block;
  padding: 10px 16px;
  border-radius: var(--m3-radius-full);
  background: var(--m3-surface-container-high);
  border: 1px solid var(--m3-outline-variant);
  color: var(--m3-on-surface);
}

.qr-trust { display: flex; flex-wrap: wrap; gap: 8px; margin: 26px 0 0; padding: 0; list-style: none; }

.qr-hero-shot {
  padding: 12px;
  border-radius: var(--m3-radius-l);
  background: linear-gradient(150deg,
    color-mix(in srgb, var(--m3-brand-lavender) 40%, var(--m3-surface-container)),
    color-mix(in srgb, var(--m3-brand-periwinkle) 30%, var(--m3-surface-container)));
  box-shadow: var(--m3-elevation-3);
}

.qr-hero-shot img {
  display: block;
  width: 100%;
  height: auto;
  border-radius: var(--m3-radius-s);
  background: var(--m3-surface);
}

/* --- sections --- */
.qr-section-title { margin: 10px 0 0; }
.qr-section-lead { margin: 12px 0 26px; max-width: 62ch; }

.qr-features {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 18px;
  margin-top: 30px;
}

.qr-feature { display: grid; gap: 10px; align-content: start; }

.qr-feature-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border-radius: var(--m3-radius-s);
  background: var(--m3-primary-container);
  color: var(--m3-on-primary-container);
}

.qr-steps-section { background: var(--m3-surface-container-low); }

.qr-steps {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(270px, 1fr));
  gap: 22px;
  margin: 30px 0 0;
  padding: 0;
  list-style: none;
  counter-reset: none;
}

.qr-steps li { display: flex; gap: 14px; align-items: flex-start; }

.qr-step-number {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 34px;
  width: 34px;
  height: 34px;
  border-radius: var(--m3-radius-full);
  background: var(--m3-primary);
  color: var(--m3-on-primary);
  font-weight: 700;
}

.qr-steps h3 { margin-bottom: 6px; }

.qr-step-code {
  display: inline-block;
  margin-top: 12px;
  padding: 8px 12px;
  border-radius: var(--m3-radius-xs);
  background: var(--m3-surface-container-high);
  border: 1px solid var(--m3-outline-variant);
}

.qr-gallery-more { margin: 26px 0 0; }

.qr-security-section { background: var(--m3-surface-container-low); }

.qr-security {
  display: grid;
  grid-template-columns: minmax(0, 1.6fr) minmax(0, .7fr);
  gap: 40px;
  align-items: center;
}

.qr-security p { margin-top: 14px; }
.qr-security-mark { display: grid; place-items: center; }
.qr-security-mark img { width: min(180px, 40vw); height: auto; }

.qr-cta { padding: 56px 0 72px; }

.qr-cta-inner {
  display: flex;
  flex-wrap: wrap;
  gap: 24px;
  align-items: center;
  justify-content: space-between;
  padding: 34px 38px;
  border-radius: var(--m3-radius-l);
  background: linear-gradient(120deg,
    color-mix(in srgb, var(--m3-brand-lavender) 42%, var(--m3-surface-container)),
    color-mix(in srgb, var(--m3-brand-periwinkle) 34%, var(--m3-surface-container)));
}

.qr-cta-text { margin-top: 8px; }
.qr-cta-actions { display: flex; flex-wrap: wrap; gap: 12px; }

@media (max-width: 940px) {
  .qr-hero { padding-top: 44px; }
  .qr-hero-inner { grid-template-columns: 1fr; gap: 34px; }
  .qr-security { grid-template-columns: 1fr; }
  .qr-security-mark { display: none; }
}
</style>
