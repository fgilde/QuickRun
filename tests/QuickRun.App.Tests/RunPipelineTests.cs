using QuickRun.App.Commands;
using QuickRun.Core.Config;
using QuickRun.Core.Git;
using QuickRun.Core.Process;
using QuickRun.Core.Tests;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

public class RunPipelineTests
{
    /// <summary>A client whose credential chain finds nothing, so tests never read real credentials.</summary>
    private static GitClient Git() =>
        new(new CredentialResolver(null, (_, _) => new CommandResult(1, "", false), _ => null));

    private static RunArgs Args(string repo, string? @ref = "main", params string[] inputs) =>
        new(repo, @ref, null, null, inputs, null, false, true, false, null);

    private static RunPreparation Prepare(RunArgs args, TempHome home,
        IReadOnlyDictionary<string, string?>? answers = null) =>
        RunPipeline.Prepare(args, new WorkspaceStore(home.Path), Git(),
            (_, provided) => answers ?? provided);

    [Fact]
    public void A_repository_with_a_config_produces_a_plan()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "name: Demo\nrun: echo hi\n");
        repo.Commit("add config");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(0, preparation.ExitCode);
        Assert.Equal("Demo", preparation.Plan!.DisplayName);
        Assert.Equal("echo hi", Assert.Single(preparation.Plan.Commands).Command);
        Assert.Equal(repo.Head(), preparation.Plan.Commit);
    }

    [Fact]
    public void The_workspace_is_created_under_the_store_root()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.StartsWith(home.Path, preparation.Workspace!);
        Assert.True(File.Exists(Path.Combine(preparation.Workspace!, "quickrun.yml")));
    }

    [Fact]
    public void A_repository_without_a_config_falls_back_to_detection()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("package.json", "{\"scripts\":{\"dev\":\"vite\"}}");
        repo.Commit("add package.json");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(0, preparation.ExitCode);
        Assert.Contains(preparation.Plan!.Commands, c => c.Command.Contains("npm run dev"));
    }

    [Fact]
    public void A_repository_with_neither_config_nor_detectable_entry_point_fails()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(1, preparation.ExitCode);
        Assert.Contains("quickrun.yml", preparation.Error!);
    }

    [Fact]
    public void A_root_run_script_is_used_when_there_is_no_config()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("run.sh", "#!/bin/sh\necho from-script\n");
        repo.Commit("add script");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(0, preparation.ExitCode);
        Assert.Contains("run.sh", Assert.Single(preparation.Plan!.Commands).Command);
    }

    [Fact]
    public void An_invalid_config_fails_before_any_plan_is_built()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "tasks:\n  - name: a\n    run: x\n    dependsOn: [nope]\n");
        repo.Commit("add bad config");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(1, preparation.ExitCode);
        Assert.Null(preparation.Plan);
    }

    [Fact]
    public void Malformed_yaml_fails_with_the_file_name()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: [unclosed\n");
        repo.Commit("add broken config");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(1, preparation.ExitCode);
        Assert.Contains("quickrun.yml", preparation.Error!);
    }

    [Fact]
    public void Input_assignments_are_interpolated_into_the_plan()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml",
            "inputs:\n  - id: apiKey\n    required: true\nrun: ./app --key ${inputs.apiKey}\n");
        repo.Commit("add config");

        var preparation = Prepare(Args(repo.Url, "main", "apiKey=sk-1"), home);

        Assert.Equal(0, preparation.ExitCode);
        Assert.Equal("./app --key sk-1", Assert.Single(preparation.Plan!.Commands).Command);
    }

    [Fact]
    public void A_missing_required_input_fails_when_the_collector_supplies_nothing()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "inputs:\n  - id: apiKey\n    required: true\nrun: ./app\n");
        repo.Commit("add config");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(1, preparation.ExitCode);
        Assert.Contains("apiKey", preparation.Error!);
    }

    [Fact]
    public void The_collector_can_supply_a_missing_required_input()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml",
            "inputs:\n  - id: apiKey\n    required: true\nrun: ./app --key ${inputs.apiKey}\n");
        repo.Commit("add config");

        var preparation = Prepare(Args(repo.Url), home,
            answers: new Dictionary<string, string?> { ["apiKey"] = "sk-prompted" });

        Assert.Equal(0, preparation.ExitCode);
        Assert.Contains("sk-prompted", preparation.Plan!.Commands[0].Command);
    }

    [Fact]
    public void Defaults_satisfy_a_required_input_without_prompting()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml",
            "inputs:\n  - id: mode\n    required: true\n    default: dev\nrun: ./app --mode ${inputs.mode}\n");
        repo.Commit("add config");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(0, preparation.ExitCode);
        Assert.Contains("--mode dev", preparation.Plan!.Commands[0].Command);
    }

    [Fact]
    public void An_unknown_ref_fails_with_the_git_error()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        var preparation = Prepare(Args(repo.Url, "no-such-branch"), home);

        Assert.Equal(1, preparation.ExitCode);
        Assert.NotNull(preparation.Error);
    }

    [Fact]
    public void A_subdir_scopes_the_config_lookup()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("web/quickrun.yml", "name: Web\nrun: npm run dev\n");
        repo.Commit("add nested config");

        var preparation = RunPipeline.Prepare(
            new RunArgs(repo.Url, "main", null, "web", Array.Empty<string>(), null, false, true, false, null),
            new WorkspaceStore(home.Path), Git(), (_, provided) => provided);

        Assert.Equal(0, preparation.ExitCode);
        Assert.Equal("Web", preparation.Plan!.DisplayName);
    }

    [Fact]
    public void A_subdir_escaping_the_repository_is_a_usage_error()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var preparation = RunPipeline.Prepare(
            new RunArgs(repo.Url, "main", null, "../../etc", Array.Empty<string>(), null, false, true, false, null),
            new WorkspaceStore(home.Path), Git(), (_, provided) => provided);

        Assert.Equal(2, preparation.ExitCode);
        Assert.Contains("outside", preparation.Error!);
    }

    [Fact]
    public void A_malformed_input_assignment_returns_usage_failure()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: ./app\n");
        repo.Commit("add config");

        Assert.Equal(2, Prepare(Args(repo.Url, "main", "no-equals-sign"), home).ExitCode);
    }

    [Fact]
    public void An_unsupported_repository_shorthand_returns_usage_failure()
    {
        using var home = new TempHome();
        Assert.Equal(2, Prepare(Args("javascript:alert(1)"), home).ExitCode);
    }

    [Fact]
    public void The_workspace_metadata_records_the_commit()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        Prepare(Args(repo.Url), home);

        Assert.Equal(repo.Head(), Assert.Single(new WorkspaceStore(home.Path).List()).LastCommit);
    }

    [Fact]
    public void Other_detection_candidates_are_reported_alongside_the_chosen_one()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("docker-compose.yml", "services: {}");
        repo.Write("package.json", "{\"scripts\":{\"dev\":\"vite\"}}");
        repo.Commit("add two entry points");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(0, preparation.ExitCode);
        Assert.Contains("docker compose up", preparation.Plan!.Commands[0].Command);
        Assert.NotEmpty(preparation.OtherCandidates);
    }

    /// <summary>
    /// Your own config for a repository beats the one the repository ships, and the run says so -
    /// otherwise a run that ignores a committed quickrun.yml is a mystery.
    /// </summary>
    [Fact]
    public void A_saved_override_wins_over_the_repositorys_own_config()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "name: Theirs\nrun: echo theirs\n");
        repo.Commit("add config");

        new ConfigOverrides(home.Path).Write(repo.Url, "name: Mine\nrun: echo mine\n");

        var preparation = Prepare(Args(repo.Url), home);

        Assert.Equal(0, preparation.ExitCode);
        Assert.Equal("Mine", preparation.Plan!.DisplayName);
        Assert.Equal("echo mine", Assert.Single(preparation.Plan.Commands).Command);
        Assert.Contains(preparation.Notes, n => n.Contains("your local config"));
        Assert.Contains(preparation.Notes, n => n.Contains("quickrun.yml it ships"));
    }

    [Fact]
    public void A_config_from_the_editor_wins_over_everything()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "name: Theirs\nrun: echo theirs\n");
        repo.Commit("add config");

        new ConfigOverrides(home.Path).Write(repo.Url, "name: Mine\nrun: echo mine\n");

        var args = Args(repo.Url) with { ConfigText = "name: Editing\nrun: echo editing\n" };
        var preparation = Prepare(args, home);

        Assert.Equal("Editing", preparation.Plan!.DisplayName);
        Assert.Equal("echo editing", Assert.Single(preparation.Plan.Commands).Command);
    }

    [Fact]
    public void A_broken_config_from_the_editor_fails_with_the_reason()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("readme.md", "nothing to run");
        repo.Commit("init");

        var preparation = Prepare(Args(repo.Url) with { ConfigText = "tasks: [" }, home);

        Assert.NotEqual(0, preparation.ExitCode);
        Assert.Contains("the config you supplied", preparation.Error);
    }

    /// <summary>An override for another repository must not leak into this one.</summary>
    [Fact]
    public void An_override_for_a_different_repository_is_ignored()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "name: Theirs\nrun: echo theirs\n");
        repo.Commit("add config");

        new ConfigOverrides(home.Path).Write("https://github.com/someone/else", "run: echo wrong\n");

        Assert.Equal("Theirs", Prepare(Args(repo.Url), home).Plan!.DisplayName);
    }
}
