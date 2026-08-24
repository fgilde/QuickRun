using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

public class ConfigParserTests
{
    [Fact]
    public void Scalar_run_becomes_one_task_named_run()
    {
        var c = ConfigParser.Parse("run: ./run.sh", OSKind.Linux);
        var task = Assert.Single(c.Tasks);
        Assert.Equal("run", task.Name);
        Assert.Equal("./run.sh", task.Run);
        Assert.Equal(1, c.Version);
        Assert.Empty(c.Setup);
        Assert.Empty(c.Requires);
    }

    [Fact]
    public void Platform_map_picks_the_current_platform()
    {
        const string yaml = "run:\n  linux: ./run.sh\n  macos: ./run.sh\n  windows: ./run.ps1";
        Assert.Equal("./run.ps1", ConfigParser.Parse(yaml, OSKind.Windows).Tasks[0].Run);
        Assert.Equal("./run.sh", ConfigParser.Parse(yaml, OSKind.MacOs).Tasks[0].Run);
    }

    [Fact]
    public void Platform_map_without_an_entry_for_this_platform_throws()
    {
        var ex = Assert.Throws<ConfigException>(() => ConfigParser.Parse("run:\n  linux: ./run.sh", OSKind.Windows));
        Assert.Contains("windows", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mapping_with_unknown_keys_is_not_a_platform_map()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("run:\n  solaris: ./run.sh", OSKind.Linux));
    }

    [Fact]
    public void String_list_setup_becomes_sequential_steps()
    {
        var c = ConfigParser.Parse("setup: [npm ci, dotnet restore]\nrun: npm start", OSKind.Linux);
        Assert.Equal(new[] { "npm ci", "dotnet restore" }, c.Setup.Select(s => s.Run));
        Assert.All(c.Setup, s => Assert.False(s.ContinueOnError));
    }

    [Fact]
    public void String_list_tasks_get_generated_names()
    {
        var c = ConfigParser.Parse("tasks: [npm start, python api.py]", OSKind.Linux);
        Assert.Equal(new[] { "task-1", "task-2" }, c.Tasks.Select(t => t.Name));
    }

    [Fact]
    public void When_accepts_a_scalar_or_a_list()
    {
        var yaml = string.Join("\n",
            "setup:",
            "  - run: apt-get install -y libfoo",
            "    when: linux",
            "  - run: brew install foo",
            "    when: [macos]",
            "run: ./app");
        var c = ConfigParser.Parse(yaml, OSKind.Linux);
        Assert.Equal(new[] { "linux" }, c.Setup[0].When);
        Assert.Equal(new[] { "macos" }, c.Setup[1].When);
    }

    [Fact]
    public void Full_form_is_read_verbatim()
    {
        var yaml = string.Join("\n",
            "version: 1",
            "name: My App",
            "requires:",
            "  - tool: dotnet",
            "    version: \">=9.0\"",
            "    install: https://dot.net",
            "inputs:",
            "  - id: apiKey",
            "    type: password",
            "    required: true",
            "    env: OPENAI_API_KEY",
            "env:",
            "  ASPNETCORE_ENVIRONMENT: Development",
            "tasks:",
            "  - name: db",
            "    run: docker compose up -d db",
            "    readyWhen: {port: 5432}",
            "  - name: api",
            "    run: dotnet run",
            "    dependsOn: [db]",
            "    readyWhen: {http: \"http://localhost:5000\"}",
            "    open: true",
            "    restart: onFailure",
            "stop:",
            "  - docker compose down");
        var c = ConfigParser.Parse(yaml, OSKind.Linux);

        Assert.Equal("My App", c.Name);
        Assert.Equal(">=9.0", c.Requires[0].Version);
        Assert.False(c.Requires[0].Optional);
        Assert.Equal(InputType.Password, c.Inputs[0].Type);
        Assert.True(c.Inputs[0].Required);
        Assert.Equal("OPENAI_API_KEY", c.Inputs[0].Env);
        Assert.Equal("Development", c.Env["ASPNETCORE_ENVIRONMENT"]);
        Assert.Equal(5432, c.Tasks[0].ReadyWhen!.Port);
        Assert.Equal(new[] { "db" }, c.Tasks[1].DependsOn);
        Assert.True(c.Tasks[1].OpenReady);
        Assert.Null(c.Tasks[1].OpenUrl);
        Assert.Equal(RestartPolicy.OnFailure, c.Tasks[1].Restart);
        Assert.Equal("docker compose down", Assert.Single(c.Stop).Run);
    }

