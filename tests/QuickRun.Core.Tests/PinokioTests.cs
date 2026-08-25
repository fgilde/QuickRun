using QuickRun.Core.Config;
using QuickRun.Core.Foreign;

namespace QuickRun.Core.Tests;

public class PinokioTemplateTests
{
    private static readonly Dictionary<string, JsValue> Windows = new()
    {
        ["platform"] = new JsValue.Str("win32"),
        ["gpu"] = new JsValue.Str(""),
        ["local"] = new JsValue.Obj(new Dictionary<string, JsValue> { ["url"] = new JsValue.Str("http://localhost:8188") }),
    };

    [Fact]
    public void Text_without_a_hole_is_left_alone() =>
        Assert.Equal("python main.py", PinokioTemplate.Expand("python main.py", Windows));

    [Fact]
    public void A_variable_is_substituted() =>
        Assert.Equal("open http://localhost:8188", PinokioTemplate.Expand("open {{local.url}}", Windows));

    /// <summary>The real ComfyUI start script decides its command exactly like this.</summary>
    [Fact]
    public void An_unknown_gpu_takes_the_generic_branch_of_a_ternary() =>
        Assert.Equal("python main.py", PinokioTemplate.Expand(
            "{{platform === 'win32' && gpu === 'amd' ? 'python main.py --directml' : 'python main.py'}}", Windows));

    [Fact]
    public void A_matching_condition_takes_the_other_branch()
    {
        var amd = new Dictionary<string, JsValue>(Windows) { ["gpu"] = new JsValue.Str("amd") };
        Assert.Equal("python main.py --directml", PinokioTemplate.Expand(
            "{{platform === 'win32' && gpu === 'amd' ? 'python main.py --directml' : 'python main.py'}}", amd));
    }

    [Fact]
    public void Negation_and_inequality_are_understood()
    {
        Assert.Equal("yes", PinokioTemplate.Expand("{{platform !== 'darwin' ? 'yes' : 'no'}}", Windows));
        Assert.Equal("no", PinokioTemplate.Expand("{{!platform ? 'yes' : 'no'}}", Windows));
    }

    [Fact]
    public void A_missing_variable_is_empty_rather_than_an_error() =>
        Assert.Equal("", PinokioTemplate.Expand("{{args}}", Windows));

    [Fact]
    public void An_expression_this_evaluator_cannot_read_is_refused() =>
        Assert.Throws<JsParseException>(() => PinokioTemplate.Expand("{{ kernel.script.local(x) }}", Windows));
}

public class PinokioAdapterTests
{
    private const string Meta = """
        module.exports = {
          version: "3.7",
          title: "Comfyui",
          description: "The most powerful and modular diffusion model GUI.",
          menu: async (kernel, info) => { return [{ href: "start.js" }] }
        }
        """;

    private static ForeignConfig Load(FakeRepo repo, OSKind os = OSKind.Linux) =>
        Pinokio.Load(repo.Path, os) ?? throw new Xunit.Sdk.XunitException("no config was derived");

    [Fact]
    public void A_repository_without_a_pinokio_file_is_not_ours()
    {
        using var repo = new FakeRepo().With("start.js", "module.exports = { run: [] }");
        Assert.Null(Pinokio.Load(repo.Path, OSKind.Linux));
    }

    [Fact]
    public void A_pinokio_file_without_any_script_is_not_enough()
    {
        using var repo = new FakeRepo().With("pinokio.js", Meta);
        Assert.Null(Pinokio.Load(repo.Path, OSKind.Linux));
    }

