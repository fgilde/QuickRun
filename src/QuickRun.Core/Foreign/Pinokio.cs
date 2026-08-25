using System.Text.RegularExpressions;
using QuickRun.Core.Config;

namespace QuickRun.Core.Foreign;

/// <summary>A config QuickRun derived from someone else's format, plus what it had to leave out.</summary>
public sealed record ForeignConfig(string Kind, RunConfig Config, IReadOnlyList<string> Notes);

/// <summary>
/// Runs a Pinokio app the way Pinokio would.
/// <para>
/// A Pinokio repository ships a <c>pinokio.js</c> next to scripts - usually <c>install.js</c> and
/// <c>start.js</c> - whose exported <c>run</c> array is a list of <c>{ method, params }</c> steps.
/// The methods that matter for starting something are <c>shell.run</c> (a command, optionally in a
/// subfolder and with a Python virtual environment activated), <c>fs.download</c>, and
/// <c>local.set</c>, whose <c>url</c> is what Pinokio's own "open web UI" button points at. A
/// <c>shell.run</c> may carry an <c>on</c> array of regular expressions; the one marked
/// <c>done</c> is how Pinokio knows the service came up, which is exactly QuickRun's
/// <c>readyWhen.log</c>.
/// </para>
/// <para>
/// Nothing here executes anything: the result is a <see cref="RunConfig"/>, so the confirmation
/// gate, the command list and the runner all apply unchanged. Steps that cannot be translated are
/// dropped and reported rather than guessed at.
/// </para>
/// </summary>
public static class Pinokio
{
    private const int MaxScriptDepth = 3;

    private static readonly string[] Markers = { "pinokio.js", "pinokio.json" };
    private static readonly string[] StartNames = { "start.js", "start.json", "run.js", "run.json" };
    private static readonly string[] InstallNames = { "install.js", "install.json" };

    public static bool Present(string root) =>
        Markers.Any(m => File.Exists(Path.Combine(root, m)));

    public static ForeignConfig? Load(string root, OSKind os)
    {
        if (!Present(root)) return null;

        var start = FirstScript(root, StartNames);
        var install = FirstScript(root, InstallNames);
        if (start is null && install is null) return null;

        var meta = Markers.Select(m => ReadScript(root, m)).FirstOrDefault(v => v is not null);
        var state = new Translation(root, os);

        // local.set can appear after the command that renders {{local.url}}, so the locals are
        // collected before anything is expanded.
        state.CollectLocals(install);
        state.CollectLocals(start);

        var setup = install is null ? new List<Step>() : state.Setup(install);
        var tasks = state.Tasks(start);

        // Steps a start script needs but only an install script would have created.
        setup.InsertRange(0, state.PendingSetup);

        // With nothing to start there is nothing to hand over: the caller falls back to detection,
        // which is a better answer than a config whose only content is an install.
        if (tasks.Count == 0) return null;

        var config = new RunConfig(
            1,
            meta?.Field("title")?.Text ?? meta?.Field("name")?.Text,
            Describe(meta, state.Notes),
            null,
            null,
            state.Requirements(),
            Array.Empty<InputDef>(),
            new Dictionary<string, string>(),
            setup,
            tasks,
            Array.Empty<Step>());

        return new ForeignConfig("pinokio", config, state.Notes);
    }

    private static string? Describe(JsValue? meta, IReadOnlyList<string> notes)
    {
        var description = meta?.Field("description")?.Text;
        var parts = new[] { description, notes.Count == 0 ? null : string.Join(" ", notes) }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var text = string.Join(" ", parts);
        return text.Length == 0 ? null : text;
    }

    private static string? FirstScript(string root, IEnumerable<string> names) =>
        names.Select(n => Path.Combine(root, n)).FirstOrDefault(File.Exists);

    private static JsValue? ReadScript(string root, string name) => ReadScript(Path.Combine(root, name));

    private static JsValue? ReadScript(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try { return JsLiteral.TryParse(File.ReadAllText(path)); }
        catch (IOException) { return null; }
    }

    /// <summary>The work of turning one repository's scripts into steps and tasks.</summary>
    private sealed class Translation(string root, OSKind os)
    {
        private readonly Dictionary<string, JsValue> _locals = new(StringComparer.Ordinal);
        private readonly HashSet<string> _venvs = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _skipped = new(StringComparer.Ordinal);
        private readonly List<string> _notes = new();
        private readonly List<string> _commands = new();

        public List<Step> PendingSetup { get; } = new();

