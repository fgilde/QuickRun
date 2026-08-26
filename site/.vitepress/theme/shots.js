// The screenshots, and what each one is showing.
//
// Captured from a real run of a real repository (fgilde/MudBlazor.Extensions) against a released
// build, by scripts/capture-screenshots.mjs - nothing here is a mockup, which is the point: the
// window on the landing page is the window you get.

export function shots(de) {
  return de
    ? [
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
