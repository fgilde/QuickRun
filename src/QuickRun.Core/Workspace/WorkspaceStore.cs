using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuickRun.Core.Workspace;

/// <param name="Path">
/// Where the code is. For a checkout that is the directory under runs/; for a folder run in place
/// it is the folder itself, which is somebody's working copy and not QuickRun's to delete.
/// </param>
/// <param name="Local">
/// Whether this is a folder on this machine that QuickRun runs where it lies. Nothing was checked
/// out and nothing was copied, so what is under runs/ is a note saying where it was - removing this
/// workspace removes the note.
/// </param>
public sealed record WorkspaceInfo(
    string Id, string Path, string Repo, string Ref, long Bytes,
    DateTimeOffset LastUsed, string? LastCommit, bool? LastOk, bool Local = false);

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
    /// <param name="variant">
    /// Distinguishes workspaces that share a repository and ref without being the same thing: a
    /// folder run where it lies and a copy of that folder are both keyed on its path, and one must
    /// not overwrite the other's note.
    /// </param>
    public static string IdFor(string repoUrl, string @ref, string? variant = null)
    {
        var (owner, repo) = OwnerAndRepo(repoUrl);
        var suffix = string.IsNullOrEmpty(variant) ? "" : $"__{Sanitize(variant)}";
        var name = $"{Sanitize(owner)}__{Sanitize(repo)}__{Sanitize(@ref)}{suffix}";
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];

        // The variant only joins the hash when there is one, so every workspace that existed before
        // it keeps its id - otherwise an update would silently orphan every checkout on the machine
        // and clone them all again.
        var seed = string.IsNullOrEmpty(variant) ? $"{repoUrl}\n{@ref}" : $"{repoUrl}\n{@ref}\n{variant}";

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant()[..6];
        return $"{name}-{hash}";
    }

    public string PathFor(string repoUrl, string @ref, string? variant = null) =>
        Path.Combine(RunsDir, IdFor(repoUrl, @ref, variant));

    /// <summary>
    /// Every workspace directory, including the ones with no metadata to describe them.
    /// <para>
    /// A directory whose metadata is missing used to be left out of this entirely, which made it
    /// invisible to everything: the list, the size total, and every Remove button - so a checkout
    /// that died before it was recorded, or one whose removal got halfway, sat there for ever with
    /// no way to get rid of it from inside QuickRun. It is listed now, saying what little is known
    /// about it, because a directory that cannot be seen cannot be deleted either.
    /// </para>
    /// </summary>
    /// <param name="withSizes">
    /// Whether to measure what each workspace occupies. Measuring means walking every file in it,
    /// and a checkout with a node_modules in it has tens of thousands - so a list of fifteen took
    /// minutes and the window showed nothing at all until it finished. A caller that only wants to
    /// name them asks for no sizes and gets an answer at once.
    /// </param>
    public IReadOnlyList<WorkspaceInfo> List(bool withSizes = true)
    {
        if (!Directory.Exists(RunsDir)) return Array.Empty<WorkspaceInfo>();

        var found = new List<WorkspaceInfo>();

        foreach (var dir in Directory.EnumerateDirectories(RunsDir))
        {
            if (Read(dir, withSizes) is { } described)
            {
                found.Add(described);
                continue;
            }

            // No metadata and nothing in it: the shell left behind when a removal deleted the
            // contents and then failed on the directory itself. There is nothing to lose here, so
            // it goes rather than being listed as a puzzle.
            if (IsEmpty(dir) && TryDeleteTree(dir)) continue;

            found.Add(Unknown(dir));
        }

        return found.OrderByDescending(info => info.LastUsed).ToList();
    }

    /// <summary>
    /// Whether a directory holds no file at all, however many directories are inside it.
    /// <para>
    /// Not "has no entries": a removal that dies partway leaves the whole directory tree standing
    /// with every file gone - forty-six empty directories and not one file, on the machine that
    /// reported this. That is a skeleton, and there is nothing in it to lose.
    /// </para>
    /// </summary>
    private static bool IsEmpty(string dir)
    {
        try { return !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(); }
        catch (Exception) { return false; }
    }

    /// <summary>What can be said about a directory that never recorded what it was.</summary>
    private static WorkspaceInfo Unknown(string dir)
    {
        var lastUsed = new DateTimeOffset(Directory.GetLastWriteTimeUtc(dir), TimeSpan.Zero);
        return new WorkspaceInfo(System.IO.Path.GetFileName(dir), dir,
            "unknown - no QuickRun metadata", "unknown", SizeOf(dir), lastUsed, null, null);
    }

    public WorkspaceInfo? Get(string id) => Read(Resolve(id));

    /// <param name="localPath">
    /// For a folder run in place: where it is. The directory under runs/ then holds only this note,
    /// so that the folder appears in the list - and can be taken off it - without QuickRun ever
    /// being in a position to delete somebody's working copy.
    /// </param>
    public void Touch(string id, string repoUrl, string @ref, string? commit, bool? ok,
        string? localPath = null)
    {
        var dir = Resolve(id);
        Directory.CreateDirectory(dir);
        var meta = new Meta(repoUrl, @ref, _now(), commit, ok, localPath);
        File.WriteAllText(Path.Combine(dir, MetaFileName), JsonSerializer.Serialize(meta, Json));
    }

    /// <summary>
    /// Deletes a workspace. False when it is not there; throws when it is there and will not go.
    /// <para>
    /// It used to return true either way, because the delete was wrapped in an empty catch. A
    /// Remove that failed therefore reported success, the list refreshed, and the button looked like
    /// it had done nothing at all - while the directory it deleted the contents of stayed behind.
    /// </para>
    /// </summary>
    public bool Remove(string id)
    {
        // Always the directory under runs/, never WorkspaceInfo.Path: for a folder run in place
        // those are different places, and the second one is somebody's working copy. Removing such
        // a workspace takes away QuickRun's note about it and touches nothing else.
        var dir = Resolve(id);
        if (!Directory.Exists(dir)) return false;

        if (!TryDeleteTree(dir))
            throw new IOException(
                $"'{id}' could not be removed - something still has a file in it open. "
                + "A run of it may still be going, or a virus scanner or an open Explorer window "
                + $"is holding it: {dir}");

        return true;
    }

    public int Clean(TimeSpan olderThan)
    {
        var cutoff = _now() - olderThan;
        return RemoveEach(List().Where(w => w.LastUsed < cutoff)).Removed;
    }

    public int RemoveAll() => RemoveEach(List()).Removed;

    /// <summary>
    /// Removes each of them, and says which would not go. One locked workspace must not stop the
    /// other fourteen - "Remove all" that gives up on the first failure looks like it does nothing.
    /// </summary>
    public (int Removed, IReadOnlyList<string> Failed) RemoveEach(IEnumerable<WorkspaceInfo> workspaces)
    {
        var removed = 0;
        var failed = new List<string>();

        foreach (var info in workspaces)
        {
            try
            {
                if (Remove(info.Id)) removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                failed.Add($"{info.Id}: {e.Message}");
            }
        }

        return (removed, failed);
    }

    // ---- internals ----------------------------------------------------------

    /// <param name="LocalPath">
    /// Set when the workspace is a folder QuickRun runs where it lies. Then the directory under
    /// runs/ holds this file and nothing else, and it is all that a removal can take away.
    /// </param>
    private sealed record Meta(string Repo, string Ref, DateTimeOffset LastUsed, string? LastCommit,
        bool? LastOk, string? LocalPath = null);

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

    private WorkspaceInfo? Read(string dir, bool withSizes = true)
    {
        var metaPath = Path.Combine(dir, MetaFileName);
        if (!File.Exists(metaPath)) return null;

        Meta? meta;
        try { meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaPath), Json); }
        catch (JsonException) { return null; }
        if (meta is null) return null;

        // A note pointing at a folder reports the folder, because that is where the code is - and
        // its size is not QuickRun's disk usage, so it is not counted.
        return meta.LocalPath is { } local
            ? new WorkspaceInfo(Path.GetFileName(dir), local, meta.Repo, meta.Ref,
                0, meta.LastUsed, meta.LastCommit, meta.LastOk, Local: true)
            : new WorkspaceInfo(Path.GetFileName(dir), dir, meta.Repo, meta.Ref,
                withSizes ? SizeOf(dir) : -1, meta.LastUsed, meta.LastCommit, meta.LastOk);
    }

    /// <summary>
    /// What a workspace occupies, by walking it.
    /// <para>
    /// Expensive by nature, which is why listing does not do it unless asked: the window loads the
    /// names first and fills the sizes in afterwards.
    /// </para>
    /// </summary>
    public static long SizeOf(string dir)
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
        // Backslash included, because a folder run in place is identified by its path - without it
        // the whole path ended up in the directory name, two underscores per separator.
        var segments = trimmed.Split(new[] { '/', ':', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2
            ? (segments[^2], segments[^1])
            : ("repo", segments.LastOrDefault() ?? "repo");
    }

    /// <summary>
    /// Characters removed from a workspace name. Deliberately a fixed set rather than
    /// <see cref="Path.GetInvalidFileNameChars"/>: on Unix that returns only '/' and NUL, so the
    /// same repository and ref would produce different ids on different platforms.
    /// </summary>
    private static readonly HashSet<char> InvalidNameChars =
    [
        '<', '>', ':', '"', '/', '\\', '|', '?', '*', ' ',
        .. Enumerable.Range(0, 32).Select(c => (char)c),
    ];

    private static string Sanitize(string value)
    {
        var invalid = InvalidNameChars;
        var builder = new StringBuilder(value.Length);

        foreach (var c in value) builder.Append(invalid.Contains(c) ? "__" : c.ToString());

        var result = builder.ToString();
        while (result.Contains("___", StringComparison.Ordinal))
            result = result.Replace("___", "__", StringComparison.Ordinal);

        return result.Trim('_', '.');
    }

    /// <summary>
    /// Deletes a directory tree, and says whether it is actually gone.
    /// <para>
    /// Retried, because on Windows the usual failure is temporary and looks permanent. A file whose
    /// handle is still open - a virus scanner reading it as it goes, an indexer, an editor - is
    /// marked for deletion rather than deleted, so the directory reads as empty and refuses to be
    /// removed with "the directory is not empty". Waiting a moment and asking again is what gets
    /// past it, and is why fourteen empty directories were left behind on the machine that reported
    /// this: the contents went, the directory did not, and the error was swallowed.
    /// </para>
    /// </summary>
    private static bool TryDeleteTree(string dir)
    {
        for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
        {
            // git marks pack files read-only, which blocks Directory.Delete on Windows.
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch (Exception) { }
            }
            catch (Exception)
            {
                // A tree that cannot even be walked is still worth trying to delete.
            }

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteAttempts) return !Directory.Exists(dir);
                Thread.Sleep(DeleteBackoff * attempt);
                continue;
            }

            if (!Directory.Exists(dir)) return true;
        }

        return !Directory.Exists(dir);
    }

    /// <summary>How many times a deletion is attempted before it counts as refused.</summary>
    private const int DeleteAttempts = 5;

    /// <summary>Grows with each attempt: 100ms, 200ms, ... which is long enough for a scanner.</summary>
    private static readonly TimeSpan DeleteBackoff = TimeSpan.FromMilliseconds(100);
}
