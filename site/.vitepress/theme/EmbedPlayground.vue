<script setup>
import { computed, onMounted, reactive, ref, watchEffect } from 'vue';
import { useData } from 'vitepress';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));

/**
 * The real components, loaded the way a page out there loads them.
 *
 * A script tag rather than an import, because that is the line the snippet below tells people to
 * paste - so if that line ever stops working, it stops working here first.
 */
const loaded = ref(false);

onMounted(() => {
  if (customElements.get('quickrun-btn')) {
    loaded.value = true;
    return;
  }

  const tag = document.createElement('script');
  tag.type = 'module';
  tag.src = '/components.js';
  tag.onload = () => { loaded.value = true; };
  document.head.append(tag);
});

const SOURCE = 'https://quickrun.org/components.js';

/** Everything the button reads, with the value it uses when the attribute is absent. */
const DEFAULTS = {
  repo: 'fgilde/QuickRun',
  ref: '',
  pr: '',
  'run-cfg': '',
  icon: 'left',
  label: '',
  mode: 'window',
};

/** The custom properties, with the values the stylesheet inside the component starts from. */
const LOOK = {
  '--quickrun-bg': '#6d4ac4',
  '--quickrun-fg': '#ffffff',
  '--quickrun-border': 'transparent',
  '--quickrun-radius': '8px',
  '--quickrun-padding': '8px 14px',
  '--quickrun-gap': '8px',
  '--quickrun-size': '14px',
  '--quickrun-weight': '600',
  '--quickrun-icon-size': '0.85em',
};

const attributes = reactive({ ...DEFAULTS });
const look = reactive({ ...LOOK });
const unstyled = ref(false);

/** Ready-made looks, because nobody wants to guess nine values to see what is possible. */
const presets = computed(() => [
  { name: 'QuickRun', look: {} },
  {
    name: de.value ? 'Umrandet' : 'Outline',
    look: {
      '--quickrun-bg': 'transparent',
      '--quickrun-fg': '#6d4ac4',
      '--quickrun-border': '#6d4ac4',
    },
  },
  {
    name: de.value ? 'Pille' : 'Pill',
    look: { '--quickrun-radius': '999px', '--quickrun-padding': '10px 20px' },
  },
  {
    name: de.value ? 'GitHub-grün' : 'GitHub green',
    look: { '--quickrun-bg': '#1f883d', '--quickrun-radius': '6px', '--quickrun-weight': '500' },
  },
  {
    name: de.value ? 'Groß' : 'Large',
    look: { '--quickrun-size': '18px', '--quickrun-padding': '14px 26px', '--quickrun-radius': '12px' },
  },
]);

function apply(preset) {
  Object.assign(look, LOOK, preset.look);
  unstyled.value = false;
}

/** Only what differs from the default, so the snippet stays the shortest thing that works. */
const chosen = computed(() => Object.entries(attributes)
  .filter(([key, value]) => value && value !== DEFAULTS[key]));

const styling = computed(() => Object.entries(look).filter(([key, value]) => value !== LOOK[key]));

/**
 * The preview button, dressed by hand.
 *
 * Not v-bind: "ref" is reserved in a Vue template, and the whole point of the preview is that it
 * carries the same attributes as the snippet - ref among them. So the snippet decides, and this
 * writes exactly that onto the element.
 */
const button = ref(null);

onMounted(() => watchEffect(() => {
  const element = button.value;
  if (!element) return;

  for (const [key, value] of Object.entries(attributes)) {
    if (value && value !== DEFAULTS[key]) element.setAttribute(key, value);
    else element.removeAttribute(key);
  }

  // icon and mode have a default the component itself applies, so leaving them off is right - but
  // the preview should show the chosen one even when it is the default.
  element.setAttribute('repo', attributes.repo);
  element.setAttribute('icon', attributes.icon);

  for (const [key, value] of Object.entries(LOOK)) {
    if (look[key] !== value) element.style.setProperty(key, look[key]);
    else element.style.removeProperty(key);
  }
}));

