namespace QuickRun.Core.Tests;

/// <summary>A disposable directory used as the workspace root, so tests never touch the real one.</summary>
public sealed class TempHome : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "quickrun-tests-" + Guid.NewGuid().ToString("n")[..8]);

    public TempHome() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
