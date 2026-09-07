<script setup>
import { computed, onMounted, ref } from 'vue';
import { useData } from 'vitepress';

import EmbedPlayground from './EmbedPlayground.vue';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));

/** The real components, loaded the way the snippet below tells everyone else to load them. */
const loaded = ref(false);

onMounted(() => {
  if (customElements.get('quickrun-btn')) { loaded.value = true; return; }

  const tag = document.createElement('script');
  tag.type = 'module';
  tag.src = '/components.js';
  tag.onload = () => { loaded.value = true; };
  document.head.append(tag);
});

const SOURCE = 'https://quickrun.org/components.js';
const install = `<script type="module" src="${SOURCE}"><\/script>`;

const copied = ref('');

async function copy(text, which) {
  try {
    await navigator.clipboard.writeText(text);
    copied.value = which;
    setTimeout(() => { copied.value = ''; }, 1600);
  } catch {
    copied.value = '';
  }
}

/**
 * The six of them, each with the snippet that produces exactly what is beside it.
 *
 * Live rather than pictured: every demo here is the element itself, on this page, talking to the
 * QuickRun on the reader's own machine. A screenshot of a button would be the one thing this page
 * cannot afford to be.
 */
const pieces = computed(() => [
  {
    id: 'btn',
    name: '<quickrun-btn>',
    what: de.value
      ? 'Der Button. Ein Klick öffnet QuickRuns Fenster mit dem Plan für dein Repository.'
      : 'The button. A click opens QuickRun’s window with the plan for your repository.',
    code: '<quickrun-btn repo="fgilde/WebDataStudio">'
      + (de.value ? 'Starten' : 'Run this') + '</quickrun-btn>',
  },
  {
    id: 'cfg',
    name: '<quickrun-btn run-cfg="…">',
    what: de.value
      ? 'Mit einer bestimmten Config: collection, ein Pfad im Repository oder eine https-Adresse. '
        + 'Nie ein Befehl — QuickRun liest die Datei selbst und zeigt sie vor dem Start.'
      : 'With a config named: collection, a path inside the repository, or an https address. Never '
        + 'a command — QuickRun reads the file itself and shows it before anything starts.',
    code: '<quickrun-btn repo="passbolt/passbolt_api" run-cfg="collection" icon="right">'
      + (de.value ? 'Demo starten' : 'Run the demo') + '</quickrun-btn>',
  },
  {
    id: 'badge',
    name: '<quickrun-badge>',
    what: de.value
      ? 'Das README-Badge als Element. Standard ist der Link auf quickrun.org; mode="run" übergibt '
        + 'direkt, und der Link darunter funktioniert weiterhin.'
      : 'The README badge as an element. The default is the link to quickrun.org; mode="run" hands '
        + 'over directly, and the link underneath still works.',
    code: '<quickrun-badge repo="fgilde/QuickRun" mode="run"></quickrun-badge>',
  },
  {
    id: 'status',
    name: '<quickrun-status>',
    what: de.value
      ? 'Eine Zeile: läuft QuickRun hier, und in welcher Version.'
      : 'One line: is QuickRun here, and which version.',
    code: '<quickrun-status></quickrun-status>',
  },
  {
    id: 'gate',
    name: '<quickrun-gate>',
    what: de.value
      ? 'Zwei Slots, und der Ping entscheidet, welchen der Leser sieht. Damit lässt sich der Rest '
        + 'der Seite für einen Fall schreiben statt für beide gleichzeitig.'
      : 'Two slots, and the ping decides which one a reader sees — so the rest of the page can '
        + 'be written for one case instead of hedged for both.',
    code: '<quickrun-gate>\n'
      + `  <p slot="running">${de.value ? 'QuickRun ist da — ein Klick genügt.' : 'QuickRun is here — one click and it runs.'}</p>\n`
      + `  <p slot="missing">${de.value ? 'Noch kein QuickRun? Einmal herunterladen.' : 'No QuickRun yet? One download.'}</p>\n`
      + '</quickrun-gate>',
  },
  {
    id: 'get',
    name: '<quickrun-get>',
    what: de.value
      ? 'Der Download, benannt nach dem Rechner, der die Seite liest. Immer auf unsere '
        + 'Download-Seite, nie direkt auf eine Datei.'
      : 'The download, named for the machine reading the page. Always our download page, never a '
        + 'file directly.',
    code: '<quickrun-get only-when-missing></quickrun-get>',
  },
  {
    id: 'config',
    name: '<quickrun-config>',
    what: de.value
      ? 'Was laufen würde, im Klartext: die quickrun.yml des Repositories oder die, die QuickRun '
        + 'dafür hält. Beide sind öffentlich, also kann die Seite es zeigen statt um Vertrauen zu bitten.'
      : 'What would run, in plain text: the repository’s own quickrun.yml, or the one QuickRun '
        + 'keeps for it. Both are public, so a page can show it instead of asking for trust.',
    code: '<quickrun-config repo="passbolt/passbolt_api" run-cfg="collection"></quickrun-config>',
    wide: true,
  },
]);

