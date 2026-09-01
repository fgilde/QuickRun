<script setup>
/**
 * A config, shown as what it is: the commands somebody is about to approve.
 *
 * Highlighted here rather than by an editor. Monaco is what QuickRun's own window uses, and loading
 * an editor to read forty lines would cost more than the page it sits on - so YAML gets a small
 * pass of its own: keys, strings, comments, and the run lines picked out, because those are the
 * lines that matter.
 *
 * Every part of the config is rendered as text. It comes from a repository or from a collection, so
 * it is content, never markup - that rule is what keeps a config from putting a script on this page.
 */
import { computed, ref } from 'vue';

const props = defineProps({
  yaml: { type: String, default: '' },
  source: { type: String, default: '' },
  runLabel: { type: String, default: 'Run' },
  copyLabel: { type: String, default: 'Copy' },
  copiedLabel: { type: String, default: 'Copied' },
});

const emit = defineEmits(['run']);

/** One line, split into the pieces that get a colour. Text only - see the note above. */
function pieces(line) {
  const comment = line.match(/^(\s*)(#.*)$/);
  if (comment) return [{ kind: 'indent', text: comment[1] }, { kind: 'comment', text: comment[2] }];

  const entry = line.match(/^(\s*-?\s*)([A-Za-z_][\w.-]*)(:)(\s*)(.*)$/);

  if (entry) {
    const [, indent, key, colon, gap, rest] = entry;
    return [
      { kind: 'indent', text: indent },
      { kind: key === 'run' || key === 'repository' ? 'key-strong' : 'key', text: key },
      { kind: 'punct', text: colon },
      { kind: 'indent', text: gap },
      { kind: rest.startsWith('#') ? 'comment' : 'value', text: rest },
    ];
  }

  return [{ kind: 'plain', text: line }];
}

const lines = computed(() =>
  (props.yaml ?? '').replace(/\r\n/g, '\n').split('\n').map((line, index) => ({
    number: index + 1,
    parts: pieces(line),
  })));

const copied = ref(false);

async function copy() {
  try {
    await navigator.clipboard.writeText(props.yaml);
    copied.value = true;
    setTimeout(() => { copied.value = false; }, 1600);
  } catch {
    // No clipboard permission: the text is on screen and selectable, which is the fallback.
  }
}
</script>

<template>
  <div class="qr-config">
    <div class="qr-config-bar">
      <span v-if="source" class="m3-label qr-config-source">{{ source }}</span>
      <span class="qr-config-spacer"></span>
      <button class="m3-button m3-button--text" type="button" @click="copy">
        {{ copied ? copiedLabel : copyLabel }}
      </button>
      <button class="m3-button" type="button" @click="emit('run')">{{ runLabel }}</button>
    </div>

    <pre class="qr-config-code"><code><span v-for="line in lines" :key="line.number" class="qr-config-line"><span
      class="qr-config-number">{{ line.number }}</span><span v-for="(part, at) in line.parts" :key="at"
      :class="`qr-config-${part.kind}`">{{ part.text }}</span>
</span></code></pre>
  </div>
</template>

<style scoped>
.qr-config { display: flex; flex-direction: column; min-height: 0; }

.qr-config-bar {
  display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
  padding: 0 0 10px;
}
.qr-config-source { opacity: 0.75; }
.qr-config-spacer { flex: 1; }

.qr-config-code {
  margin: 0; padding: 12px 14px; overflow: auto; min-height: 0;
  border-radius: 10px; border: 1px solid var(--vp-c-divider);
  background: var(--vp-code-block-bg, rgba(127, 127, 127, 0.08));
  font-size: 12.5px; line-height: 1.55; tab-size: 2;
}
.qr-config-code code { white-space: pre; }

.qr-config-line { display: block; }
.qr-config-number {
  display: inline-block; width: 2.6em; margin-right: 12px;
  text-align: right; opacity: 0.35; user-select: none;
}

.qr-config-key { color: var(--vp-c-brand-1, #7f7fff); }
.qr-config-key-strong { color: var(--vp-c-brand-1, #7f7fff); font-weight: 700; }
.qr-config-punct { opacity: 0.6; }
.qr-config-comment { opacity: 0.55; font-style: italic; }
.qr-config-value { color: var(--vp-c-text-1); }
.qr-config-plain { color: var(--vp-c-text-2); }
</style>
