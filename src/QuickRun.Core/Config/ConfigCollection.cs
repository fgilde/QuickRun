using System.Net.Http;

namespace QuickRun.Core.Config;

/// <summary>
/// Configs QuickRun keeps for repositories that have none of their own.
/// <para>
/// Plenty of good repositories will never commit a quickrun.yml, and QuickRun's reading of their
/// files is a guess - a decent one, but a guess. A config written by hand for a known repository is
/// better than a guess, so they are collected in QuickRun's own repository under
/// <c>configs/&lt;owner&gt;/&lt;repo&gt;.yml</c> and served from quickrun.org.
/// </para>
/// <para>
/// What this costs, said plainly: fetching one tells quickrun.org which repository is being started.
/// So it is asked only when the repository has nothing of its own, the answer is cached, and
/// <c>QUICKRUN_NO_COLLECTION</c> turns it off entirely - in which case QuickRun behaves exactly as
/// it did before this existed.
/// </para>
/// </summary>
public static class ConfigCollection
{
    /// <summary>Set to any value to never ask. The detector then has the last word, as before.</summary>
    public const string OptOut = "QUICKRUN_NO_COLLECTION";

    public const string BaseUrl = "https://quickrun.org/configs";

    /// <summary>
    /// How long a cached answer counts as current.
    /// <para>
    /// A day: these configs change when somebody improves one, which is not often, and a run must
    /// never wait on the network for something it already has.
    /// </para>
    /// </summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    /// <summary>Short, because a run is waiting on it and the detector is a fine answer.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// The config kept for this repository, or null when there is none, the network is unreachable,
    /// or asking is switched off.
    /// </summary>
    /// <param name="repo">The repository, in any of the forms QuickRun accepts.</param>
    /// <param name="cacheDir">Where answers are kept between runs.</param>
    /// <param name="fetch">The download, injectable so tests never touch the network.</param>
    public static string? For(string repo, string cacheDir, Func<string, string?>? fetch = null)
    {
        if (Environment.GetEnvironmentVariable(OptOut) is { Length: > 0 }) return null;
        if (RepoPath(repo) is not { } path) return null;

        var cached = CachePath(cacheDir, path);

        if (cached is not null && Fresh(cached)) return Read(cached);

        fetch ??= Download;
        var text = fetch($"{BaseUrl}/{path}.yml");

        if (text is null)
        {
            // Nothing new to be had. A stale answer beats no answer: it was written for this
            // repository, and the alternative is guessing from file names.
            return cached is not null && File.Exists(cached) ? Read(cached) : null;
        }

        if (cached is not null) Write(cached, text);
        return text;
    }

    /// <summary>
    /// <c>owner/repo</c> for the collection's file layout, or null for anything that is not a plain
    /// repository name - a URL for a host we do not key on, a path, anything odd.
    /// </summary>
    public static string? RepoPath(string? repo)
    {
        var value = (repo ?? "").Trim();
        if (value.Length == 0) return null;

        // The shapes QuickRun takes, reduced to owner/repo.
        foreach (var prefix in new[]
                 {
                     "https://github.com/", "http://github.com/", "git@github.com:",
                     "ssh://git@github.com/",
                 })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                value = value[prefix.Length..];

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];

        // A path is not a repository. Trimming slashes first turned "/etc/passwd" into a perfectly
        // well-formed "etc/passwd", which is the sort of thing that only looks harmless until the
        // day something downstream treats it as one.
        if (value.StartsWith('/') || value.StartsWith('\\') || value.StartsWith('~')) return null;
        if (value.Contains("://", StringComparison.Ordinal)) return null;
        if (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':') return null;

        value = value.Trim('/');

        var parts = value.Split('/');
        if (parts.Length != 2) return null;

        foreach (var part in parts)
            if (part.Length == 0 || !part.All(Allowed))
                return null;

        return $"{parts[0]}/{parts[1]}";
    }

    /// <summary>
    /// What may appear in an owner or repository name. Deliberately narrow: this string becomes part
    /// of a URL and of a file name, and a repository name is the caller's, not ours.
    /// </summary>
    private static bool Allowed(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.';

    private static string CachePath(string cacheDir, string path) =>
        Path.Combine(cacheDir, path.Replace('/', '_') + ".yml");

    private static bool Fresh(string file)
    {
        try
        {
            return File.Exists(file)
                   && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < CacheFor;
        }
        catch (IOException) { return false; }
    }

    private static string? Read(string file)
    {
        try
        {
            var text = File.ReadAllText(file);
            return text.Length == 0 ? null : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void Write(string file, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be written is slower, not broken.
        }
    }

    private static string? Download(string url)
    {
        try
        {
            // No proxy: this is one small file over the public internet, and WPAD resolution on a
            // Windows machine has already cost this project four red CI runs.
            using var handler = new SocketsHttpHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = Timeout };

            var response = client.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;

            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            // Offline, blocked, slow: not having a curated config is the normal case anyway.
            return null;
        }
    }
}
