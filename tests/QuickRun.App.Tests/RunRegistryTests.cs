using QuickRun.App.Commands;
using QuickRun.App.Daemon;
using QuickRun.Core.Config;
using QuickRun.Core;
using QuickRun.Core.Run;
using QuickRun.Core.Tests;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Tests;

public class RunRegistryTests
{
    private static RunArgs Args(string repo, string? config = null) =>
        new(repo, "main", null, null, Array.Empty<string>(), null, false, true, true, config);

    [Fact]
    public async Task Preparing_returns_the_plan_and_executes_nothing()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        var marker = Path.Combine(repo.Path, "must-not-exist.txt");

        // If anything ran, this file would be created.
        var command = OSKinds.Current == OSKind.Windows
            ? "type nul > must-not-exist.txt"
            : "touch must-not-exist.txt";
        repo.Write("quickrun.yml", $"run: {command}\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (summary, error) = await registry.PrepareAsync(Args(repo.Url));

        Assert.Null(error);
        Assert.Equal(RunState.AwaitingConfirmation, summary!.State);
        Assert.Single(summary.Commands);
        Assert.False(File.Exists(marker));
        Assert.False(File.Exists(Path.Combine(summary.Commands[0].Cwd ?? home.Path, "must-not-exist.txt")));
    }

    [Fact]
    public async Task The_summary_carries_the_fingerprint_the_trust_store_will_hash()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var (summary, _) = await new RunRegistry(new WorkspaceStore(home.Path)).PrepareAsync(Args(repo.Url));

