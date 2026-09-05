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
    /// How long a cached answer is used without asking whether it is still the current one.
    /// <para>
    /// It was a day, and a day is how long a fixed config took to reach anybody: passbolt was
    /// corrected, deployed, and the next run still started the broken one out of this cache. These
    /// configs are served to strangers and fixed when somebody finds a fault, so a fix that arrives
    /// tomorrow is not a fix.
    /// </para>
    /// <para>
    /// Minutes, and past them the question is asked conditionally - "has this changed since the
    /// copy I have" - which costs an empty 304 and no download. What is kept is the part that
    /// mattered: a run never waits long on the network, and a machine that cannot reach it uses the
    /// copy it already has for as long as that lasts.
    /// </para>
    /// </summary>
    public static readonly TimeSpan TrustFor = TimeSpan.FromMinutes(5);

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

        if (cached is not null && Trusted(cached)) return Read(cached);

        // The copy's own date is what the question is asked with: the server answers 304 and sends
        // nothing when it still holds the same file. A null from that is indistinguishable from
        // being offline, and both mean the same thing here - keep what we have.
        var since = cached is not null ? Written(cached) : null;

        fetch ??= url => Download(url, since);
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

    /// <summary>Whether the copy is recent enough to use without asking about it at all.</summary>
    private static bool Trusted(string file) =>
        Written(file) is { } written && DateTimeOffset.UtcNow - written < TrustFor;

    /// <summary>When the cached copy was written, or null when there is none to ask about.</summary>
    private static DateTimeOffset? Written(string file)
    {
        try
        {
            return File.Exists(file) ? new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) : null;
        }
        catch (IOException) { return null; }
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

    /// <param name="since">
    /// When the copy on disk was written, so the server can answer "unchanged" instead of sending
    /// the file again. Null when there is no copy, which asks for it outright.
    /// </param>
    private static string? Download(string url, DateTimeOffset? since)
    {
        try
        {
            // No proxy: this is one small file over the public internet, and WPAD resolution on a
            // Windows machine has already cost this project four red CI runs.
            using var handler = new SocketsHttpHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = Timeout };

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (since is { } written) request.Headers.IfModifiedSince = written;

            var response = client.SendAsync(request).GetAwaiter().GetResult();

            // 304 among them: nothing came back because nothing changed, and the caller keeps the
            // copy it has.
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
