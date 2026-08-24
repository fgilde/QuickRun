<script setup>
import { computed, onMounted, ref } from 'vue';

const props = defineProps({
  lang: { type: String, default: 'en' },
});

// Release assets carry no version in their names, precisely so these links can be permanent:
// releases/latest/download/<name> needs the exact file name, and the version lives in the tag.
const base = 'https://github.com/fgilde/QuickRun/releases/latest/download';

const detected = ref(null);
const copied = ref('');

const t = computed(() => (props.lang === 'de'
  ? {
      yours: 'Für dein System',
      pick: 'Andere Plattform wählen',
      recommended: 'Empfohlen',
      direct: 'Direkter Download',
      manager: 'Mit Paketmanager',
      copy: 'Kopieren',
      copied: 'Kopiert',
      arch: { x64: 'Intel/AMD 64-bit', arm64: 'ARM64' },
      pending: 'Prüfung läuft',
      unpacked: 'Entpackt laden',
      note: 'Binaries sind nicht signiert. Unter macOS ist Homebrew deshalb der empfohlene Weg.',
    }
  : {
      yours: 'For your system',
      pick: 'Choose another platform',
      recommended: 'Recommended',
      direct: 'Direct download',
      manager: 'With a package manager',
      copy: 'Copy',
      copied: 'Copied',
      arch: { x64: 'Intel/AMD 64-bit', arm64: 'ARM64' },
      pending: 'review pending',
      unpacked: 'Load unpacked',
      note: 'Binaries are unsigned. On macOS, Homebrew is the recommended path for that reason.',
    }));

const platforms = [
  {
    os: 'windows',
    name: 'Windows',
    tint: '#0078d4',
    command: 'winget install fgilde.QuickRun',
    alternative: 'scoop install https://fgilde.github.io/QuickRun/quickrun.json',
    builds: [
      { arch: 'x64', asset: 'quickrun-win-x64.zip' },
      { arch: 'arm64', asset: 'quickrun-win-arm64.zip' },
    ],
  },
  {
    os: 'macos',
    name: 'macOS',
    tint: '#a2aaad',
    command: 'brew install fgilde/tap/quickrun',
    builds: [
      { arch: 'arm64', asset: 'quickrun-osx-arm64.tar.gz', label: 'Apple silicon' },
      { arch: 'x64', asset: 'quickrun-osx-x64.tar.gz', label: 'Intel' },
    ],
  },
  {
    os: 'linux',
    name: 'Linux',
    tint: '#f0b400',
    command: 'curl -fsSL https://fgilde.github.io/QuickRun/install.sh | sh',
    builds: [
      { arch: 'x64', asset: 'quickrun-linux-x64.tar.gz' },
      { arch: 'arm64', asset: 'quickrun-linux-arm64.tar.gz' },
    ],
  },
];

const mine = computed(() => platforms.find((p) => p.os === detected.value) ?? null);
const others = computed(() => platforms.filter((p) => p.os !== detected.value));

onMounted(() => { detected.value = detect(); });

function detect() {
  const ua = navigator.userAgent;
  const platform = navigator.userAgentData?.platform ?? navigator.platform ?? '';

  if (/Win/i.test(platform) || /Windows/i.test(ua)) return 'windows';
  if (/Mac/i.test(platform) || /Mac OS X/i.test(ua)) return 'macos';
  if (/Linux|X11/i.test(platform) || /Linux/i.test(ua)) return 'linux';
  return null;
}

async function copy(text) {
  try {
    await navigator.clipboard.writeText(text);
    copied.value = text;
    setTimeout(() => { copied.value = ''; }, 1600);
  } catch {
    // Clipboard permission denied: the command is on screen and selectable anyway.
  }
}

function label(build) {
  return build.label ?? t.value.arch[build.arch] ?? build.arch;
}
</script>

