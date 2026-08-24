using QuickRun.Core.Process;

namespace QuickRun.Core.Git;

/// <summary>
/// Finds a token for a host, first hit wins. Every step is guarded so a missing tool is never fatal.
/// </summary>
/// <remarks>
/// ponytail: two of the five sources in the spec. The OS credential store is written by the desktop
/// UI (phase 2), and `git credential fill` needs stdin plumbing that only pays off once someone
/// reports the gh path is not enough. Plain git with no token stays the final fallback, which is
/// what already covers SSH remotes and Git Credential Manager.
/// </remarks>
public sealed class CredentialResolver(
    string? explicitToken = null,
    Func<string, string[], CommandResult>? runner = null,
    Func<string, string?>? envLookup = null)
{
    private readonly Func<string, string[], CommandResult> _run =
        runner ?? ((file, args) => CommandRunner.Capture(file, args, timeoutMs: 10_000));

    private readonly Func<string, string?> _env = envLookup ?? Environment.GetEnvironmentVariable;

    public string? Resolve(string host)
    {
        if (!string.IsNullOrWhiteSpace(explicitToken)) return explicitToken;

        var fromEnvironment = _env("QUICKRUN_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;

        // The user may only be logged into the GitHub CLI.
        var fromGh = _run("gh", new[] { "auth", "token" });
        if (fromGh.ExitCode == 0 && fromGh.Output.Trim().Length > 0) return fromGh.Output.Trim();

        return null;
    }
}
