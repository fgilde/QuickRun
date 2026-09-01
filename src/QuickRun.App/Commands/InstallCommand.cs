using System.ComponentModel;
using Microsoft.AspNetCore.Http;
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
        // reaches this machine. It is carried to the window, which prepares the plan and asks for
        // confirmation there. Nothing about a link may start anything: the page that produced it is
        // untrusted, and the window that asks is not.
        var target = RunTarget.From(url);

        // This process is not QuickRun. It was started by the operating system for one link, it has
        // no tray icon and no window, and if it became the daemon there would be a QuickRun running
        // that nobody can see or quit - which is exactly what it used to do.
        if (!await SingleInstance.RunningAsync(settings.Port, cancellationToken))
        {
            Output.Info("starting QuickRun");

            if (!SingleInstance.Start(settings.Port))
            {
                Output.Error("could not start QuickRun");
                return 1;
            }

            if (!await SingleInstance.WaitAsync(settings.Port, TimeSpan.FromSeconds(20), cancellationToken))
            {
                Output.Error("QuickRun did not answer after starting");
                return 1;
            }
        }

        // Its own window, raised by the instance that owns it. Falling back to the browser matters:
        // an older build has no /api/show, and a QuickRun started with --no-tray has no window to
        // raise - in both cases the page is still there to open.
        if (!await SingleInstance.ShowAsync(settings.Port, target, cancellationToken))
            UiCommand.Launch(Dashboard(settings.Port, target));

        return 0;
    }

    private static string Dashboard(int port, string? target) =>
        target is null
            ? $"http://127.0.0.1:{port}/"
            : $"http://127.0.0.1:{port}/#run?{target}";
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
        var parsed = System.Web.HttpUtility.ParseQueryString(url.Query);

        // quickrun://run?repo=... and quickrun://run/?repo=... are the same thing to a browser.
        if (string.Equals(url.Host, "run", StringComparison.OrdinalIgnoreCase))
            return From(name => parsed[name]);

        // quickrun://runfile?path=... names a config file on this machine. Carried, because it was
        // asked for - and carried as "file", which is what tells the window this came from a link
        // rather than from its own picker. The window says so before the commands, and the daemon
        // refuses to fall back to the file's own directory for it.
        if (string.Equals(url.Host, "runfile", StringComparison.OrdinalIgnoreCase))
            return FromFile(parsed["path"]);

        return null;
    }

    /// <summary>
    /// The config file a link names, or null when it is not one.
    /// <para>
    /// Checked here and again in the daemon. A link is written by somebody else, and this one points
    /// at a file on the reader's own disk - the narrowest thing that can still be useful is a .yml
    /// path and nothing else.
    /// </para>
    /// </summary>
    private static string? FromFile(string? path)
    {
        var file = (path ?? "").Trim();

        if (file.Length == 0 || file.Length > 400) return null;
        if (file.Any(char.IsControl)) return null;
        if (file.Contains("://", StringComparison.Ordinal)) return null;

        if (!file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            && !file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"file={Uri.EscapeDataString(file)}";
    }

    /// <summary>The same target out of a request's query string, for <c>/api/show</c>.</summary>
    /// <param name="allowFile">
    /// Whether a path on this machine may be named. True for the extension and for a
    /// <c>quickrun://</c> link, which is somebody clicking a link they were given; false for a web
    /// page, however trusted - a page naming a file on the reader's disk is the thing the origin
    /// rules exist to prevent, and trusting a site to open a window is not trusting it with that.
    /// </param>
    public static string? FromQuery(IQueryCollection query, bool allowFile = true) =>
        query["file"].FirstOrDefault() is { Length: > 0 } file
            ? allowFile ? FromFile(file) : null
            : From(name => query[name].FirstOrDefault());

    private static string? From(Func<string, string?> query)
    {
        var repo = query("repo");
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

        if (query("ref") is { Length: > 0 } reference && reference.Length < 250)
            carried.Add($"ref={Uri.EscapeDataString(reference)}");

        if (int.TryParse(query("pr"), out var pr) && pr > 0)
            carried.Add($"pr={pr}");

        // Which config was asked for. A name, not a config: "the one QuickRun keeps for this
        // repository", which QuickRun then fetches itself. Without carrying it, a link that says
        // "run it with our config" would open the window on the repository's own.
        if (string.Equals(query("config"), "collection", StringComparison.OrdinalIgnoreCase))
            carried.Add("config=collection");

        return string.Join('&', carried);
    }
}
