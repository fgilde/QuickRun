using System.Collections.Concurrent;
using QuickRun.App.Commands;
using QuickRun.Core.Config;
using QuickRun.Core.Git;

namespace QuickRun.App.Daemon;

/// <param name="Quickrun">The repository commits a quickrun.yml.</param>
/// <param name="Pinokio">It carries scripts written for Pinokio.</param>
/// <param name="Known">
/// Whether the answer means anything. A repository on a host this cannot read is reported as
/// unknown rather than as empty, so nothing is hidden on a guess.
/// </param>
public sealed record RepoContents(bool Quickrun, bool Pinokio, bool Known);

/// <summary>
/// Asks GitHub what a repository carries, without cloning it.
/// <para>
/// This exists for one setting: whether the browser extension shows its button on every repository
/// or only where QuickRun has real instructions to follow. The extension cannot ask GitHub itself
/// without a host permission for raw.githubusercontent.com - a new question at every store review,
/// for something the daemon can answer in one request it is already allowed to make.
/// </para>
/// </summary>
public static class RepoProbe
{
    /// <summary>
    /// How long an answer stands. A repository that gains a quickrun.yml should show its button
    /// before long, and one that never had one should not be asked about on every page load.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, RepoContents Contents)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The files that make a repository one QuickRun knows how to start.</summary>
    private static readonly string[] Pinokio = { "pinokio.js", "pinokio.json" };

    public static async Task<RepoContents> LookAsync(string repo, string? reference, CancellationToken ct)
    {
        var key = $"{repo}@{reference ?? "HEAD"}";

        if (Cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < Lifetime)
            return cached.Contents;

        var contents = await AskAsync(repo, reference, ct);
        Cache[key] = (DateTimeOffset.UtcNow, contents);
        return contents;
    }

    private static async Task<RepoContents> AskAsync(string repo, string? reference, CancellationToken ct)
    {
        string url;
        try { url = RunPipeline.Normalize(repo); }
        catch (ArgumentException) { return new(false, false, Known: false); }

        // Only GitHub has a raw endpoint this knows the shape of. Anywhere else the honest answer
        // is "no idea", and an extension that hides buttons on no idea would hide the wrong ones.
        if (!GitClient.HostOf(url).Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return new(false, false, Known: false);

        var path = new Uri(url).AbsolutePath.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];

        // HEAD is not a ref raw.githubusercontent.com resolves, so an unspecified ref becomes the
        // repository's default branch by way of its two usual names.
        var refs = reference is { Length: > 0 } and not "HEAD"
            ? new[] { reference }
            : new[] { "HEAD", "main", "master" };

        foreach (var candidate in refs)
        {
            var quickrun = await AnyAsync(path, candidate, ConfigParser.FileNames, ct);
            var pinokio = await AnyAsync(path, candidate, Pinokio, ct);

            // A ref that answers for one file answers for all of them; a ref that does not exist
            // gives 404 for every name, and the next candidate is tried.
            if (quickrun || pinokio) return new(quickrun, pinokio, Known: true);
        }

        return new(false, false, Known: true);
    }

    private static async Task<bool> AnyAsync(string path, string reference, string[] names, CancellationToken ct)
    {
        foreach (var name in names)
        {
            var address = $"https://raw.githubusercontent.com/{path}/{reference}/{name}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, address);
                using var response = await Http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) return true;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Offline, or GitHub having a moment. Not knowing is not the same as knowing there
                // is nothing, and the caller treats it that way.
                return false;
            }
        }

        return false;
    }

    /// <summary>Forgets everything, for a test that would otherwise see a previous answer.</summary>
    internal static void Forget() => Cache.Clear();
}
