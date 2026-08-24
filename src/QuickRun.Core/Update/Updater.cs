using System.Security.Cryptography;

namespace QuickRun.Core.Update;

public sealed record UpdateResult(bool Ok, string? Error, string? InstalledVersion);

/// <summary>
/// Replaces the running binary with a newer release. This is a code-execution channel and is
/// treated as one: the asset must come from the release's own github.com download URL, and its
/// SHA-256 must match the checksum published with the release. A mismatch aborts.
/// </summary>
public sealed class Updater(
    Func<string, Task<byte[]>>? download = null,
    Func<string, Task<string>>? fetchText = null)
{
    public const string ChecksumFileName = "SHA256SUMS";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly Func<string, Task<byte[]>> _download = download ?? (url => Http.GetByteArrayAsync(url));
    private readonly Func<string, Task<string>> _fetchText = fetchText ?? (url => Http.GetStringAsync(url));

    /// <summary>
    /// Rejects anything that is not a github.com release download. Without this check the
    /// "latest release" JSON could point the updater at an arbitrary host.
    /// </summary>
    public static bool IsTrustedAssetUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;

        return uri.Host switch
        {
            "github.com" => uri.AbsolutePath.StartsWith(
                $"/{BuildInfo.Repository}/releases/", StringComparison.OrdinalIgnoreCase),
            // Where github.com redirects release downloads.
            "objects.githubusercontent.com" => true,
            _ => false,
        };
    }

    public static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>Pulls one asset's expected checksum out of a SHA256SUMS file.</summary>
    public static string? ExpectedSum(string checksums, string assetName)
    {
        foreach (var line in (checksums ?? "").Split('\n'))
        {
            var parts = line.Trim().Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && string.Equals(parts[^1].TrimStart('*'), assetName, StringComparison.OrdinalIgnoreCase))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>
    /// Downloads and verifies an asset. Returns its bytes, or an error - never unverified content.
    /// </summary>
    public async Task<(byte[]? Content, string? Error)> FetchVerifiedAsync(ReleaseInfo release, string assetName)
    {
        var asset = release.Asset(assetName);
        if (asset is null) return (null, $"release {release.Tag} has no asset '{assetName}'");
        if (!IsTrustedAssetUrl(asset.Url)) return (null, $"refusing to download from {asset.Url}");

        var checksumAsset = release.Asset(ChecksumFileName);
        if (checksumAsset is null) return (null, $"release {release.Tag} publishes no {ChecksumFileName}");
        if (!IsTrustedAssetUrl(checksumAsset.Url)) return (null, $"refusing to download from {checksumAsset.Url}");

        string checksums;
        byte[] content;
        try
        {
            checksums = await _fetchText(checksumAsset.Url);
            content = await _download(asset.Url);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (null, $"download failed: {e.Message}");
        }

        var expected = ExpectedSum(checksums, assetName);
        if (expected is null) return (null, $"{ChecksumFileName} has no entry for {assetName}");

        var actual = Sha256(content);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            return (null, $"checksum mismatch for {assetName}\n  expected {expected}\n  actual   {actual}");

        return (content, null);
    }

    /// <summary>
    /// Puts a verified binary in place of the current one.
    /// <para>
    /// On Windows a running executable cannot be overwritten, so the current file is renamed aside
    /// first and removed on the next start. Everywhere else the rename is atomic.
    /// </para>
    /// </summary>
    public static UpdateResult Swap(string currentPath, byte[] newBinary, string newVersion)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(currentPath));
        if (string.IsNullOrEmpty(directory)) return new(false, $"cannot resolve directory of {currentPath}", null);

        var staged = Path.Combine(directory, Path.GetFileName(currentPath) + ".new");
        var retired = Path.Combine(directory, Path.GetFileName(currentPath) + ".old");

        try
        {
            File.WriteAllBytes(staged, newBinary);

            if (File.Exists(retired)) TryDelete(retired);
            if (File.Exists(currentPath)) File.Move(currentPath, retired, overwrite: true);

            File.Move(staged, currentPath, overwrite: true);

            MakeExecutable(currentPath);

            return new(true, null, newVersion);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(staged);
            // Put the old binary back rather than leaving nothing behind.
            if (!File.Exists(currentPath) && File.Exists(retired))
                try { File.Move(retired, currentPath); } catch { }
            return new(false, $"could not replace {currentPath}: {e.Message}", null);
        }
    }

    /// <summary>Removes the file left behind by a previous Windows swap. Safe to call always.</summary>
    public static void CleanUpAfterSwap(string currentPath)
    {
        var retired = currentPath + ".old";
        if (File.Exists(retired)) TryDelete(retired);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* still locked; next start tries again */ }
    }

    /// <summary>A downloaded file has no execute bit; Windows has no such concept.</summary>
    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute); }
        catch { /* not fatal: the file is in place */ }
    }
}
