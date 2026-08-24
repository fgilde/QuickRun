using QuickRun.Core.Process;

namespace QuickRun.Core.Git;

public sealed record GitOutcome(bool Ok, string? Error, string? Commit);

/// <summary>
/// Checks repositories out into a workspace. Every error string that leaves this class has been
/// through <see cref="Scrub"/> - that is the invariant the file exists to hold.
/// </summary>
public sealed class GitClient(
    CredentialResolver credentials,
    Func<string, string[], string?, CommandResult>? runner = null)
{
    private const int CloneTimeoutMs = 300_000;

    /// <summary>Left in place across updates so a second start takes seconds, not minutes.</summary>
    private static readonly string[] PreservedPaths =
        { "node_modules", ".venv", "venv", "obj", "bin", "target", "vendor", ".gradle", "__pycache__" };

    /// <summary>
    /// Keeps credential helpers usable but silent. GIT_TERMINAL_PROMPT only suppresses terminal
    /// prompts - Git Credential Manager opens a GUI dialog and waits forever, which for a
    /// background daemon means an invisible hang. Stored credentials are still returned; only
    /// prompting is off.
    /// </summary>
    private static readonly Dictionary<string, string> NonInteractive = new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GCM_INTERACTIVE"] = "never",
        ["GIT_ASKPASS"] = "echo",
        ["SSH_ASKPASS"] = "echo",
    };

    /// <summary>Prepended to every git invocation, for the same reason as <see cref="NonInteractive"/>.</summary>
    private static readonly string[] NonInteractiveArgs = { "-c", "credential.interactive=false" };

    private readonly Func<string, string[], string?, CommandResult> _run =
        runner ?? ((file, args, cwd) =>
            CommandRunner.Capture(file, args, cwd, NonInteractive, timeoutMs: CloneTimeoutMs));

    /// <summary>
    /// Accepts <c>owner/repo</c>, <c>host/owner/repo</c>, an https URL, or an scp-style SSH URL.
    /// Anything else is rejected rather than guessed - this is a security boundary.
    /// </summary>
    public static string NormalizeRepoUrl(string input)
    {
        var value = (input ?? "").Trim();
        if (value.Length == 0) throw new ArgumentException("no repository given", nameof(input));

        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return value;

        if (SshPattern(value)) return value;

        if (value.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException($"unsupported repository URL '{input}'", nameof(input));

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length switch
        {
            2 => $"https://github.com/{segments[0]}/{segments[1]}",
            3 when segments[0].Contains('.') => $"https://{segments[0]}/{segments[1]}/{segments[2]}",
            _ => throw new ArgumentException(
                $"'{input}' is not a repository - expected owner/repo or an https URL", nameof(input)),
        };
    }

    public static string HostOf(string repoUrl)
    {
        if (SshPattern(repoUrl))
        {
            var afterUser = repoUrl[(repoUrl.IndexOf('@') + 1)..];
            var colon = afterUser.IndexOf(':');
            return colon < 0 ? afterUser : afterUser[..colon];
        }
        return new Uri(repoUrl).Host;
    }

    /// <summary>Injects a token into an https URL: https://&lt;token&gt;@host/...</summary>
    internal static string AuthUrl(string url, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return url;

        const string https = "https://";
        if (!url.StartsWith(https, StringComparison.OrdinalIgnoreCase)) return url;

        // An existing credential in the URL wins - do not stack two.
        var authority = url[https.Length..];
        if (authority.Contains('@')) return url;

        return https + Uri.EscapeDataString(token) + "@" + authority;
    }

    internal static string Scrub(string text, string? token)
    {
        if (string.IsNullOrEmpty(token)) return text;
        return text
            .Replace(token, "***", StringComparison.Ordinal)
            .Replace(Uri.EscapeDataString(token), "***", StringComparison.Ordinal);
    }

    public GitOutcome CheckoutOrUpdate(string repoUrl, string @ref, int? pullRequest, string targetDir, bool fresh)
    {
        var token = credentials.Resolve(SafeHost(repoUrl));
        var url = AuthUrl(repoUrl, token);

        if (fresh) DeleteTree(targetDir);

        var outcome = Directory.Exists(Path.Combine(targetDir, ".git"))
            ? Update(url, @ref, pullRequest, targetDir)
            : Clone(url, @ref, pullRequest, targetDir);

        return outcome.Ok
            ? outcome with { Commit = HeadCommit(targetDir) }
            : outcome with { Error = Scrub(outcome.Error ?? "git failed", token) };
    }

    public (IReadOnlyList<string>? Branches, string? Error) ListBranches(string repoUrl)
    {
        var token = credentials.Resolve(SafeHost(repoUrl));

        foreach (var candidate in WithGitSuffix(repoUrl))
        {
            var result = Git(null, "ls-remote", "--heads", AuthUrl(candidate, token));
            if (result.ExitCode != 0) continue;

            const string prefix = "refs/heads/";
            var branches = result.Output.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Contains(prefix, StringComparison.Ordinal))
                .Select(line => line[(line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length)..].Trim())
                .Where(branch => branch.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(branch => branch, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (branches, null);
        }

        return (null, "could not list branches - private repository, or bad URL");
    }

    public string? HeadCommit(string dir)
    {
        var result = Git(dir, "rev-parse", "HEAD");
        return result.ExitCode == 0 && result.Output.Trim().Length > 0 ? result.Output.Trim() : null;
    }

    // ---- internals ----------------------------------------------------------

    private CommandResult Git(string? cwd, params string[] args) =>
        _run("git", NonInteractiveArgs.Concat(args).ToArray(), cwd);

    private GitOutcome Clone(string url, string @ref, int? pullRequest, string dir)
    {
        string? lastError = null;

        foreach (var candidate in WithGitSuffix(url))
        {
            DeleteTree(dir);

            if (pullRequest is { } number)
            {
                var cloned = Git(null, "clone", "--depth", "1", candidate, dir);
                if (cloned.ExitCode != 0) { lastError = cloned.Output; continue; }

                var spec = $"pull/{number}/head";
                var fetched = Git(dir, "fetch", "--depth", "1", "origin", spec);
                if (fetched.ExitCode != 0) return new(false, Trim(fetched.Output), null);

                var checkedOut = Git(dir, "checkout", "-q", "FETCH_HEAD");
                return checkedOut.ExitCode == 0
                    ? new(true, null, null)
                    : new(false, Trim(checkedOut.Output), null);
            }

            var result = Git(null, "clone", "--depth", "1", "--branch", @ref, candidate, dir);
            if (result.ExitCode == 0) return new(true, null, null);
            lastError = result.Output;
        }

        return new(false, $"git clone failed: {Trim(lastError ?? "")}", null);
    }

    /// <summary>
    /// Refreshes an existing workspace. Anything unexpected falls through to a fresh clone -
    /// a broken workspace should heal itself rather than block the user.
    /// </summary>
    private GitOutcome Update(string url, string @ref, int? pullRequest, string dir)
    {
        var spec = pullRequest is { } number ? $"pull/{number}/head" : @ref;

        var steps = new[]
        {
            new[] { "remote", "set-url", "origin", url },
            new[] { "fetch", "--depth", "1", "origin", spec },
            new[] { "reset", "--hard", "FETCH_HEAD" },
            CleanArgs(),
        };

        foreach (var args in steps)
        {
            var result = Git(dir, args);
            if (result.ExitCode != 0) return Clone(url, @ref, pullRequest, dir);
        }

        return new(true, null, null);
    }

    private static string[] CleanArgs()
    {
        var args = new List<string> { "clean", "-fdx" };
        foreach (var path in PreservedPaths) { args.Add("-e"); args.Add(path); }
        return args.ToArray();
    }

    private static IEnumerable<string> WithGitSuffix(string url) =>
        url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? new[] { url }
            : new[] { url, url + ".git" };

    private static bool SshPattern(string url)
    {
        var at = url.IndexOf('@');
        return at > 0 && url.IndexOf(':', at) > at && !url.Contains("://", StringComparison.Ordinal);
    }

    /// <summary>Host extraction that tolerates the file:// URLs used by tests.</summary>
    private static string SafeHost(string repoUrl)
    {
        try { return HostOf(repoUrl); } catch { return ""; }
    }

    private static string Trim(string text) => text.Length > 600 ? text[^600..] : text;

    private static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir)) return;

        // git marks pack files read-only, which blocks Directory.Delete on Windows.
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }

        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