const snippet = computed(() => {
  const parts = chosen.value.map(([key, value]) => `${key}="${value}"`);
  if (unstyled.value) parts.push('unstyled');

  if (styling.value.length) {
    parts.push(`style="${styling.value.map(([key, value]) => `${key}: ${value}`).join('; ')}"`);
  }

  const attrs = parts.length ? `\n  ${parts.join('\n  ')}` : '';
  const text = attributes.label ? '' : (de.value ? 'Starten' : 'Run this');

  return `<script type="module" src="${SOURCE}"><\/script>\n\n`
    + `<quickrun-btn${attrs}>${text}</quickrun-btn>`;
});

const copied = ref(false);

async function copy() {
  await navigator.clipboard.writeText(snippet.value);
  copied.value = true;
  setTimeout(() => { copied.value = false; }, 1600);
}

/**
 * What the button tells the page around it.
 *
 * This panel is the demonstration: a page can watch the events, and it can cancel quickrun-run to
 * put something of its own in front of the hand-over. It is not a plan and not a log - those live
 * in QuickRun's own window, where they cannot be faked by a page.
 */
const seen = ref([]);
const intercept = ref(false);

function note(event) {
  const detail = event.detail ?? {};
  const time = new Date().toLocaleTimeString();

  const said = event.type === 'quickrun-status'
    ? (detail.running
      ? (de.value ? `läuft, Version ${detail.version ?? '?'}` : `running, version ${detail.version ?? '?'}`)
      : (de.value ? 'antwortet hier nicht' : 'not answering here'))
    : event.type === 'quickrun-handover'
      ? (detail.how === 'window'
        ? (de.value ? 'Fenster direkt geöffnet' : 'opened the window directly')
        : (de.value ? 'über quickrun:// übergeben' : 'handed over through quickrun://'))
      : JSON.stringify(detail.target);

  seen.value = [{ time, type: event.type, said }, ...seen.value].slice(0, 8);
}

function onRun(event) {
  note(event);

  if (intercept.value) {
    event.preventDefault();
    seen.value = [{
      time: new Date().toLocaleTimeString(),
      type: 'preventDefault()',
      said: de.value ? 'die Seite hat die Übergabe abgebrochen' : 'the page stopped the hand-over',
    }, ...seen.value].slice(0, 8);
  }
}

const t = computed(() => (de.value
  ? {
      what: 'Der Button', repo: 'Repository', ref: 'Branch oder Tag', pr: 'Pull Request',
      cfg: 'Config (run-cfg)', label: 'Beschriftung', icon: 'Icon', mode: 'Beim Klick',
      left: 'links', right: 'rechts', none: 'keins',
      window: 'QuickRun-Fenster öffnen', link: 'auf quickrun.org verlinken',
      look: 'Aussehen', presets: 'Vorlagen', unstyled: 'Ohne mitgeliefertes CSS (unstyled)',
      unstyledHint: 'Nur noch nackte Elemente: styl’ sie über ::part(button), ::part(icon) und '
        + '::part(label) selbst.',
      preview: 'Vorschau', copy: 'Code kopieren', copied: 'Kopiert',
      events: 'Events', clear: 'Leeren',
      interceptLabel: 'quickrun-run abfangen (preventDefault)',
      empty: 'Klick den Button in der Vorschau — was dabei passiert, steht hier.',
      loading: 'components.js wird geladen…',
      status: 'Und dasselbe als Textzeile:',
      cfgHint: 'collection, ein Pfad im Repository (what/quickrun.yml) oder eine https-Adresse.',
    }
  : {
      what: 'The button', repo: 'Repository', ref: 'Branch or tag', pr: 'Pull request',
      cfg: 'Config (run-cfg)', label: 'Label', icon: 'Icon', mode: 'On a click',
      left: 'left', right: 'right', none: 'none',
      window: 'open the QuickRun window', link: 'link to quickrun.org',
      look: 'Look', presets: 'Presets', unstyled: 'Without the bundled CSS (unstyled)',
      unstyledHint: 'Bare elements from here on: style them yourself through ::part(button), '
        + '::part(icon) and ::part(label).',
      preview: 'Preview', copy: 'Copy the code', copied: 'Copied',
      events: 'Events', clear: 'Clear',
      interceptLabel: 'intercept quickrun-run (preventDefault)',
      empty: 'Press the button in the preview — what happens appears here.',
      loading: 'loading components.js…',
      status: 'And the same thing as a line of text:',
      cfgHint: 'collection, a path inside the repository (what/quickrun.yml), or an https address.',
    }));
