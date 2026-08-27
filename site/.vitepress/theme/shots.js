// The screenshots, and what each one is showing.
//
// Captured from real runs of real repositories against released builds - the first two on GitHub
// itself, the rest by scripts/capture-screenshots.mjs. Nothing here is a mockup, which is the
// point: what the landing page shows is what you get.

export function shots(de) {
  return de
    ? [
        {
          file: 'github-button.png',
          title: 'Der Button, wo du ihn brauchst',
          text: 'Neben dem Branch-Dropdown auf GitHub. Kein Klonen, kein Terminal, keine Setup-Doku.',
        },
        {
          file: 'github-confirm.png',
          title: 'Jeder Befehl, bevor er läuft',
          text: 'Repository, Ref, Commit und die vollständige Befehlsliste — in einem Fenster, das keine Webseite überlagern kann.',
        },
        {
          file: 'runs.png',
          title: 'Was läuft',
          text: 'Fortschritt, jeder Task mit Zustand, Adresse und Prozess-ID, und das Log im Zulauf.',
        },
        {
          file: 'plan.png',
          title: 'Erst der Plan',
          text: 'Die Befehle, die laufen würden, plus die Werte, die die Config abfragt. Nichts startet ohne Bestätigung.',
        },
        {
          file: 'builder.png',
          title: 'Config-Builder',
          text: 'Editor mit Schema-Vervollständigung, serverseitige Prüfung, Testlauf gegen das Repository.',
        },
        {
          file: 'settings.png',
          title: 'Einstellungen',
          text: 'Autostart, quickrun im Terminal, und was die Kommandozeile kann — alles pro Benutzer.',
        },
        {
          file: 'workspaces.png',
          title: 'Workspaces',
          text: 'Was ausgecheckt ist, wie viel Platz es braucht, wann es zuletzt lief.',
        },
      ]
    : [
        {
          file: 'github-button.png',
          title: 'The button where you need it',
          text: 'Next to the branch dropdown on GitHub. No clone, no terminal, no setup documentation.',
        },
        {
          file: 'github-confirm.png',
          title: 'Every command, before it runs',
          text: 'Repository, ref, commit and the full command list - in a window no web page can draw over.',
        },
        {
          file: 'runs.png',
          title: 'What is running',
          text: 'Progress, every task with its state, address and process id, and the log as it arrives.',
        },
        {
          file: 'plan.png',
          title: 'The plan first',
          text: 'The commands that would run, and the values the config asks for. Nothing starts unconfirmed.',
        },
        {
          file: 'builder.png',
          title: 'Config builder',
          text: 'An editor with schema completion, checked by the daemon, tested against the repository.',
        },
        {
          file: 'settings.png',
          title: 'Settings',
          text: 'Autostart, quickrun in a terminal, and what the command line can do - all per user.',
        },
        {
          file: 'workspaces.png',
          title: 'Workspaces',
          text: 'What is checked out, how much disk it uses, and when it last ran.',
        },
      ];
}
