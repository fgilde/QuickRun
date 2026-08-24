using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuickRun.Core.Workspace;

public sealed record WorkspaceInfo(
    string Id, string Path, string Repo, string Ref, long Bytes,
    DateTimeOffset LastUsed, string? LastCommit, bool? LastOk);

/// <summary>
/// Owns the directories QuickRun checks repositories out into. Deliberately not under %TEMP%:
/// system cleaners delete from there, and a half-removed node_modules mid-run is a bug factory.
/// </summary>
public sealed class WorkspaceStore
{
    private const string MetaFileName = ".quickrun-meta.json";
    private const int MaxNameLength = 80;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Func<DateTimeOffset> _now;

    /// <param name="rootOverride">Explicit root, otherwise QUICKRUN_HOME, otherwise the OS app-data directory.</param>
    /// <param name="now">Injectable clock, so cleanup rules are testable.</param>
    public WorkspaceStore(string? rootOverride = null, Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Root = rootOverride
               ?? Environment.GetEnvironmentVariable("QUICKRUN_HOME")
               ?? Path.Combine(
                   Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                   "QuickRun");
    }

    /// <summary>
    /// LocalApplicationData already maps to %LOCALAPPDATA%, ~/.local/share and
    /// ~/Library/Application Support, so no per-platform branching is needed.
    /// </summary>
    public string Root { get; }

    private string RunsDir => Path.Combine(Root, "runs");

    /// <summary>
    /// A readable directory name plus a short hash, so that two refs sanitising to the same
    /// name still get separate workspaces.
    /// </summary>
    public static string IdFor(string repoUrl, string @ref)
    {
        var (owner, repo) = OwnerAndRepo(repoUrl);
        var name = $"{Sanitize(owner)}__{Sanitize(repo)}__{Sanitize(@ref)}";
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{repoUrl}\n{@ref}")))
            .ToLowerInvariant()[..6];
        return $"{name}-{hash}";
    }

    public string PathFor(string repoUrl, string @ref) => Path.Combine(RunsDir, IdFor(repoUrl, @ref));

    public IReadOnlyList<WorkspaceInfo> List()
    {
        if (!Directory.Exists(RunsDir)) return Array.Empty<WorkspaceInfo>();

        return Directory.EnumerateDirectories(RunsDir)
            .Select(Read)
            .Where(info => info is not null)
            .Select(info => info!)
            .OrderByDescending(info => info.LastUsed)
            .ToList();
    }

    public WorkspaceInfo? Get(string id) => Read(Resolve(id));

    public void Touch(string id, string repoUrl, string @ref, string? commit, bool? ok)
    {
        var dir = Resolve(id);
        Directory.CreateDirectory(dir);
        var meta = new Meta(repoUrl, @ref, _now(), commit, ok);
        File.WriteAllText(Path.Combine(dir, MetaFileName), JsonSerializer.Serialize(meta, Json));
    }

    public bool Remove(string id)
    {
        var dir = Resolve(id);
        if (!Directory.Exists(dir)) return false;
        DeleteTree(dir);
        return true;
    }

    public int Clean(TimeSpan olderThan)
    {
        var cutoff = _now() - olderThan;
        var removed = 0;
        foreach (var info in List().Where(w => w.LastUsed < cutoff))
            if (Remove(info.Id)) removed++;
        return removed;
    }

    public int RemoveAll()
    {
        var removed = 0;
        foreach (var info in List())
            if (Remove(info.Id)) removed++;
        return removed;
    }

    // ---- internals ----------------------------------------------------------

    private sealed record Meta(string Repo, string Ref, DateTimeOffset LastUsed, string? LastCommit, bool? LastOk);

    /// <summary>Maps an id to its directory, rejecting anything that could escape the root.</summary>
    private string Resolve(string id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || id.Contains('/') || id.Contains('\\')
            || id.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(id))
            throw new ArgumentException($"'{id}' is not a workspace id", nameof(id));

        return Path.Combine(RunsDir, id);
    }

    private WorkspaceInfo? Read(string dir)
    {
        var metaPath = Path.Combine(dir, MetaFileName);
        if (!File.Exists(metaPath)) return null;

        Meta? meta;
        try { meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaPath), Json); }
        catch (JsonException) { return null; }
        if (meta is null) return null;

        return new WorkspaceInfo(Path.GetFileName(dir), dir, meta.Repo, meta.Ref,
            SizeOf(dir), meta.LastUsed, meta.LastCommit, meta.LastOk);
    }

    // ponytail: full tree walk per List() call; cache the size in the metadata if listing feels slow
    private static long SizeOf(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch
        {
            return 0;
        }
    }

    private static (string Owner, string Repo) OwnerAndRepo(string repoUrl)
    {
        var trimmed = repoUrl.TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^4];

        // Explicit array: Split('/', ':', options) would bind ':' as the count parameter.
        var segments = trimmed.Split(new[] { '/', ':' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2
            ? (segments[^2], segments[^1])
            : ("repo", segments.LastOrDefault() ?? "repo");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ' ' }).ToHashSet();
        var builder = new StringBuilder(value.Length);

        foreach (var c in value) builder.Append(invalid.Contains(c) ? "__" : c.ToString());

        var result = builder.ToString();
        while (result.Contains("___", StringComparison.Ordinal))
            result = result.Replace("___", "__", StringComparison.Ordinal);

        return result.Trim('_', '.');
    }

    private static void DeleteTree(string dir)
    {
        // git marks pack files read-only, which blocks Directory.Delete on Windows.
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }

        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