</script>

<template>
  <div class="pg">
    <div class="pg-controls">
      <section>
        <h3>{{ t.what }}</h3>

        <label>{{ t.repo }}
          <input v-model="attributes.repo" placeholder="owner/repo" spellcheck="false">
        </label>

        <div class="pg-two">
          <label>{{ t.ref }}
            <input v-model="attributes.ref" placeholder="main" spellcheck="false">
          </label>
          <label>{{ t.pr }}
            <input v-model="attributes.pr" placeholder="42" spellcheck="false">
          </label>
        </div>

        <label>{{ t.cfg }}
          <input v-model="attributes['run-cfg']" placeholder="collection" spellcheck="false">
          <small>{{ t.cfgHint }}</small>
        </label>

        <label>{{ t.label }}
          <input v-model="attributes.label" :placeholder="de ? 'Starten' : 'Run this'">
        </label>

        <label>{{ t.icon }}
          <div class="pg-choice">
            <button
              v-for="side in ['left', 'right', 'none']"
              :key="side"
              type="button"
              :class="{ on: attributes.icon === side }"
              @click="attributes.icon = side"
            >{{ t[side] }}</button>
          </div>
        </label>

        <label>{{ t.mode }}
          <select v-model="attributes.mode">
            <option value="window">{{ t.window }}</option>
            <option value="link">{{ t.link }}</option>
          </select>
        </label>
      </section>

      <section>
        <h3>{{ t.look }}</h3>

        <div class="pg-presets">
          <button v-for="preset in presets" :key="preset.name" type="button" @click="apply(preset)">
            {{ preset.name }}
          </button>
        </div>

        <div class="pg-two">
          <label>--quickrun-bg
            <input v-model="look['--quickrun-bg']" type="text" spellcheck="false">
          </label>
          <label>--quickrun-fg
            <input v-model="look['--quickrun-fg']" type="text" spellcheck="false">
          </label>
        </div>

        <div class="pg-two">
          <label>--quickrun-border
            <input v-model="look['--quickrun-border']" type="text" spellcheck="false">
          </label>
          <label>--quickrun-radius
            <input v-model="look['--quickrun-radius']" type="text" spellcheck="false">
          </label>
        </div>

        <div class="pg-two">
          <label>--quickrun-padding
            <input v-model="look['--quickrun-padding']" type="text" spellcheck="false">
          </label>
          <label>--quickrun-gap
            <input v-model="look['--quickrun-gap']" type="text" spellcheck="false">
          </label>
        </div>

        <div class="pg-two">
          <label>--quickrun-size
            <input v-model="look['--quickrun-size']" type="text" spellcheck="false">
          </label>
          <label>--quickrun-weight
            <input v-model="look['--quickrun-weight']" type="text" spellcheck="false">
          </label>
        </div>

        <label>--quickrun-icon-size
          <input v-model="look['--quickrun-icon-size']" type="text" spellcheck="false">
        </label>

        <label class="pg-check">
          <input v-model="unstyled" type="checkbox">
          <span>{{ t.unstyled }}</span>
        </label>
        <small v-if="unstyled">{{ t.unstyledHint }}</small>
      </section>
    </div>

    <div class="pg-result">
      <section class="pg-stage">
        <h3>{{ t.preview }}</h3>

        <div class="pg-frame">
          <p v-if="!loaded" class="pg-empty">{{ t.loading }}</p>
          <quickrun-btn
            v-else
            ref="button"
            :key="`${unstyled}`"
            @quickrun-run="onRun"
            @quickrun-handover="note"
            @quickrun-status="note"
          >{{ de ? 'Starten' : 'Run this' }}</quickrun-btn>
        </div>

        <p class="pg-note">{{ t.status }}</p>
        <div class="pg-frame">
          <quickrun-status v-if="loaded" />
        </div>
      </section>

      <section class="pg-code">
        <div class="pg-head">
          <h3>HTML</h3>
          <button type="button" class="pg-copy" @click="copy">
            {{ copied ? t.copied : t.copy }}
          </button>
        </div>
        <pre><code>{{ snippet }}</code></pre>
      </section>

      <section class="pg-events">
        <div class="pg-head">
          <h3>{{ t.events }}</h3>
          <button v-if="seen.length" type="button" class="pg-copy" @click="seen = []">
            {{ t.clear }}
          </button>
        </div>

        <label class="pg-check">
          <input v-model="intercept" type="checkbox">
          <span>{{ t.interceptLabel }}</span>
        </label>

        <p v-if="!seen.length" class="pg-empty">{{ t.empty }}</p>
        <ul v-else>
          <li v-for="(line, index) in seen" :key="index">
            <code>{{ line.type }}</code>
            <span>{{ line.said }}</span>
            <time>{{ line.time }}</time>
          </li>
        </ul>
      </section>
    </div>
  </div>