        public IReadOnlyList<string> Notes
        {
            get
            {
                // The same note about six torch variants is noise; how many were left out is not.
                var counted = _notes
                    .GroupBy(n => n, StringComparer.Ordinal)
                    .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ({g.Count()} times)")
                    .ToList();

                if (_skipped.Count > 0)
                    counted.Add($"Steps QuickRun does not translate were skipped: {string.Join(", ", _skipped.Order())}.");

                return counted;
            }
        }

        public void CollectLocals(string? script)
        {
            foreach (var step in Steps(ReadScript(script)))
            {
                if (Method(step) != "local.set") continue;
                if (step.Field("params") is not JsValue.Obj values) continue;

                foreach (var (key, value) in values.Fields)
                    _locals[key] = value is JsValue.Str s ? new JsValue.Str(Expand(s.Value) ?? s.Value) : value;
            }
        }

        public List<Step> Setup(string script)
        {
            var steps = new List<Step>();
            Readable(script);

            foreach (var piece in Pieces(script, 0))
            {
                steps.AddRange(piece.Creation);
                steps.AddRange(piece.Commands.Select(c => new Step(c, piece.Cwd, Array.Empty<string>(), false)));
            }

            return steps;
        }

        public List<TaskDef> Tasks(string? script)
        {
            if (script is null) return new List<TaskDef>();

            Readable(script);

            var tasks = new List<TaskDef>();
            var url = _locals.TryGetValue("url", out var local) ? Http(local.Text) : null;

            foreach (var piece in Pieces(script, 0))
            {
                // A task is one process, so a step's several commands run in one shell.
                PendingSetup.AddRange(piece.Creation);
                if (piece.Commands.Count == 0) continue;

                tasks.Add(new TaskDef(
                    tasks.Count == 0 ? "app" : $"app-{tasks.Count + 1}",
                    string.Join(" && ", piece.Commands),
                    piece.Cwd,
                    piece.Env,
                    Array.Empty<string>(),
                    piece.Ready,
                    url is not null,
                    url,
                    RestartPolicy.Never));
            }

            return tasks;
        }

        /// <summary>
        /// Says so when a script is a JavaScript function rather than a literal. Those ask Pinokio's
        /// runtime for a free port or the machine's GPUs, and no amount of reading gets the steps
        /// out of them - but a run that silently skips the install is worse than being told.
        /// </summary>
        private void Readable(string script)
        {
            var name = Path.GetFileName(script);
            var value = ReadScript(script);

            if (value is null)
                _notes.Add($"{name} could not be read.");
            else if (Steps(value).Count == 0)
                _notes.Add($"{name} builds its steps in JavaScript, which QuickRun cannot read - it was skipped.");
        }

        public IReadOnlyList<ToolRequirement> Requirements()
        {
            var all = string.Join("\n", _commands);
            var requirements = new List<ToolRequirement>();

            void Need(string tool, string install, bool when)
            {
                if (when) requirements.Add(new ToolRequirement(tool, null, install, false));
            }

            Need("git", "https://git-scm.com/downloads", Word(all, "git"));
            Need("python", "https://www.python.org/downloads/",
                _venvs.Count > 0 || Word(all, "python") || Word(all, "python3") || Word(all, "pip"));
            Need("uv", "https://docs.astral.sh/uv/getting-started/installation/", Word(all, "uv"));
            Need("node", "https://nodejs.org", Word(all, "node") || Word(all, "npm") || Word(all, "npx"));

            if (Word(all, "conda") || Word(all, "micromamba"))
                _notes.Add("This app expects conda, which Pinokio ships and QuickRun does not - install it yourself if the run fails.");

            return requirements;
        }

        private static bool Word(string text, string word) =>
            Regex.IsMatch(text, $@"(^|[\s&|;(""']){Regex.Escape(word)}(\s|$)", RegexOptions.Multiline);

        // ---- steps ----------------------------------------------------------

        private IEnumerable<Piece> Pieces(string script, int depth)
        {
            var value = ReadScript(script);

            foreach (var step in Steps(value))
            {
                // A Pinokio script lists every variant of a step and picks one with `when` - the
                // CUDA install, the DirectML install, the CPU install. Ignoring that would run all
                // of them, so a step whose condition is false, or whose condition cannot be read,
                // is left out.
                if (!When(step)) continue;

                var piece = Translate(step, script, depth);
                if (piece is not null) yield return piece;
            }
        }

        private bool When(JsValue step)
        {
            if (step.Field("when")?.Text is not { } condition) return true;

            try { return PinokioTemplate.Evaluate(Unwrap(condition), Variables()).Truthy; }
            catch (JsParseException)
            {
                // Conditions like "kernel.gpus.find(x => / 50.+/.test(x.model))" ask about hardware
                // through Pinokio's own runtime. False is the safe answer: the script's next
                // variant is the more general one.
                _notes.Add("A step with a condition QuickRun cannot evaluate was left out.");
                return false;
            }
        }

