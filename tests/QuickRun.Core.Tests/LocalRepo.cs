using QuickRun.Core.Process;

namespace QuickRun.Core.Tests;

/// <summary>A throwaway git repository on disk, so git tests never hit the network.</summary>
public sealed class LocalRepo : IDisposable
{
    public string Path { get; }

    public string Url => new Uri(Path).AbsoluteUri;

    public LocalRepo()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "quickrun-repo-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(Path);

        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "test@example.com");
        Git("config", "user.name", "Test");
        Git("config", "commit.gpgsign", "false");
        Write("README.md", "hello");
        Commit("initial");
    }

    public void Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    public void Branch(string name) => Git("checkout", "-q", "-b", name);

    public void Checkout(string name) => Git("checkout", "-q", name);

    public void Tag(string name) => Git("tag", name);

    public string Head() => CommandRunner.Capture("git", new[] { "rev-parse", "HEAD" }, Path).Output.Trim();

    private void Git(params string[] args)
    {
        var result = CommandRunner.Capture("git", args, Path);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {result.Output}");
    }

    public void Dispose() => DeleteTree(Path);

    /// <summary>git marks pack files read-only, which blocks Directory.Delete on Windows.</summary>
    internal static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }

        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