</template>

<style scoped>
.pg {
  display: grid;
  gap: 20px;
  grid-template-columns: minmax(0, 1fr);
  margin: 24px 0 8px;
}

@media (min-width: 900px) {
  .pg { grid-template-columns: minmax(0, 320px) minmax(0, 1fr); align-items: start; }
}

.pg-controls, .pg-result { display: grid; gap: 16px; align-content: start; }

.pg section {
  border: 1px solid var(--vp-c-divider);
  border-radius: 12px;
  padding: 16px;
  background: var(--vp-c-bg-soft);
}

.pg h3 { margin: 0 0 12px; font-size: 13px; text-transform: uppercase; letter-spacing: .06em; opacity: .7; }

.pg label { display: block; font-size: 13px; margin-bottom: 12px; }
.pg label:last-child { margin-bottom: 0; }

.pg input[type="text"], .pg input:not([type]), .pg select {
  display: block;
  width: 100%;
  margin-top: 4px;
  padding: 7px 9px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  background: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  font: inherit;
  font-size: 13px;
}

.pg small { display: block; margin-top: 4px; font-size: 11px; opacity: .65; }

.pg-two { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }

.pg-choice, .pg-presets { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 6px; }

.pg-choice button, .pg-presets button, .pg-copy {
  padding: 5px 11px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 999px;
  background: var(--vp-c-bg);
  color: var(--vp-c-text-1);
  font-size: 12px;
  cursor: pointer;
}

.pg-choice button.on { border-color: var(--vp-c-brand-1); color: var(--vp-c-brand-1); }
.pg-choice button:hover, .pg-presets button:hover, .pg-copy:hover { border-color: var(--vp-c-brand-1); }

.pg-check { display: flex; align-items: center; gap: 8px; }
.pg-check input { width: auto; margin: 0; }

.pg-frame {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 92px;
  padding: 20px;
  border: 1px dashed var(--vp-c-divider);
  border-radius: 10px;
  background: var(--vp-c-bg);
}

.pg-note { margin: 14px 0 6px; font-size: 12px; opacity: .7; }

.pg-head { display: flex; align-items: baseline; justify-content: space-between; gap: 12px; }
.pg-head h3 { margin-bottom: 12px; }

.pg-code pre {
  margin: 0;
  padding: 12px 14px;
  border-radius: 10px;
  background: var(--vp-c-bg);
  border: 1px solid var(--vp-c-divider);
  overflow-x: auto;
  font-size: 12.5px;
  line-height: 1.55;
}

.pg-empty { margin: 8px 0 0; font-size: 12.5px; opacity: .6; }

.pg-events ul { margin: 10px 0 0; padding: 0; list-style: none; display: grid; gap: 6px; }

.pg-events li {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  gap: 10px;
  align-items: baseline;
  font-size: 12.5px;
  padding: 6px 0;
  border-top: 1px solid var(--vp-c-divider);
}

.pg-events code { font-size: 11.5px; }
.pg-events time { opacity: .5; font-size: 11px; }
</style>
