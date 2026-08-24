<script setup>
import { computed, onMounted, ref } from 'vue';

const props = defineProps({
  lang: { type: String, default: 'en' },
});

// GitHub serves releases/latest/download as a permanent redirect, so these links never carry a
// version and can never go stale.
const base = 'https://github.com/fgilde/QuickRun/releases/latest/download';
const releases = 'https://github.com/fgilde/QuickRun/releases/latest';

const detected = ref(null);

const copy = computed(() =>
  props.lang === 'de'
    ? {
        heading: 'QuickRun herunterladen',
        detectedNote: 'Für dein System erkannt',
        others: 'Andere Plattformen',
        preferred: 'Empfohlen: Paketmanager',
        allAssets: 'Alle Dateien dieser Version',
        unsigned:
          'Die Binaries sind nicht signiert. macOS blockiert einen direkt geladenen Download, '
          + 'deshalb ist Homebrew dort der empfohlene Weg.',
      }
    : {
        heading: 'Download QuickRun',
        detectedNote: 'Detected for your system',
        others: 'Other platforms',
        preferred: 'Recommended: package managers',
        allAssets: 'All files in this release',
        unsigned:
          'Binaries are unsigned. macOS blocks a directly downloaded binary, which is why Homebrew '
          + 'is the recommended path there.',
      });

// Release assets carry no version in their names, precisely so that these links can be stable:
// releases/latest/download/<name> needs the exact file name, and the version lives in the tag.
const platforms = [
  { id: 'win-x64', label: 'Windows (x64)', asset: 'quickrun-win-x64.zip' },
  { id: 'win-arm64', label: 'Windows (ARM64)', asset: 'quickrun-win-arm64.zip' },
  { id: 'osx-arm64', label: 'macOS (Apple silicon)', asset: 'quickrun-osx-arm64.tar.gz' },
  { id: 'osx-x64', label: 'macOS (Intel)', asset: 'quickrun-osx-x64.tar.gz' },
  { id: 'linux-x64', label: 'Linux (x64)', asset: 'quickrun-linux-x64.tar.gz' },
  { id: 'linux-arm64', label: 'Linux (ARM64)', asset: 'quickrun-linux-arm64.tar.gz' },
];

const detectedPlatform = computed(() => platforms.find((p) => p.id === detected.value));
const otherPlatforms = computed(() => platforms.filter((p) => p.id !== detected.value));

onMounted(() => {
  detected.value = detect();
});

function detect() {
  const ua = navigator.userAgent;
  const platform = navigator.userAgentData?.platform ?? navigator.platform ?? '';
  const arm = /arm|aarch64/i.test(ua) || /arm/i.test(platform);

  if (/Win/i.test(platform) || /Windows/i.test(ua)) return arm ? 'win-arm64' : 'win-x64';
  // Apple silicon Macs still report Intel in the user agent, so the architecture cannot be read
  // from the browser at all. Apple silicon is the right default, and the Intel build is one click
  // away below.
  if (/Mac/i.test(platform) || /Mac OS X/i.test(ua)) return 'osx-arm64';
  if (/Linux/i.test(platform) || /Linux/i.test(ua)) return arm ? 'linux-arm64' : 'linux-x64';
  return null;
}
</script>

<template>
  <div class="qr-download">
    <h3>{{ copy.heading }}</h3>

    <a v-if="detectedPlatform" class="qr-primary" :href="`${base}/${detectedPlatform.asset}`">
      <span class="qr-label">{{ detectedPlatform.label }}</span>
      <span class="qr-note">{{ copy.detectedNote }}</span>
    </a>

    <details class="qr-others">
      <summary>{{ detectedPlatform ? copy.others : copy.heading }}</summary>
      <ul>
        <li v-for="platform in otherPlatforms" :key="platform.id">
          <a :href="`${base}/${platform.asset}`">{{ platform.label }}</a>
        </li>
      </ul>
      <p><a :href="releases">{{ copy.allAssets }}</a></p>
    </details>

    <p class="qr-unsigned">{{ copy.unsigned }}</p>
  </div>
</template>

<style scoped>
.qr-download {
  margin: 24px 0;
  padding: 20px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 12px;
  background: var(--vp-c-bg-soft);
}

.qr-download h3 {
  margin: 0 0 12px;
  font-size: 16px;
}

.qr-primary {
  display: inline-flex;
  flex-direction: column;
  gap: 2px;
  padding: 10px 18px;
  border-radius: 8px;
  background: var(--vp-c-brand-1);
  color: var(--vp-c-white);
  font-weight: 600;
  text-decoration: none;
}

.qr-primary:hover { background: var(--vp-c-brand-2); }

.qr-primary .qr-note {
  font-size: 12px;
  font-weight: 400;
  opacity: .85;
}

.qr-others { margin-top: 14px; }
.qr-others summary { cursor: pointer; color: var(--vp-c-brand-1); }
.qr-others ul { margin: 8px 0 0; }

.qr-unsigned {
  margin: 14px 0 0;
  font-size: 13px;
  color: var(--vp-c-text-2);
}
</style>
