using System.Security.Cryptography;
using System.Text;

namespace QuickRun.Core.Config;

/// <summary>
/// Your own config for someone else's repository.
/// <para>
/// A repository you do not own will never carry a <c>quickrun.yml</c> that suits you, and detection
/// is a guess. This is where the config you wrote for it lives - keyed by repository, so it applies
/// to every branch of it.
/// </para>
/// <para>
/// Deliberately not inside the checkout: <c>--fresh</c> deletes that, <c>git status</c> would show
/// an untracked file in a repository that is not yours, and one careless commit would push it to a
/// stranger's project. The name is kept recognisable anyway, for whoever finds the file.
/// </para>
/// </summary>
public sealed class ConfigOverrides(string root)
{
    public const string FileName = "__auto_quickrun.config.yml";

    private const int MaxNameLength = 60;

    private string Dir => Path.Combine(root, "configs");

    /// <summary>Where this repository's override would live, whether or not it exists.</summary>
    public string PathFor(string repoUrl) => Path.Combine(Dir, IdFor(repoUrl), FileName);

    public bool Has(string repoUrl) => File.Exists(PathFor(repoUrl));

    /// <summary>The override's text, or null when there is none or it cannot be read.</summary>
    public string? Read(string repoUrl)
    {
        var path = PathFor(repoUrl);

        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Writes the override. Returns where it went.</summary>
    public string Write(string repoUrl, string yaml)
    {
        var path = PathFor(repoUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, yaml);
        return path;
    }

    public bool Delete(string repoUrl)
    {
        var path = PathFor(repoUrl);
        if (!File.Exists(path)) return false;

        File.Delete(path);

        // Leave no empty folder behind: the listing would show a repository with no override.
        var directory = Path.GetDirectoryName(path)!;
        try
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch (IOException)
        {
            // A folder that cannot be removed is not worth failing the delete over.
        }

        return true;
    }

    /// <summary>Every repository that has one, as the URL it was saved for.</summary>
    public IReadOnlyList<(string Repo, string Path, DateTimeOffset Changed)> List()
    {
        if (!Directory.Exists(Dir)) return Array.Empty<(string, string, DateTimeOffset)>();

        var found = new List<(string, string, DateTimeOffset)>();

        foreach (var directory in Directory.EnumerateDirectories(Dir))
        {
            var file = Path.Combine(directory, FileName);
            if (!File.Exists(file)) continue;

            var repo = ReadRepo(Path.Combine(directory, "repo.txt")) ?? Path.GetFileName(directory);
            found.Add((repo, file, new FileInfo(file).LastWriteTimeUtc));
        }

        return found.OrderByDescending(f => f.Item3).ToList();
    }

    /// <summary>
    /// Remembers which URL a folder belongs to. The folder name is a sanitised hash, so without
    /// this the listing could only show something nobody recognises.
    /// </summary>
    public void Remember(string repoUrl)
    {
        var directory = Path.GetDirectoryName(PathFor(repoUrl))!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "repo.txt"), repoUrl);
    }

    private static string? ReadRepo(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// A readable folder name plus a short hash of the URL, so <c>github.com/a/app</c> and
    /// <c>gitlab.example/a/app</c> cannot land on the same override.
    /// </summary>
    internal static string IdFor(string repoUrl)
    {
        var trimmed = repoUrl.Trim().TrimEnd('/');
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^4];

        var readable = new string(trimmed
            .Split('/', '\\', ':')
            .Where(part => part.Length > 0)
            .TakeLast(2)
            .SelectMany(part => part.Append('_'))
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray()).Trim('_', '-');

        if (readable.Length > MaxNameLength) readable = readable[..MaxNameLength];

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed.ToLowerInvariant())))
            .ToLowerInvariant()[..6];

        return readable.Length == 0 ? hash : $"{readable}-{hash}";
    }
}