    [Fact]
    public void An_install_script_becomes_setup_and_a_start_script_becomes_a_task()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", Meta)
            .With("install.json", """
                {
                  "run": [
                    { "method": "shell.run", "params": { "message": "git clone https://example.invalid/app app" } },
                    { "method": "shell.run", "params": { "venv": "env", "path": "app", "message": ["uv pip install -r requirements.txt"] } }
                  ]
                }
                """)
            .With("start.js", """
                module.exports = {
                  daemon: true,
                  run: [
                    {
                      method: "shell.run",
                      params: {
                        venv: "env",
                        path: "app",
                        env: { TOKENIZERS_PARALLELISM: "false" },
                        message: ["{{platform === 'win32' && gpu === 'amd' ? 'python main.py --directml' : 'python main.py'}}"],
                        on: [{ event: "/To see the GUI go to: +(http:\/\/[a-zA-Z0-9.]+:[0-9]+)/i", done: true }]
                      }
                    },
                    { method: "local.set", params: { url: "http://localhost:8188" } }
                  ]
                }
                """);

        var foreign = Load(repo);
        var config = foreign.Config;

        Assert.Equal("pinokio", foreign.Kind);
        Assert.Equal("Comfyui", config.Name);

        Assert.Equal(
            new[]
            {
                "git clone https://example.invalid/app app",
                "python3 -m venv env",
                ". env/bin/activate && uv pip install -r requirements.txt",
            },
            config.Setup.Select(s => s.Run));
        Assert.Equal("app", config.Setup[2].Cwd);

        var task = Assert.Single(config.Tasks);
        Assert.Equal(". env/bin/activate && python main.py", task.Run);
        Assert.Equal("app", task.Cwd);
        Assert.Equal("false", task.Env["TOKENIZERS_PARALLELISM"]);

        // Pinokio's own "it is up" pattern is QuickRun's log condition, case-insensitively.
        Assert.Equal("(?i)To see the GUI go to: +(http://[a-zA-Z0-9.]+:[0-9]+)", task.ReadyWhen!.Log);

        // local.set url is what Pinokio's "open web UI" button uses, so it is what we open.
        Assert.Equal("http://localhost:8188", task.OpenUrl);
        Assert.True(task.OpenReady);

