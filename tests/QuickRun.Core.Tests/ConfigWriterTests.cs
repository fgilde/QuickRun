using QuickRun.Core.Config;
using QuickRun.Core.Foreign;

namespace QuickRun.Core.Tests;

/// <summary>
/// A written config has to parse back to the same thing, or the builder hands people a file that
/// runs differently from what they tested.
/// </summary>
public class ConfigWriterTests
{
    private static RunConfig RoundTrip(RunConfig config) =>
        ConfigParser.Parse(ConfigWriter.ToYaml(config), OSKind.Linux);

    [Fact]
    public void A_full_config_survives_being_written_and_read()
    {
        var original = ConfigParser.Parse("""
            version: 1
            name: Demo
            description: "A: colon, and a #hash"
            requires:
              - tool: dotnet
                version: ">=10.0"
                install: https://dotnet.microsoft.com/download
              - tool: docker
                optional: true
            inputs:
              - id: apiKey
                label: API key
                type: password
                required: true
                env: API_KEY
              - id: mode
                type: select
                default: fast
                options:
                  - fast
                  - {value: slow, label: Slow but sure}
            env:
              GLOBAL: "1"
            setup:
              - run: npm ci
                when: [linux, macos]
                continueOnError: true
            tasks:
              - name: db
                run: docker compose up -d
                readyWhen: {port: 5432}
              - name: web
                run: npm run dev
                cwd: apps/web
                dependsOn: [db]
                env:
                  PORT: "5173"
                readyWhen: {http: "http://localhost:5173"}
                open: true
                restart: onFailure
            stop:
              - run: docker compose down
            """, OSKind.Linux);

        var again = RoundTrip(original);

        // Records hold collections, so equality on them is reference equality - the comparison has
        // to be on the content, which is what "the same config" actually means here.
        Assert.Equal(original.Name, again.Name);
        Assert.Equal(original.Description, again.Description);
        Assert.Equal(original.Requires, again.Requires);
        Assert.Equal(original.Env, again.Env);
        Assert.Equal(Describe(original.Inputs), Describe(again.Inputs));
        Assert.Equal(Describe(original.Setup), Describe(again.Setup));
        Assert.Equal(Describe(original.Tasks), Describe(again.Tasks));
        Assert.Equal(Describe(original.Stop), Describe(again.Stop));
    }

    /// <summary>The trap the documentation warns about: a log pattern in double quotes will not parse.</summary>
    [Fact]
    public void A_log_pattern_is_written_in_single_quotes()
    {
        var config = ConfigParser.Parse("""
            tasks:
              - name: app
                run: python app.py
                readyWhen: {log: 'Running on (?<url>\S+)'}
                open: true
            """, OSKind.Linux);

        var yaml = ConfigWriter.ToYaml(config);
        Assert.Contains(@"readyWhen: {log: 'Running on (?<url>\S+)'}", yaml);
        Assert.Equal(@"Running on (?<url>\S+)", Assert.Single(RoundTrip(config).Tasks).ReadyWhen!.Log);
    }

    [Fact]
    public void The_schema_line_is_there_so_an_editor_can_help()
    {
        var yaml = ConfigWriter.ToYaml(RunConfigDefaults.Empty with { Name = "x" }, "generated from something");

        Assert.StartsWith(ConfigWriter.SchemaLine, yaml);
        Assert.Contains("# generated from something", yaml);
    }

    /// <summary>What the builder starts from when a repository only has Pinokio scripts.</summary>
    [Fact]
    public void A_config_derived_from_pinokio_scripts_can_be_written_out()
    {
        using var repo = new FakeRepo()
            .With("pinokio.js", """module.exports = { title: "App", description: "does things" }""")
            .With("start.json", """
                {"run": [{"method": "shell.run", "params": {"venv": "env", "message": "python app.py",
                 "on": [{"event": "/http:\/\/[0-9.:]+/", "done": true}]}}]}
                """);

        var foreign = Pinokio.Load(repo.Path, OSKind.Linux)!;
        var yaml = ConfigWriter.ToYaml(foreign.Config, $"generated from this repository's {foreign.Kind} scripts");
        var again = ConfigParser.Parse(yaml, OSKind.Linux);

        Assert.Equal("App", again.Name);
        Assert.Equal(". env/bin/activate && python app.py", Assert.Single(again.Tasks).Run);
        Assert.Equal("http://[0-9.:]+", Assert.Single(again.Tasks).ReadyWhen!.Log);
        Assert.DoesNotContain(ConfigValidator.Validate(again), i => i.IsError);
    }
    private static IReadOnlyList<string> Describe(IReadOnlyList<InputDef> inputs) =>
        inputs.Select(i => string.Join('|',
            i.Id, i.Label, i.Type, i.Description, i.Default, i.Required, i.Pattern, i.Min, i.Max, i.Env, i.Persist,
            string.Join(',', i.Options.Select(o => $"{o.Value}={o.Label}")))).ToList();

    private static IReadOnlyList<string> Describe(IReadOnlyList<Step> steps) =>
        steps.Select(s => string.Join('|', s.Run, s.Cwd, string.Join(',', s.When), s.ContinueOnError)).ToList();

    private static IReadOnlyList<string> Describe(IReadOnlyList<TaskDef> tasks) =>
        tasks.Select(t => string.Join('|',
            t.Name, t.Run, t.Cwd, string.Join(',', t.Env.Select(e => $"{e.Key}={e.Value}")),
            string.Join(',', t.DependsOn), t.ReadyWhen, t.OpenReady, t.OpenUrl, t.Restart)).ToList();
}