    [Fact]
    public void Open_with_a_url_sets_OpenUrl_and_not_OpenReady()
    {
        var c = ConfigParser.Parse("tasks:\n  - run: npm run dev\n    open: http://localhost:5173", OSKind.Linux);
        Assert.False(c.Tasks[0].OpenReady);
        Assert.Equal("http://localhost:5173", c.Tasks[0].OpenUrl);
    }

    [Fact]
    public void ReadyWhen_delay_parses_a_duration()
    {
        var c = ConfigParser.Parse("tasks:\n  - run: ./slow\n    readyWhen: {delay: 5s}", OSKind.Linux);
        Assert.Equal(TimeSpan.FromSeconds(5), c.Tasks[0].ReadyWhen!.Delay);
    }

    [Theory]
    [InlineData("500ms", 0.5)]
    [InlineData("2m", 120)]
    [InlineData("30", 30)]
    public void ReadyWhen_delay_understands_the_documented_units(string text, double expectedSeconds)
    {
        var c = ConfigParser.Parse($"tasks:\n  - run: ./slow\n    readyWhen: {{delay: {text}}}", OSKind.Linux);
        Assert.Equal(expectedSeconds, c.Tasks[0].ReadyWhen!.Delay!.Value.TotalSeconds, 3);
    }

    [Fact]
    public void ReadyWhen_with_more_than_one_condition_throws()
    {
        Assert.Throws<ConfigException>(() =>
            ConfigParser.Parse("tasks:\n  - run: a\n    readyWhen: {port: 1, delay: 5s}", OSKind.Linux));
    }

    [Fact]
    public void Malformed_yaml_throws_ConfigException_not_a_yaml_exception()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("run: [unclosed", OSKind.Linux));
    }

    [Fact]
    public void Unknown_top_level_keys_throw()
    {
        var ex = Assert.Throws<ConfigException>(() => ConfigParser.Parse("runn: ./run.sh", OSKind.Linux));
        Assert.Contains("runn", ex.Message);
    }

    [Fact]
    public void Unknown_task_keys_throw()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("tasks:\n  - run: a\n    reddyWhen: x", OSKind.Linux));
    }

    [Fact]
    public void Both_run_and_tasks_is_rejected()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("run: ./a\ntasks: [./b]", OSKind.Linux));
    }

    [Fact]
    public void An_input_id_that_is_not_an_identifier_throws()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("inputs:\n  - id: not-an-identifier\nrun: a", OSKind.Linux));
    }

    [Fact]
    public void An_input_without_an_id_throws()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("inputs:\n  - label: Nameless\nrun: a", OSKind.Linux));
    }

    [Fact]
    public void A_bare_string_requirement_means_just_the_tool_name()
    {
        var c = ConfigParser.Parse("requires: [docker]\nrun: a", OSKind.Linux);
        var r = Assert.Single(c.Requires);
        Assert.Equal("docker", r.Tool);
        Assert.Null(r.Version);
    }

    [Fact]
    public void Select_options_accept_scalars_and_value_label_mappings()
    {
        var yaml = string.Join("\n",
            "inputs:",
            "  - id: mode",
            "    type: select",
            "    options:",
            "      - dev",
            "      - value: prod",
            "        label: Production",
            "run: a");
        var options = ConfigParser.Parse(yaml, OSKind.Linux).Inputs[0].Options;
        Assert.Equal("dev", options[0].Value);
        Assert.Null(options[0].Label);
        Assert.Equal("prod", options[1].Value);
        Assert.Equal("Production", options[1].Label);
    }

    [Fact]
    public void An_empty_config_parses_to_nothing_to_run()
    {
        var c = ConfigParser.Parse("name: Empty", OSKind.Linux);
        Assert.Empty(c.Tasks);
        Assert.Empty(c.Setup);
    }

    [Fact]
    public void Restart_never_is_the_default()
    {
        Assert.Equal(RestartPolicy.Never, ConfigParser.Parse("run: a", OSKind.Linux).Tasks[0].Restart);
    }

    [Fact]
    public void An_unknown_restart_policy_throws()
    {
        Assert.Throws<ConfigException>(() => ConfigParser.Parse("tasks:\n  - run: a\n    restart: always", OSKind.Linux));
    }
}