<template>
  <div class="qr-download">
    <!-- Detected platform first: one obvious thing to do. -->
    <section v-if="mine" class="qr-mine" :style="{ '--tint': mine.tint }">
      <header>
        <svg class="qr-glyph" viewBox="0 0 24 24" aria-hidden="true">
          <!-- Geometric glyphs, not brand logos: a badly redrawn logo looks worse than none. -->
          <template v-if="mine.os === 'windows'">
            <rect x="3" y="3" width="8" height="8" /><rect x="13" y="3" width="8" height="8" />
            <rect x="3" y="13" width="8" height="8" /><rect x="13" y="13" width="8" height="8" />
          </template>
          <template v-else-if="mine.os === 'macos'">
            <path d="M8 3a3 3 0 0 1 3 3v3H8a3 3 0 1 1 0-6Zm8 0a3 3 0 1 1 0 6h-3V6a3 3 0 0 1 3-3ZM8 21a3 3 0 1 1 0-6h3v3a3 3 0 0 1-3 3Zm8 0a3 3 0 0 1-3-3v-3h3a3 3 0 1 1 0 6ZM9 9h6v6H9V9Z"
              fill="none" stroke="currentColor" stroke-width="1.7" />
          </template>
          <template v-else>
            <rect x="2.5" y="4" width="19" height="16" rx="2.5" fill="none" stroke="currentColor" stroke-width="1.7" />
            <path d="M7 9.5 10 12l-3 2.5M12.5 15h4.5" fill="none" stroke="currentColor"
              stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" />
          </template>
        </svg>
        <div>
          <span class="qr-eyebrow">{{ t.yours }}</span>
          <h3>{{ mine.name }}</h3>
        </div>
      </header>

      <div class="qr-command">
        <span class="qr-tag">{{ t.recommended }}</span>
        <code>{{ mine.command }}</code>
        <button type="button" @click="copy(mine.command)">
          {{ copied === mine.command ? t.copied : t.copy }}
        </button>
      </div>

      <div v-if="mine.alternative" class="qr-command qr-secondary">
        <code>{{ mine.alternative }}</code>
        <button type="button" @click="copy(mine.alternative)">
          {{ copied === mine.alternative ? t.copied : t.copy }}
        </button>
      </div>

      <div class="qr-builds">
        <span class="qr-eyebrow">{{ t.direct }}</span>
        <a v-for="build in mine.builds" :key="build.asset" :href="`${base}/${build.asset}`">
          {{ label(build) }}
        </a>
      </div>
    </section>

    <!-- Everything else, still one click away. -->
    <section class="qr-others">
      <span class="qr-eyebrow">{{ mine ? t.pick : t.direct }}</span>
      <div class="qr-grid">
        <article v-for="platform in others" :key="platform.os" :style="{ '--tint': platform.tint }">
          <h4>{{ platform.name }}</h4>
          <code>{{ platform.command }}</code>
          <div class="qr-links">
            <a v-for="build in platform.builds" :key="build.asset" :href="`${base}/${build.asset}`">
              {{ label(build) }}
            </a>
          </div>
        </article>
      </div>
    </section>

    <p class="qr-note">{{ t.note }}</p>
  </div>
</template>

<style scoped>
.qr-download { margin: 26px 0; }

.qr-eyebrow {
  display: block;
  font-size: 11px;
  letter-spacing: .08em;
  text-transform: uppercase;
  color: var(--vp-c-text-3);
  margin-bottom: 4px;
}

/* --- detected platform --- */
.qr-mine {
  padding: 22px;
  border: 1px solid var(--vp-c-divider);
  border-left: 4px solid var(--tint);
  border-radius: 12px;
  background: var(--vp-c-bg-soft);
}

.qr-mine header { display: flex; align-items: center; gap: 14px; margin-bottom: 16px; }
.qr-mine h3 { margin: 0; font-size: 20px; line-height: 1.2; }

.qr-glyph { width: 34px; height: 34px; flex: 0 0 34px; fill: var(--tint); color: var(--tint); }

.qr-command {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  background: var(--vp-c-bg);
}

.qr-command + .qr-command { margin-top: 8px; }
.qr-secondary { opacity: .82; }

.qr-command code {
  flex: 1;
  min-width: 0;
  overflow-x: auto;
  white-space: nowrap;
  background: none;
  padding: 0;
  font-size: 13px;
}

.qr-tag {
  flex: 0 0 auto;
  font-size: 10.5px;
  letter-spacing: .06em;
  text-transform: uppercase;
  padding: 2px 7px;
  border-radius: 10px;
  background: var(--tint);
  color: #fff;
  font-weight: 600;
}

.qr-command button {
  flex: 0 0 auto;
  padding: 4px 10px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 6px;
  background: var(--vp-c-bg-soft);
  color: var(--vp-c-text-1);
  font-size: 12px;
  cursor: pointer;
}

.qr-command button:hover { border-color: var(--tint); color: var(--tint); }

.qr-builds { margin-top: 16px; }
.qr-builds a {
  display: inline-block;
  margin: 0 8px 6px 0;
  padding: 5px 12px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 999px;
  font-size: 13px;
  text-decoration: none;
  color: var(--vp-c-text-1);
}
.qr-builds a:hover { border-color: var(--tint); color: var(--tint); }

/* --- other platforms --- */
.qr-others { margin-top: 26px; }

.qr-grid {
  display: grid;
  gap: 12px;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
}

.qr-grid article {
  padding: 14px;
  border: 1px solid var(--vp-c-divider);
  border-top: 3px solid var(--tint);
  border-radius: 10px;
  background: var(--vp-c-bg-soft);
}

.qr-grid h4 { margin: 0 0 8px; font-size: 15px; }

.qr-grid code {
  display: block;
  overflow-x: auto;
  white-space: nowrap;
  font-size: 12px;
  padding: 6px 8px;
}

.qr-links { margin-top: 10px; display: flex; flex-wrap: wrap; gap: 6px; }
.qr-links a { font-size: 12.5px; }

.qr-note { margin-top: 18px; font-size: 13px; color: var(--vp-c-text-2); }
</style>
