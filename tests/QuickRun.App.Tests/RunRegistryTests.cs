using QuickRun.App.Commands;
using QuickRun.App.Daemon;
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
}