        Assert.Contains(config.Requires, r => r.Tool == "git");
        Assert.Contains(config.Requires, r => r.Tool == "python");
        Assert.Contains(config.Requires, r => r.Tool == "uv");
    }

    [Fact]
    public void A_windows_run_activates_the_virtual_environment_the_windows_way()
    {
        using var repo = new FakeRepo()
            .With("pinokio.json", """{"title": "App"}""")
            .With("start.json", """
                {"run": [{"method": "shell.run", "params": {"venv": "env", "path": "app", "message": "python app.py"}}]}
                """);

        var config = Load(repo, OSKind.Windows).Config;
        Assert.Equal(@"python -m venv env", config.Setup[0].Run);
        Assert.Equal(@"call env\Scripts\activate.bat && python app.py", Assert.Single(config.Tasks).Run);
    }

    /// <summary>A start script that needs a venv the install script never created still gets one.</summary>
    [Fact]
    public void A_venv_a_task_needs_is_created_before_the_run()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", Meta)
            .With("start.json", """
                {"run": [{"method": "shell.run", "params": {"venv": "env", "message": "python app.py"}}]}
                """);

        var config = Load(repo).Config;
        Assert.Equal("python3 -m venv env", Assert.Single(config.Setup).Run);
        Assert.True(config.Setup[0].ContinueOnError);
    }

    [Fact]
    public void Steps_that_cannot_be_translated_are_reported_not_invented()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", Meta)
            .With("start.json", """
                {
                  "run": [
                    { "method": "fs.link", "params": { "drive": {} } },
                    { "method": "shell.run", "params": { "message": "python app.py" } }
                  ]
                }
                """);

        var foreign = Load(repo);
        Assert.Equal("python app.py", Assert.Single(foreign.Config.Tasks).Run);
        Assert.Contains(foreign.Notes, n => n.Contains("fs.link"));
    }

    [Fact]
    public void A_download_becomes_a_download_and_a_nested_script_is_followed()
    {
        using var repo = new FakeRepo()
            .With("pinokio.json", """{"title": "App"}""")
            .With("start.json", """{"run": [{"method": "shell.run", "params": {"message": "python app.py"}}]}""")
            .With("torch.json", """
                {"run": [{"method": "shell.run", "params": {"path": "{{args && args.path ? args.path : '.'}}", "message": "uv pip install torch"}}]}
                """)
            .With("install.json", """
                {
                  "run": [
                    { "method": "fs.download", "params": { "uri": "https://example.invalid/model.safetensors", "path": "models/model.safetensors" } },
                    { "method": "script.start", "params": { "uri": "torch.json", "params": { "path": "app" } } }
                  ]
                }
                """);

        var config = Load(repo).Config;
        Assert.Equal(
            new[]
            {
                "curl -L -o \"models/model.safetensors\" \"https://example.invalid/model.safetensors\"",
                "uv pip install torch",
            },
            config.Setup.Select(s => s.Run));
        Assert.Equal("app", config.Setup[1].Cwd);
    }

    /// <summary>
    /// An install script written as a function is unreadable, and saying so is better than a run
    /// that skips the install and then fails half way through.
    /// </summary>
    [Fact]
    public void An_install_script_that_is_a_function_still_leaves_the_start_script_usable()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", Meta)
            .With("install.js", "module.exports = async (kernel, info) => { return { run: [] } }")
            .With("start.json", """{"run": [{"method": "shell.run", "params": {"message": "python app.py"}}]}""");

        var config = Load(repo).Config;
        Assert.Empty(config.Setup);
        Assert.Equal("python app.py", Assert.Single(config.Tasks).Run);
    }

    /// <summary>
    /// A real torch.js lists a CUDA, a DirectML and a CPU install and picks one with `when`. Running
    /// all three is exactly the failure this guards against.
    /// </summary>
    [Fact]
    public void Only_the_step_whose_condition_holds_is_kept()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", Meta)
            .With("start.json", """
                {
                  "run": [
                    { "when": "{{platform === 'win32'}}", "method": "shell.run", "params": { "message": "windows.bat" } },
                    { "when": "{{platform === 'linux'}}", "method": "shell.run", "params": { "message": "linux.sh" } },
                    { "when": "{{kernel.gpus.find(x => /50.+/.test(x.model))}}", "method": "shell.run", "params": { "message": "fancy.sh" } }
                  ]
                }
                """);

        var foreign = Load(repo);
        Assert.Equal("linux.sh", Assert.Single(foreign.Config.Tasks).Run);
        Assert.Contains(foreign.Notes, n => n.Contains("condition QuickRun cannot evaluate"));
    }

    /// <summary>"." and no path at all are the same folder, so one venv is enough.</summary>
    [Fact]
    public void The_same_virtual_environment_is_created_once()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", Meta)
            .With("install.json", """
                {
                  "run": [
                    { "method": "shell.run", "params": { "venv": "env", "path": ".", "message": "uv pip install torch" } },
                    { "method": "shell.run", "params": { "venv": "env", "message": "uv pip install gradio" } }
                  ]
                }
                """)
            .With("start.json", """{"run": [{"method": "shell.run", "params": {"venv": "env", "message": "python app.py"}}]}""");

        var config = Load(repo).Config;
        Assert.Single(config.Setup, s => s.Run == "python3 -m venv env");
        Assert.All(config.Setup.Skip(1), s => Assert.Null(s.Cwd));
    }

    /// <summary>
    /// A start script asking Pinokio's runtime for a port cannot be read, and a config with an
    /// install but nothing to start is worse than letting detection have a go.
    /// </summary>
    [Fact]
    public void A_start_script_that_is_a_function_leaves_the_repository_to_detection()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", Meta)
            .With("install.json", """{"run": [{"method": "shell.run", "params": {"message": "git clone https://example.invalid/app app"}}]}""")
            .With("start.js", "module.exports = async (kernel) => { const port = await kernel.port(); return { run: [] } }");

        Assert.Null(Pinokio.Load(repo.Path, OSKind.Linux));
    }

    [Fact]
    public void The_derived_config_passes_the_validator()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", Meta)
            .With("start.json", """{"run": [{"method": "shell.run", "params": {"message": "python app.py"}}]}""");

        Assert.DoesNotContain(ConfigValidator.Validate(Load(repo).Config), i => i.IsError);
    }
}