        /// <summary>A condition is a template, and what is wanted is the value inside it.</summary>
        private static string Unwrap(string condition)
        {
            var trimmed = condition.Trim();
            return trimmed.StartsWith("{{", StringComparison.Ordinal) && trimmed.EndsWith("}}", StringComparison.Ordinal)
                ? trimmed[2..^2]
                : trimmed;
        }

        private IEnumerable<Piece> Nested(string uri, JsValue? parameters, string from, int depth)
        {
            if (depth + 1 >= MaxScriptDepth)
            {
                _notes.Add($"Nested script '{uri}' was not followed: too many levels.");
                return Array.Empty<Piece>();
            }

            var path = Path.Combine(Path.GetDirectoryName(from) ?? root, uri);
            if (!File.Exists(path))
            {
                _notes.Add($"Nested script '{uri}' does not exist in the repository.");
                return Array.Empty<Piece>();
            }

            var saved = _args;
            _args = parameters ?? JsValue.None;
            try { return Pieces(path, depth + 1).ToList(); }
            finally { _args = saved; }
        }

        private Piece? Translate(JsValue step, string script, int depth)
        {
            var method = Method(step);
            var parameters = step.Field("params") ?? JsValue.None;

            switch (method)
            {
                case "shell.run":
                    return Shell(parameters);

                case "script.start":
                {
                    var uri = Expand(parameters.Field("uri")?.Text);
                    if (uri is null) { Note(method); return null; }

                    var nested = Nested(uri, parameters.Field("params"), script, depth).ToList();
                    if (nested.Count == 0) return null;

                    // A nested script is a sequence, and a Piece is one shell, so its steps are
                    // flattened into a single piece in the order the script had them.
                    return new Piece(
                        nested.SelectMany(p => p.Commands).ToList(),
                        nested[0].Cwd,
                        nested.SelectMany(p => p.Env).GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.Last().Value),
                        nested.Select(p => p.Ready).FirstOrDefault(r => r is not null),
                        nested.SelectMany(p => p.Creation).ToList());
                }

                case "fs.download":
                {
                    var uri = Expand(parameters.Field("uri")?.Text ?? parameters.Field("url")?.Text);
                    var target = Expand(parameters.Field("path")?.Text);
                    if (uri is null || target is null) { Note(method); return null; }

                    return Track(new Piece(new[] { $"curl -L -o \"{target}\" \"{uri}\"" }, null,
                        new Dictionary<string, string>(), null, Array.Empty<Step>()));
                }

                case "local.set":
                case "log":
                case "notify":
                case "json":
                case "jump":
                case "input":
                case "filepicker":
                case "script.return":
                case "script.stop":
                    return null;

                default:
                    Note(method ?? "a step without a method");
                    return null;
            }
        }

        private Piece? Shell(JsValue parameters)
        {
            var commands = new List<string>();

            foreach (var message in parameters.Field("message")?.Strings ?? Array.Empty<string>())
            {
                var expanded = Expand(message);
                if (expanded is null) return null;
                if (expanded.Trim().Length > 0) commands.Add(expanded.Trim());
            }

            if (commands.Count == 0) { Note("shell.run"); return null; }

            var cwd = Folder(Expand(parameters.Field("path")?.Text));
            var venv = Expand(parameters.Field("venv")?.Text);
            var creation = new List<Step>();

            if (!string.IsNullOrWhiteSpace(venv))
            {
                var key = $"{cwd}|{venv}";
                if (_venvs.Add(key))
                    creation.Add(new Step(
                        $"{(os == OSKind.Windows ? "python" : "python3")} -m venv {venv}",
                        cwd, Array.Empty<string>(), true));

                commands = commands.Select(c => Activate(venv!) + c).ToList();
            }

            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            if (parameters.Field("env") is JsValue.Obj values)
                foreach (var (key, value) in values.Fields)
                    if (Expand(value.AsText) is { } text)
                        env[key] = text;

            if (parameters.Field("sudo")?.Truthy == true)
                _notes.Add("A step asked for administrator rights, which QuickRun does not grant - it runs as you do.");

            return Track(new Piece(commands, cwd, env, Ready(parameters.Field("on")), creation));
        }

        /// <summary>
        /// The repository root, however the script spelled it. Without this, "." and no path at all
        /// are two different folders, and the same virtual environment gets created twice.
        /// </summary>
        private static string? Folder(string? path)
        {
            var trimmed = path?.Trim().TrimEnd('/', '\\');
            return string.IsNullOrEmpty(trimmed) || trimmed == "." ? null : trimmed;
        }