const t = computed(() => (de.value
  ? {
      eyebrow: 'Für die eigene Seite',
      title: 'QuickRun einbinden',
      lead: 'Eine Zeile lädt die Komponenten, eine zweite ist der Button. Kein Build, keine '
        + 'Abhängigkeit, immer die aktuelle Fassung — und ein Klick landet in QuickRuns eigenem '
        + 'Fenster, wo der Plan auf eine Bestätigung wartet.',
      install: 'Einmal einbinden',
      copy: 'Kopieren',
      copied: 'Kopiert',
      piecesTitle: 'Die Komponenten',
      piecesLead: 'Alles hier ist echt: die Elemente laufen auf dieser Seite und reden mit dem '
        + 'QuickRun auf deinem Rechner.',
      playTitle: 'Bauen und stylen',
      playLead: 'Attribute und Farben einstellen, Vorschau ansehen, Code kopieren.',
      docs: 'Alle Attribute, Events und Sicherheitsregeln stehen in der Dokumentation:',
      docsLink: 'Web-Komponenten',
      whereTitle: 'Button, Badge oder Link?',
      loading: 'components.js wird geladen…',
      run: 'Starten',
    }
  : {
      eyebrow: 'For your own page',
      title: 'Embed QuickRun',
      lead: 'One line loads the components, a second one is the button. No build step, no '
        + 'dependency, always the current version — and a click ends in QuickRun’s own '
        + 'window, where the plan waits to be confirmed.',
      install: 'Load it once',
      copy: 'Copy',
      copied: 'Copied',
      piecesTitle: 'The components',
      piecesLead: 'Everything here is real: the elements run on this page and talk to the QuickRun '
        + 'on your machine.',
      playTitle: 'Build it and style it',
      playLead: 'Set the attributes and the colours, watch the preview, copy the code.',
      docs: 'Every attribute, every event and the security rules are in the documentation:',
      docsLink: 'Web components',
      whereTitle: 'Button, badge or link?',
      loading: 'loading components.js…',
      run: 'Run this',
    }));

/** Where each of the three belongs, which is the question a repository owner actually has. */
const places = computed(() => (de.value
  ? [
      ['README auf GitHub', 'das Badge als Bild in einem Link',
        'GitHub führt keine Skripte aus und entfernt unbekannte Link-Schemata. Bleibt Standard.'],
      ['Eigene Seite', '<quickrun-btn>',
        'Ein Klick öffnet direkt das Fenster, und die Seite kann auf die Events reagieren.'],
      ['Link im Text, Chat', 'quickrun.org/run?repo=…',
        'Funktioniert überall, wo ein Link funktioniert.'],
    ]
  : [
      ['A README on GitHub', 'the badge, an image in a link',
        'GitHub runs no scripts and strips unknown link schemes. Stays the default.'],
      ['Your own page', '<quickrun-btn>',
        'A click opens the window directly, and the page can react to the events.'],
      ['A link in text, a chat', 'quickrun.org/run?repo=…',
        'Works anywhere a link works.'],
    ]));
</script>

