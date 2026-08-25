// The editor QuickRun's config builder uses: the editor itself, YAML highlighting, and nothing
// else. The published min build statically pulls every language plus the TypeScript, CSS, HTML and
// JSON language services with their workers - 24 MB for a page that only ever edits one YAML file.

import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/languages/definitions/yaml/register';

// The only worker left is the editor's own, which does tokenisation and diffing off the main thread.
self.MonacoEnvironment = { getWorkerUrl: () => '/monaco/editor.worker.js' };

window.monaco = monaco;