        private Piece Track(Piece piece)
        {
            _commands.AddRange(piece.Commands);
            return piece;
        }

        /// <summary>Activating the virtual environment in the same shell as the command.</summary>
        private string Activate(string venv) =>
            os == OSKind.Windows
                ? $"call {venv.Replace('/', '\\')}\\Scripts\\activate.bat && "
                : $". {venv}/bin/activate && ";

        /// <summary>
        /// The event Pinokio treats as "it is up" becomes a log condition. Its pattern is a
        /// JavaScript regular expression literal, so the delimiters go and an <c>i</c> flag becomes
        /// an inline one.
        /// </summary>
        private ReadyWhen? Ready(JsValue? events)
        {
            foreach (var handler in events?.Items ?? Array.Empty<JsValue>())
            {
                if (handler.Field("done")?.Truthy != true) continue;
                if (handler.Field("event")?.Text is not { } pattern) continue;

                var match = Regex.Match(pattern, @"^/(?<body>.*)/(?<flags>[a-z]*)$", RegexOptions.Singleline);
                var body = (match.Success ? match.Groups["body"].Value : pattern).Replace(@"\/", "/");
                var ignoreCase = match.Success && match.Groups["flags"].Value.Contains('i');

                try
                {
                    _ = new Regex(body);
                    return new ReadyWhen(null, null, ignoreCase ? $"(?i){body}" : body, null);
                }
                catch (ArgumentException)
                {
                    _notes.Add("A readiness pattern from the Pinokio script could not be used.");
                }
            }

            return null;
        }

        private void Note(string method) => _skipped.Add(method);

        private static IReadOnlyList<JsValue> Steps(JsValue? script)
        {
            if (script is null) return Array.Empty<JsValue>();

            var run = script.Field("run") ?? (script is JsValue.Arr ? script : null);
            var steps = run?.Items.Where(s => s is JsValue.Obj).ToList() ?? new List<JsValue>();
            return steps;
        }

        private static string? Method(JsValue step) => step.Field("method")?.Text;

        // ---- template variables ---------------------------------------------

        private JsValue _args = JsValue.None;

        private string? Expand(string? template)
        {
            if (template is null) return null;

            try { return PinokioTemplate.Expand(template, Variables()); }
            catch (JsParseException e)
            {
                _notes.Add($"A step was skipped: {e.Message}.");
                return null;
            }
        }

        /// <summary>
        /// What Pinokio's own variables mean here. <c>gpu</c> is deliberately unset unless the user
        /// says otherwise: an unknown value makes a <c>gpu === 'amd'</c> branch false, which is the
        /// generic command, rather than a guess that cannot run.
        /// </summary>
        private IReadOnlyDictionary<string, JsValue> Variables() => new Dictionary<string, JsValue>(StringComparer.Ordinal)
        {
            ["platform"] = new JsValue.Str(os switch
            {
                OSKind.Windows => "win32",
                OSKind.MacOs => "darwin",
                _ => "linux",
            }),
            ["arch"] = new JsValue.Str(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()),
            ["gpu"] = new JsValue.Str(Gpu()),
            ["cwd"] = new JsValue.Str(root.Replace('\\', '/')),
            ["port"] = JsValue.None,
            ["args"] = _args,
            ["input"] = JsValue.None,
            ["local"] = new JsValue.Obj(_locals),
            ["env"] = JsValue.None,
            ["kernel"] = JsValue.None,
        };

        /// <summary>
        /// Which accelerator a Pinokio script should assume. Nothing is executed to find out: an
        /// <c>nvidia-smi</c> on the PATH is what a driver installation leaves behind, and Apple
        /// silicon is decided by the platform. Unknown means the CPU variant, which always works.
        /// </summary>
        private string Gpu()
        {
            if (Environment.GetEnvironmentVariable("QUICKRUN_GPU") is { Length: > 0 } declared) return declared;
            if (os == OSKind.MacOs) return "apple";

            var exe = os == OSKind.Windows ? "nvidia-smi.exe" : "nvidia-smi";
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);

            foreach (var directory in paths.Where(d => d.Length > 0))
            {
                try
                {
                    if (File.Exists(Path.Combine(directory, exe))) return "nvidia";
                }
                catch (ArgumentException)
                {
                    // A PATH entry with illegal characters is not a directory to look in.
                }
            }

            return "";
        }

        private static string? Http(string? url) =>
            url is not null && Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
                ? url
                : null;
    }

    private sealed record Piece(
        IReadOnlyList<string> Commands,
        string? Cwd,
        IReadOnlyDictionary<string, string> Env,
        ReadyWhen? Ready,
        IReadOnlyList<Step> Creation);
}
