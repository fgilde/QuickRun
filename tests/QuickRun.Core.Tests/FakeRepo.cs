namespace QuickRun.Core.Tests;

/// <summary>A disposable directory tree of plain files, for detector and runner tests.</summary>
public sealed class FakeRepo : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "quickrun-fake-" + Guid.NewGuid().ToString("n")[..8]);

    public FakeRepo() => Directory.CreateDirectory(Path);

    public FakeRepo With(string relativePath, string content = "")
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