<template>
  <div class="qr-embed">
    <span class="m3-label">{{ t.eyebrow }}</span>
    <h1 class="m3-display qr-embed-title">{{ t.title }}</h1>
    <p class="m3-body-lg qr-embed-lead">{{ t.lead }}</p>

    <section class="m3-card qr-embed-install">
      <div class="qr-embed-head">
        <h2 class="m3-title">{{ t.install }}</h2>
        <button class="m3-button m3-button--text" type="button" @click="copy(install, 'install')">
          {{ copied === 'install' ? t.copied : t.copy }}
        </button>
      </div>
      <pre class="m3-code"><code>{{ install }}</code></pre>
    </section>

    <h2 class="m3-headline qr-embed-h">{{ t.piecesTitle }}</h2>
    <p class="m3-body qr-embed-sub">{{ t.piecesLead }}</p>

    <div class="qr-embed-grid">
      <article v-for="piece in pieces" :key="piece.id"
               class="m3-card qr-embed-piece" :class="{ wide: piece.wide }">
        <h3 class="qr-embed-name"><code>{{ piece.name }}</code></h3>
        <p class="m3-body qr-embed-what">{{ piece.what }}</p>

        <div class="qr-embed-stage">
          <p v-if="!loaded" class="qr-embed-waiting">{{ t.loading }}</p>

          <template v-else-if="piece.id === 'btn'">
            <quickrun-btn repo="fgilde/WebDataStudio">{{ t.run }}</quickrun-btn>
          </template>

          <template v-else-if="piece.id === 'cfg'">
            <quickrun-btn repo="passbolt/passbolt_api" run-cfg="collection" icon="right">
              {{ de ? 'Demo starten' : 'Run the demo' }}
            </quickrun-btn>
          </template>

          <quickrun-badge v-else-if="piece.id === 'badge'" repo="fgilde/QuickRun" mode="run" />
          <quickrun-status v-else-if="piece.id === 'status'" />

          <quickrun-gate v-else-if="piece.id === 'gate'">
            <p slot="running">
              {{ de ? 'QuickRun ist da — ein Klick genügt.' : 'QuickRun is here — one click and it runs.' }}
            </p>
            <p slot="missing">
              {{ de ? 'Noch kein QuickRun? Einmal herunterladen.' : 'No QuickRun yet? One download.' }}
            </p>
          </quickrun-gate>

          <quickrun-get v-else-if="piece.id === 'get'" />

          <quickrun-config v-else-if="piece.id === 'config'"
                           repo="passbolt/passbolt_api" run-cfg="collection" />
        </div>

        <div class="qr-embed-head">
          <button class="m3-button m3-button--text" type="button"
                  @click="copy(piece.code, piece.id)">
            {{ copied === piece.id ? t.copied : t.copy }}
          </button>
        </div>
        <pre class="m3-code"><code>{{ piece.code }}</code></pre>
      </article>
    </div>

    <h2 class="m3-headline qr-embed-h">{{ t.playTitle }}</h2>
    <p class="m3-body qr-embed-sub">{{ t.playLead }}</p>

    <EmbedPlayground />

    <h2 class="m3-headline qr-embed-h">{{ t.whereTitle }}</h2>
    <div class="qr-embed-places">
      <article v-for="[where, what, why] in places" :key="where" class="m3-card qr-embed-place">
        <strong>{{ where }}</strong>
        <code>{{ what }}</code>
        <p class="m3-body">{{ why }}</p>
      </article>
    </div>

    <p class="m3-body qr-embed-docs">
      {{ t.docs }}
      <a :href="de ? '/de/components' : '/components'">{{ t.docsLink }}</a>
    </p>
  </div>
</template>

<style scoped>
.qr-embed { max-width: 1080px; margin: 0 auto; padding: 40px 20px 72px; }
.qr-embed-title { margin: 8px 0 12px; }
.qr-embed-lead { max-width: 62ch; margin: 0 0 32px; }

.qr-embed-h { margin: 44px 0 6px; }
.qr-embed-sub { max-width: 62ch; margin: 0 0 18px; opacity: .8; }

.qr-embed-install { padding: 18px 20px; }

.qr-embed-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.qr-embed-head h2 { margin: 0; }

.qr-embed pre {
  margin: 10px 0 0; padding: 12px 14px; overflow-x: auto;
  border-radius: 10px; font-size: 12.5px; line-height: 1.55;
}

.qr-embed-grid {
  display: grid; gap: 16px;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 320px), 1fr));
}

.qr-embed-piece { padding: 18px 20px; display: flex; flex-direction: column; }
.qr-embed-piece.wide { grid-column: 1 / -1; }

.qr-embed-name { margin: 0 0 6px; font-size: 14px; }
.qr-embed-what { margin: 0 0 14px; font-size: 13.5px; opacity: .85; }

.qr-embed-stage {
  display: flex; align-items: center; justify-content: center;
  min-height: 96px; padding: 18px; margin-bottom: 6px;
  border: 1px dashed currentColor; border-radius: 12px; opacity: 1;
  border-color: color-mix(in srgb, currentColor 22%, transparent);
}

.qr-embed-piece.wide .qr-embed-stage { display: block; }

.qr-embed-waiting { margin: 0; font-size: 12.5px; opacity: .6; }

.qr-embed-places {
  display: grid; gap: 14px; margin-top: 6px;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 260px), 1fr));
}

.qr-embed-place { padding: 16px 18px; }
.qr-embed-place strong { display: block; margin-bottom: 6px; }
.qr-embed-place code { font-size: 12.5px; }
.qr-embed-place p { margin: 8px 0 0; font-size: 13px; opacity: .82; }

.qr-embed-docs { margin-top: 36px; }
</style>
