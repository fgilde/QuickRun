<script setup>
import { computed } from 'vue';
import { useData, withBase } from 'vitepress';

const { lang } = useData();
const de = computed(() => lang.value.startsWith('de'));
const home = computed(() => (de.value ? '/de/' : '/'));
const link = (path) => withBase(`${home.value}${path}`);

const t = computed(() => (de.value
  ? {
      eyebrow: 'Fragen',
      title: 'Was man vorher wissen will',
      lead: 'Kurze Antworten. Die langen stehen in der Dokumentation, jeweils verlinkt.',
      items: [
        {
          q: 'Führt QuickRun einfach fremden Code aus?',
          a: 'Nur nach Bestätigung. Ein Klick auf Run checkt das Repository aus und baut den Plan — '
            + 'ausgeführt wird nichts. Die Befehlsliste steht danach in einem Fenster der Erweiterung '
            + 'oder auf QuickRuns eigener Seite, nie in die GitHub-Seite injiziert, damit keine '
            + 'Webseite ein gefälschtes Panel darüber zeichnen kann. Erst dein Klick startet es.',
          more: { text: 'Sicherheitsmodell', href: 'security' },
        },
        {
          q: 'Was passiert ohne quickrun.yml?',
          a: 'QuickRun erkennt, was da ist: Docker Compose, npm-Skripte, .NET-Projekte inklusive '
            + 'Aspire und Desktop-Apps, Python (venv, uv, poetry, Django, Streamlit, Gradio), Rust, '
            + 'Go, Java, Procfile, .replit, Makefile, Taskfile, justfile und Pinokio-Skripte. Zum '
            + 'Kandidaten gehört auch der Port, auf den dann gewartet und der geöffnet wird.',
          more: { text: 'Ohne Config', href: 'no-config' },
        },
        {
          q: 'Wo landen die Checkouts?',
          a: 'In einem verwalteten Workspace unter deinem App-Data-Verzeichnis, ein Verzeichnis pro '
            + 'Repository und Ref. Der zweite Lauf desselben Refs benutzt es wieder, deshalb dauert er '
            + 'Sekunden. Im Tab Workspaces steht, was da liegt, wie groß es ist und wann es lief.',
          more: { text: 'Workspaces', href: 'cli' },
        },
        {
          q: 'Stoppt Stop wirklich alles?',
          a: 'Ja. Alles, was ein Lauf gestartet hat, hängt in einer Prozessgruppe — auch eine '
            + 'Anwendung, deren Elternprozess längst weg ist, und ein Server, den ein bereits '
            + 'beendeter Task im Hintergrund gestartet hat. Bleibt etwas übrig, zeigt der Lauf '
            + '„still running" und behält ein Stop, das es beendet.',
          more: { text: 'Läufe steuern', href: 'install' },
        },
        {
          q: 'Der Lauf bricht mit Datei- oder Port-Fehlern ab — warum?',
          a: 'Meist hält ein früherer Lauf noch etwas fest. Er wurde nicht gestoppt, sondern hart '
            + 'beendet — Task-Manager, Absturz, Abmelden —, denn ein Absturz von QuickRun nimmt '
            + 'absichtlich keine laufende Anwendung mit. Die alten Prozesse sperren dann ihre DLLs, '
            + 'sodass der Build scheitert, und halten weiter ihren Port. Antwortet dieser Port schon '
            + 'bevor der Task startet, sagt QuickRun das und nennt den Prozess samt PID — und wartet '
            + 'nicht auf diese Adresse: was ein Fremder antwortet, ist kein Beleg über diesen Lauf. '
            + 'Den genannten Prozess beenden, dann neu starten.',
          more: { text: 'Läufe steuern', href: 'install' },
        },
        {
          q: 'Braucht es Administratorrechte?',
          a: 'Nein. Autostart, der quickrun://-Handler und quickrun im PATH sind alle pro Benutzer: '
            + 'ein Registry-Wert unter HKCU, eine .desktop-Datei im Home, ein Launch Agent, ein '
            + 'Symlink in einem bin-Verzeichnis. Nichts wird systemweit installiert.',
          more: { text: 'Erster Start', href: 'install' },
        },
        {
          q: 'Warum warnt Windows oder macOS beim Start?',
          a: 'Die Binaries sind nicht signiert. Unter Windows erscheint deshalb einmal SmartScreen, '
            + 'unter macOS braucht die nackte Binary Rechtsklick → Öffnen. Über winget, Scoop oder '
            + 'Homebrew installiert, entfällt das.',
          more: { text: 'Download', href: 'download' },
        },
        {
          q: 'Sendet QuickRun irgendwas nach draußen?',
          a: 'Der Listener bindet auf 127.0.0.1. Nach außen gehen nur die Dinge, die du erwartest: '
            + 'git clone beim Repository, die Update-Prüfung bei GitHub, und was das Repository selbst '
            + 'herunterlädt. Kein Telemetrie-Endpunkt, keine Konten.',
          more: { text: 'Datenschutz', href: 'privacy' },
        },
        {
          q: 'Geht es auch ohne Browser-Erweiterung?',
          a: 'Ja. Das Fenster hat ein eigenes „Start a run": Repository eintippen, QuickRun schlägt '
            + 'Branches vor und stellt die schon gelaufenen nach oben. Oder quickrun run owner/repo '
            + 'im Terminal.',
          more: { text: 'Kommandozeile', href: 'cli' },
        },
      ],
      moreLabel: 'Mehr dazu',
      restTitle: 'Noch eine Frage?',
      restText: 'Issues sind der schnellste Weg — auch für „bei meinem Repository macht es X".',
      restCta: 'Issue aufmachen',
    }
  : {
      eyebrow: 'Questions',
      title: 'What people ask first',
      lead: 'Short answers. The long ones are in the documentation, linked from each.',
      items: [
        {
          q: "Does QuickRun just run other people's code?",
          a: 'Only after you approve it. Pressing Run checks the repository out and builds the plan - '
            + 'nothing is executed. The command list then appears in an extension window or on '
            + "QuickRun's own page, never injected into the GitHub page, so no web page can draw a "
            + 'convincing fake panel over it. Your click is what starts it.',
          more: { text: 'Security model', href: 'security' },
        },
        {
          q: 'What happens without a quickrun.yml?',
          a: 'QuickRun detects what is there: Docker Compose, npm scripts, .NET projects including '
            + 'Aspire and desktop apps, Python (venv, uv, poetry, Django, Streamlit, Gradio), Rust, Go, '
            + 'Java, Procfile, .replit, Makefile, Taskfile, justfile and Pinokio scripts. The candidate '
            + 'carries the port it should wait for and open, too.',
          more: { text: 'Without a config', href: 'no-config' },
        },
        {
          q: 'Where do the checkouts go?',
          a: 'Into a managed workspace under your app-data directory, one per repository and ref. A '
            + 'second run of the same ref reuses it, which is why it takes seconds. The Workspaces tab '
            + 'shows what is there, how big it is and when it last ran.',
          more: { text: 'Workspaces', href: 'cli' },
        },
        {
          q: 'Does Stop really stop everything?',
          a: 'Yes. Everything a run started belongs to one process group - including an application '
            + 'whose parent process is long gone, and a server that an already-finished task launched '
            + 'in the background. If anything is left, the run reads "still running" and keeps a Stop '
            + 'that ends it.',
          more: { text: 'Controlling runs', href: 'install' },
        },
        {
          q: 'The run fails on locked files or a busy port - why?',
          a: 'Usually an earlier run is still holding something. It was not stopped but killed - task '
            + 'manager, a crash, signing out - because a crash of QuickRun deliberately does not take '
            + 'a running application with it. Those processes then keep their DLLs locked, so the '
            + 'build fails, and keep their port. When that port already answers before the task '
            + 'starts, QuickRun says so and names the process and its pid - and does not wait on that '
            + 'address: what a stranger answers is no evidence about this run. End the process it '
            + 'names, then start again.',
          more: { text: 'Controlling runs', href: 'install' },
        },
        {
          q: 'Does it need administrator rights?',
          a: 'No. Autostart, the quickrun:// handler and quickrun on your PATH are all per-user: a '
            + 'registry value under HKCU, a .desktop file in your home, a launch agent, a symlink in a '
            + 'bin directory. Nothing is installed system-wide.',
          more: { text: 'First run', href: 'install' },
        },
        {
          q: 'Why does Windows or macOS warn me?',
          a: 'The binaries are unsigned, so Windows shows SmartScreen once and the bare macOS binary '
            + 'needs right-click → Open. Installing through winget, Scoop or Homebrew avoids both.',
          more: { text: 'Download', href: 'download' },
        },
        {
          q: 'Does QuickRun send anything anywhere?',
          a: 'The listener binds 127.0.0.1. The only outbound traffic is what you would expect: git '
            + 'clone to the repository, the update check to GitHub, and whatever the repository itself '
            + 'downloads. No telemetry endpoint, no accounts.',
          more: { text: 'Privacy', href: 'privacy' },
        },
        {
          q: 'Can I use it without the browser extension?',
          a: 'Yes. The window has its own "Start a run": type a repository and QuickRun suggests its '
            + 'branches, putting the ones you have run before at the top. Or run quickrun run '
            + 'owner/repo in a terminal.',
          more: { text: 'Command line', href: 'cli' },
        },
      ],
      moreLabel: 'More on this',
      restTitle: 'Another question?',
      restText: 'An issue is the fastest route - including "on my repository it does X".',
      restCta: 'Open an issue',
    }));
