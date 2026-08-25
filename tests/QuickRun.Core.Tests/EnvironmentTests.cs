using QuickRun.Core.Config;
using QuickRun.Core.Inputs;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

/// <summary>
/// What a command actually sees. Every one of these was a way for a config to be silently ignored:
/// the top-level env block was never passed on at all, which is how a run "works" and reaches
/// nothing on the port the config pinned.
/// </summary>
public class EnvironmentTests
{
    private static string Show(string name) =>
        OSKinds.Current == OSKind.Windows ? $"echo %{name}%" : $"echo ${name}";

    private sealed class Log
    {
        private readonly List<string> _lines = new();

        public Action<RunEvent> Sink => e =>
        {
            if (e.Kind is RunEventKind.Output or RunEventKind.Error)
                lock (_lines) _lines.Add(e.Text);
        };

        public string Text { get { lock (_lines) return string.Join("\n", _lines); } }
    }

    private static async Task<string> RunAsync(string yaml, params string[] inputs)
    {
        using var repo = new FakeRepo();
        var log = new Log();
        await using var runner = new Runner(log.Sink);

        var config = ConfigParser.Parse(yaml, OSKinds.Current);
        var values = InputResolver.ApplyDefaults(config.Inputs, InputResolver.ParseAssignments(inputs));

        var options = new RunOptions(repo.Path,
            new InterpolationContext(values, repo.Path, "app", "main", _ => null),
            InputResolver.ToEnv(config.Inputs, values),
            Interpolator.Secrets(values, InputResolver.SecretIds(config.Inputs)),
            TimeSpan.FromSeconds(5),
            SkipRequires: true);

        await runner.ExecuteAsync(config, options, CancellationToken.None);
        return log.Text;
    }

    [Fact]
    public async Task The_configs_env_block_reaches_the_process()
    {
        var text = await RunAsync(string.Join("\n",
            "env:",
            "  GREETING: from-the-config",
            "tasks:",
            "  - name: t",
            $"    run: {Show("GREETING")}"));

        Assert.Contains("from-the-config", text);
    }

    [Fact]
    public async Task The_configs_env_block_reaches_a_setup_step_too()
    {
        var text = await RunAsync(string.Join("\n",
            "env:",
            "  GREETING: setup-sees-it",
            "setup:",
            $"  - run: {Show("GREETING")}",
            "tasks: []"));

        Assert.Contains("setup-sees-it", text);
    }

    /// <summary>An input that names an env variable is how a value gets into a process it runs.</summary>
    [Fact]
    public async Task An_inputs_value_arrives_as_the_env_it_names()
    {
        var text = await RunAsync(string.Join("\n",
            "inputs:",
            "  - id: mode",
            "    env: RUN_MODE",
            "    default: fast",
            "tasks:",
            "  - name: t",
            $"    run: {Show("RUN_MODE")}"), "mode=slow");

        Assert.Contains("slow", text);
    }

    [Fact]
    public async Task An_input_can_be_interpolated_into_the_command()
    {
        var text = await RunAsync(string.Join("\n",
            "inputs:",
            "  - id: greeting",
            "    default: hello",
            "tasks:",
            "  - name: t",
            "    run: echo ${inputs.greeting}-there"), "greeting=servus");

        Assert.Contains("servus-there", text);
    }

    /// <summary>A task's own env is more specific than the config's, so it wins.</summary>
    [Fact]
    public async Task A_tasks_env_overrides_the_configs()
    {
        var text = await RunAsync(string.Join("\n",
            "env:",
            "  WHO: config",
            "tasks:",
            "  - name: t",
            $"    run: {Show("WHO")}",
            "    env:",
            "      WHO: task"));

        Assert.Contains("task", text);
        Assert.DoesNotContain("config", text);
    }

    [Fact]
    public async Task An_env_value_can_be_interpolated_from_an_input()
    {
        var text = await RunAsync(string.Join("\n",
            "inputs:",
            "  - id: port",
            "    default: \"5123\"",
            "env:",
            "  ADDRESS: http://localhost:${inputs.port}",
            "tasks:",
            "  - name: t",
            $"    run: {Show("ADDRESS")}"));

        Assert.Contains("http://localhost:5123", text);
    }

    /// <summary>A secret is a value, not a leak: it reaches the process and never the log.</summary>
    [Fact]
    public async Task A_secret_input_is_passed_but_redacted_in_the_log()
    {
        var text = await RunAsync(string.Join("\n",
            "inputs:",
            "  - id: apiKey",
            "    type: password",
            "    env: API_KEY",
            "tasks:",
            "  - name: t",
            $"    run: {Show("API_KEY")}"), "apiKey=sk-do-not-print");

        Assert.DoesNotContain("sk-do-not-print", text);
        Assert.Contains("***", text);
    }

    /// <summary>
    /// MSBuild worker nodes outlive their build and hold its output pipe, which looked like a run
    /// frozen after a restore. The default that prevents it must actually be set.
    /// </summary>
    [Fact]
    public async Task Msbuild_node_reuse_is_off_by_default_and_a_config_can_turn_it_back_on()
    {
        Assert.Contains("1", await RunAsync(string.Join("\n",
            "tasks:",
            "  - name: t",
            $"    run: {Show("MSBUILDDISABLENODEREUSE")}")));

        Assert.Contains("keep-them", await RunAsync(string.Join("\n",
            "env:",
            "  MSBUILDDISABLENODEREUSE: keep-them",
            "tasks:",
            "  - name: t",
            $"    run: {Show("MSBUILDDISABLENODEREUSE")}")));
    }
}