        Assert.Equal(64, summary!.Fingerprint.Length);
    }

    [Fact]
    public async Task A_repository_without_anything_runnable_fails_preparation()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (summary, error) = await registry.PrepareAsync(Args(repo.Url));

        Assert.NotNull(error);
        Assert.Equal(RunState.Failed, summary!.State);
    }

    [Fact]
    public async Task Confirming_an_unknown_run_is_refused()
    {
        using var home = new TempHome();
        await Task.CompletedTask;
        Assert.False(new RunRegistry(new WorkspaceStore(home.Path)).Confirm("nope"));
    }

    [Fact]
    public async Task A_run_can_only_be_confirmed_once()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (summary, _) = await registry.PrepareAsync(Args(repo.Url));

        Assert.True(registry.Confirm(summary!.Id));
        Assert.False(registry.Confirm(summary.Id));
    }

    [Fact]
    public async Task Confirming_runs_the_commands_and_streams_events()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo listener-ran\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (summary, _) = await registry.PrepareAsync(Args(repo.Url));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var collected = new List<RunEvent>();
        var reading = Task.Run(async () =>
        {
            await foreach (var e in registry.Subscribe(summary!.Id, cts.Token)) collected.Add(e);
        }, cts.Token);

        Assert.True(registry.Confirm(summary!.Id));

        try { await reading; } catch (OperationCanceledException) { }

        Assert.Contains(collected, e => e.Text.Contains("listener-ran"));
        Assert.Contains(collected, e => e.Kind == RunEventKind.Progress);
        Assert.Equal(RunState.Succeeded, registry.Get(summary.Id)!.State);
    }

    [Fact]
    public async Task A_late_subscriber_receives_the_history()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo replayed\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (summary, _) = await registry.PrepareAsync(Args(repo.Url));
        registry.Confirm(summary!.Id);

        // Wait for the run to finish before subscribing at all.
        while (registry.Get(summary.Id)!.State == RunState.Running) await Task.Delay(100);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var collected = new List<RunEvent>();
        try
        {
            await foreach (var e in registry.Subscribe(summary.Id, cts.Token)) collected.Add(e);
        }
        catch (OperationCanceledException) { }

        Assert.Contains(collected, e => e.Text.Contains("replayed"));
    }

    [Fact]
    public async Task AnyActive_reflects_whether_something_is_pending()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        Assert.False(registry.AnyActive);

        await registry.PrepareAsync(Args(repo.Url));
        Assert.True(registry.AnyActive);
    }

    [Fact]
    public async Task The_summary_says_where_the_workspace_is()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var (summary, _) = await new RunRegistry(new WorkspaceStore(home.Path)).PrepareAsync(Args(repo.Url));

        // The window that shows the log shows this path, so it has to survive the round trip.
        Assert.NotNull(summary!.Workspace);
        Assert.True(Directory.Exists(summary.Workspace));
    }

    [Fact]
    public async Task The_address_a_task_reports_is_lifted_out_of_the_log_and_opened_once()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        // A task that is ready as soon as its port answers, with an explicit address to open.
        repo.Write("quickrun.yml", """
            tasks:
              - name: web
                run: echo serving
                open: http://localhost:65123/app
            """);
        repo.Commit("add config");

        var opened = new List<string>();
        var registry = new RunRegistry(new WorkspaceStore(home.Path), opened.Add);
        var (summary, _) = await registry.PrepareAsync(Args(repo.Url));

        registry.Confirm(summary!.Id);
        while (registry.Get(summary.Id)!.State == RunState.Running) await Task.Delay(100);

        Assert.Equal("http://localhost:65123/app", registry.Get(summary.Id)!.Url);
        Assert.Equal(new[] { "http://localhost:65123/app" }, opened);
    }

    [Fact]
    public async Task A_finished_run_reports_no_live_tasks()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (summary, _) = await registry.PrepareAsync(Args(repo.Url));

        registry.Confirm(summary!.Id);
        while (registry.Get(summary.Id)!.State == RunState.Running) await Task.Delay(100);

        // What the Stop button keys on: nothing is left to stop.
        Assert.Equal(0, registry.Get(summary.Id)!.LiveTasks);
    }

    [Fact]
    public async Task Stopping_an_unknown_run_is_refused()
    {
        using var home = new TempHome();
        await Task.CompletedTask;
        Assert.False(new RunRegistry(new WorkspaceStore(home.Path)).Stop("nope"));
    }

    [Fact]
    public async Task Subscribing_to_an_unknown_run_yields_nothing()
    {
        using var home = new TempHome();
        var registry = new RunRegistry(new WorkspaceStore(home.Path));

        var collected = new List<RunEvent>();
        await foreach (var e in registry.Subscribe("nope", CancellationToken.None)) collected.Add(e);

        Assert.Empty(collected);
    }

    /// <summary>
    /// What the config says the repository is, so the window that asks for confirmation can show it
    /// instead of a bare name.
    /// </summary>
    [Fact]
    public async Task The_summary_carries_the_configs_description()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "name: Demo\ndescription: Starts the thing and opens it.\nrun: echo hi\n");
        repo.Commit("add config");

        var (summary, _) = await new RunRegistry(new WorkspaceStore(home.Path)).PrepareAsync(Args(repo.Url));

        Assert.Equal("Starts the thing and opens it.", summary!.Description);
    }

    [Fact]
    public async Task A_config_without_a_description_says_nothing()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var (summary, _) = await new RunRegistry(new WorkspaceStore(home.Path)).PrepareAsync(Args(repo.Url));

        Assert.Null(summary!.Description);
    }

    /// <summary>
    /// Stopping has to be visible. The runner announces finishing and failing itself, but a stopped
    /// run used to return in silence - which left a log window saying "Running" for ever.
    /// </summary>
    [Fact]
    public async Task Stopping_a_run_ends_the_stream_with_a_cancelled_event()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();

        var sleep = OSKinds.Current == OSKind.Windows
            ? "powershell -NoProfile -Command Start-Sleep -Seconds 30"
            : "sleep 30";
        repo.Write("quickrun.yml", $"tasks:\n  - name: app\n    run: {sleep}\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (summary, _) = await registry.PrepareAsync(Args(repo.Url));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var collected = new List<RunEvent>();
        var reading = Task.Run(async () =>
        {
            await foreach (var e in registry.Subscribe(summary!.Id, cts.Token)) collected.Add(e);
        }, cts.Token);

        Assert.True(registry.Confirm(summary!.Id));

        // Wait for the task to be up before stopping it, so this tests stopping rather than racing.
        while (registry.Get(summary.Id)!.LiveTasks == 0 && !cts.IsCancellationRequested)
            await Task.Delay(50, cts.Token);

        Assert.True(registry.Stop(summary.Id));

        try { await reading; } catch (OperationCanceledException) { }

        Assert.Contains(collected, e => e.Kind == RunEventKind.Cancelled);
        Assert.Equal(RunState.Cancelled, registry.Get(summary.Id)!.State);
        Assert.Equal(0, registry.Get(summary.Id)!.LiveTasks);
    }

    /// <summary>
    /// A config whose inputs have no values is a form to fill in, not a broken config: the run
    /// waits, and says which fields it needs.
    /// </summary>
    [Fact]
    public async Task Missing_input_values_leave_the_run_waiting_with_its_form()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml",
            "inputs:\n  - id: apiKey\n    label: API key\n    type: password\n    required: true\n"
            + "  - id: mode\n    type: select\n    options: [fast, slow]\n    default: fast\n"
            + "tasks:\n  - name: t\n    run: echo ${inputs.mode}\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (summary, error) = await registry.PrepareAsync(Args(repo.Url));

        Assert.Contains("apiKey", error);
        Assert.Equal(RunState.AwaitingInput, summary!.State);
        Assert.Equal(new[] { "apiKey", "mode" }, summary.Inputs!.Select(i => i.Id));
        Assert.Equal(InputType.Select, summary.Inputs![1].Type);
        Assert.Empty(summary.Commands);

        // The default is offered back, the secret is not: a password must not travel out again.
        Assert.Equal("fast", summary.Values!["mode"]);
        Assert.Null(summary.Values["apiKey"]);
    }

    [Fact]
    public async Task Supplying_the_values_plans_the_same_run_with_them()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml",
            "inputs:\n  - id: apiKey\n    type: password\n    required: true\n    env: API_KEY\n"
            + "  - id: mode\n    type: select\n    options: [fast, slow]\n    default: fast\n"
            + "tasks:\n  - name: t\n    run: echo ${inputs.mode}\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (prepared, _) = await registry.PrepareAsync(Args(repo.Url));

        var (summary, error) = await registry.SupplyInputsAsync(prepared!.Id,
            new Dictionary<string, string?> { ["apiKey"] = "sk-secret", ["mode"] = "slow" });

        Assert.Null(error);
        Assert.Equal(prepared.Id, summary!.Id);
        Assert.Equal(RunState.AwaitingConfirmation, summary.State);
        Assert.Equal("echo slow", Assert.Single(summary.Commands).Command);
        Assert.Null(summary.Values!["apiKey"]);
        Assert.Null(summary.Error);
    }

    [Fact]
    public async Task Supplying_values_that_are_still_wrong_keeps_the_run_waiting()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml",
            "inputs:\n  - id: apiKey\n    type: password\n    required: true\n"
            + "tasks:\n  - name: t\n    run: echo hi\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (prepared, _) = await registry.PrepareAsync(Args(repo.Url));

        var (summary, error) = await registry.SupplyInputsAsync(prepared!.Id,
            new Dictionary<string, string?> { ["apiKey"] = "" });

        Assert.NotNull(error);
        Assert.Equal(RunState.AwaitingInput, summary!.State);
    }

    [Fact]
    public async Task Inputs_cannot_be_supplied_to_a_run_that_has_started()
    {
        using var repo = new LocalRepo();
        using var home = new TempHome();
        repo.Write("quickrun.yml", "run: echo hi\n");
        repo.Commit("add config");

        var registry = new RunRegistry(new WorkspaceStore(home.Path));
        var (prepared, _) = await registry.PrepareAsync(Args(repo.Url));
        Assert.True(registry.Confirm(prepared!.Id));

        var (_, error) = await registry.SupplyInputsAsync(prepared.Id,
            new Dictionary<string, string?> { ["x"] = "y" });

        Assert.Equal("this run has already started", error);
    }

    [Fact]
    public async Task Inputs_for_an_unknown_run_are_refused()
    {
        using var home = new TempHome();
        var (summary, error) = await new RunRegistry(new WorkspaceStore(home.Path))
            .SupplyInputsAsync("nope", new Dictionary<string, string?>());

        Assert.Null(summary);
        Assert.Equal("unknown run", error);
    }
}