</script>

<template>
  <div class="qr-faq">
    <section class="qr-faq-head">
      <div class="m3-wrap">
        <span class="m3-label">{{ t.eyebrow }}</span>
        <h1 class="m3-display qr-faq-title">{{ t.title }}</h1>
        <p class="m3-body-lg qr-faq-lead">{{ t.lead }}</p>
      </div>
    </section>

    <section class="m3-wrap qr-faq-list">
      <details v-for="(item, at) in t.items" :key="item.q" class="m3-card qr-item" :open="at === 0">
        <summary>
          <span class="m3-title">{{ item.q }}</span>
          <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
            <path fill="currentColor" d="M7.4 8.6 12 13.2l4.6-4.6L18 10l-6 6-6-6 1.4-1.4Z"/>
          </svg>
        </summary>
        <p class="m3-body">{{ item.a }}</p>
        <a class="qr-item-more" :href="link(item.more.href)">
          {{ t.moreLabel }}: {{ item.more.text }} →
        </a>
      </details>

      <article class="m3-card m3-card--filled qr-rest">
        <h2 class="m3-title">{{ t.restTitle }}</h2>
        <p class="m3-body">{{ t.restText }}</p>
        <a class="m3-button" href="https://github.com/fgilde/QuickRun/issues/new"
           target="_blank" rel="noreferrer">{{ t.restCta }}</a>
      </article>
    </section>
  </div>
