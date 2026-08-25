# The editor the config builder uses

`monaco.js`, `monaco.css` and `editor.worker.js` are [Monaco](https://microsoft.github.io/monaco-editor/)
0.56.0, MIT licensed (see `LICENSE.txt`), bundled down to the editor plus YAML highlighting.

They are checked in on purpose. The published `min/vs` build is 24 MB, because its entry point
statically pulls in every syntax definition and the TypeScript, CSS, HTML and JSON language services
with their web workers - none of which a `quickrun.yml` needs. Building it ourselves gives 3 MB, and
committing the output keeps `dotnet build` free of a Node toolchain.

## Rebuilding

`entry.js` is the entry point, kept next to the output so this is reproducible:

```bash
npm install monaco-editor@0.56.0 esbuild
esbuild entry.js --bundle --minify --format=iife --target=es2020 \
  --loader:.ttf=dataurl --outfile=out/monaco.js
esbuild node_modules/monaco-editor/esm/vs/editor/editor.worker.js --bundle --minify \
  --format=iife --target=es2020 --outfile=out/editor.worker.js
```

Then copy `out/monaco.js`, `out/monaco.css` and `out/editor.worker.js` here. The codicon font is
inlined as a data URI by the `.ttf` loader, so there is no font file to serve.

The builder works without any of this: the page falls back to a plain textarea, and a config is
always checked by the daemon with the same parser and validator a run uses.
