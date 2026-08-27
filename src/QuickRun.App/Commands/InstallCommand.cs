using System.ComponentModel;
using QuickRun.Core.Git;
using QuickRun.App.Daemon;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed class InstallCommand : Command<InstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-p|--port")]
        [Description("Port the autostarted daemon should listen on.")]
        public int Port { get; init; } = DaemonHost.DefaultPort;
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath;
        if (executable is null)
        {
            Output.Error("cannot determine the running executable");
            return 1;
        }

        var steps = SystemIntegration.Install(executable, settings.Port);
        Report(steps);

        Output.Info("");
        Output.Info("next: quickrun pair    # then click Pair in the browser extension");

        // A failed step is worth reporting but does not make the install useless: the CLI works
        // either way, and so does the listener once it is started by hand.
        return steps.All(s => s.Ok) ? 0 : 1;
    }

    internal static void Report(IReadOnlyList<IntegrationStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Ok) Output.Info($"{step.What}: {step.Detail}");
            else Output.Warn($"{step.What} failed: {step.Detail}");
        }
    }
}

public sealed class UninstallCommand : Command<UninstallCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var steps = SystemIntegration.Uninstall();
        InstallCommand.Report(steps);

        Output.Info("");
        Output.Info("workspaces are kept - remove them with: quickrun clean --all");

        return steps.All(s => s.Ok) ? 0 : 1;
    }
}

/// <summary>
/// Handles a <c>quickrun://</c> URL. Registered by <see cref="InstallCommand"/> as the scheme's
/// handler, and invoked by the OS when the extension opens such a link.
/// </summary>
public sealed class HandleCommand : AsyncCommand<HandleCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<url>")]
        [Description("A quickrun:// URL.")]
        public string Url { get; init; } = "";

        [CommandOption("-p|--port")]
        public int Port { get; init; } = DaemonHost.DefaultPort;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(settings.Url, UriKind.Absolute, out var url)
            || !string.Equals(url.Scheme, "quickrun", StringComparison.OrdinalIgnoreCase))
        {
            Output.Error($"not a quickrun:// URL: {settings.Url}");
            return 2;
        }

        // A repository named in the URL - quickrun://run?repo=owner/name - is how a README badge
        // reaches this machine. It is carried to the local window, which prepares the plan and asks
        // for confirmation there. Nothing about a link may start anything: the page that produced
        // it is untrusted, and the window that asks is not.
        var target = RunTarget.From(url);

        if (await Reachable(settings.Port, cancellationToken))
        {
            // Already running, so this process has nothing to host - it only points the window at
            // the repository and gets out of the way.
            if (target is not null) UiCommand.Launch(Dashboard(settings.Port, target));
            else UiCommand.Launch(Dashboard(settings.Port, null));

            return 0;
        }

        Output.Info($"starting the QuickRun daemon on port {settings.Port}");

        var daemon = new DaemonCommand();

        // Opened once the listener answers, because a browser sent to a port nothing is listening
        // on shows its own error page and the moment is gone.
        if (target is not null)
            _ = Task.Run(async () =>
            {
                if (await Reachable(settings.Port, cancellationToken, attempts: 40))
                    UiCommand.Launch(Dashboard(settings.Port, target));
            }, cancellationToken);

        return await daemon.RunAsync(new DaemonCommand.Settings { Port = settings.Port }, cancellationToken);
    }

    private static string Dashboard(int port, string? target) =>
        target is null
            ? $"http://127.0.0.1:{port}/"
            : $"http://127.0.0.1:{port}/#run?{target}";

    private static async Task<bool> Reachable(int port, CancellationToken ct, int attempts = 1)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/ping", ct);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // Not up yet, or not up at all.
            }

            if (attempt + 1 < attempts) await Task.Delay(250, ct);
        }

        return false;
    }
}

/// <summary>
/// The repository a <c>quickrun://</c> URL names, reduced to what the local window understands.
/// <para>
/// Anything a link can carry is written by whoever wrote the link, so only three fields survive and
/// each is checked: a repository shorthand or https URL, a ref, a pull request number. No token, no
/// config text, no local path - a link may say what to look at, never what to execute or with whose
/// credentials.
/// </para>
/// </summary>
internal static class RunTarget
{
    public static string? From(Uri url)
    {
        // quickrun://run?repo=... and quickrun://run/?repo=... are the same thing to a browser.
        if (!string.Equals(url.Host, "run", StringComparison.OrdinalIgnoreCase)) return null;

        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        var repo = query["repo"];
        if (string.IsNullOrWhiteSpace(repo)) return null;

        // Narrower than what the CLI accepts, on purpose: typing file:// or ssh:// yourself is a
        // choice, a link doing it is a stranger's. What a link may name is an https URL or the
        // owner/name shorthand that turns into one.
        if (!repo.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && (repo.Contains("://", StringComparison.Ordinal)
                || repo.Contains(':', StringComparison.Ordinal)
                || repo.Contains('@', StringComparison.Ordinal)))
            return null;

        try { GitClient.NormalizeRepoUrl(repo); }
        catch (ArgumentException) { return null; }

        var carried = new List<string> { $"repo={Uri.EscapeDataString(repo)}" };

        if (query["ref"] is { Length: > 0 } reference && reference.Length < 250)
            carried.Add($"ref={Uri.EscapeDataString(reference)}");

        if (int.TryParse(query["pr"], out var pr) && pr > 0)
            carried.Add($"pr={pr}");

        return string.Join('&', carried);
    }
}