</template>

<style scoped>
.qr-faq-head { padding: 60px 0 26px; }
.qr-faq-title { margin-top: 12px; }
.qr-faq-lead { margin: 16px 0 0; max-width: 58ch; }

.qr-faq-list { display: grid; gap: 12px; padding: 20px 0 64px; }

.qr-item { padding: 0; }

.qr-item summary {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 20px 24px;
  cursor: pointer;
  list-style: none;
  color: var(--m3-on-surface);
}

.qr-item summary::-webkit-details-marker { display: none; }
.qr-item summary svg { flex: 0 0 22px; color: var(--m3-on-surface-variant); transition: transform .2s ease; }
.qr-item[open] summary svg { transform: rotate(180deg); }
.qr-item p { padding: 0 24px; max-width: 78ch; }

.qr-item-more {
  display: inline-block;
  margin: 14px 24px 22px;
  color: var(--m3-primary);
  font-size: .94rem;
  font-weight: 600;
  text-decoration: none;
}

.qr-item-more:hover { text-decoration: underline; }

.qr-rest { display: grid; gap: 12px; justify-items: start; margin-top: 18px; }
.qr-rest p { margin: 0; color: var(--m3-on-primary-container); opacity: .86; }
.qr-rest h2 { color: var(--m3-on-primary-container); }

@media (prefers-reduced-motion: reduce) {
  .qr-item summary svg { transition: none; }
}
</style>
