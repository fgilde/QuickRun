using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace QuickRun.Core.Update;

/// <param name="Ok">Whether the binary on disk is now the new one.</param>
/// <param name="Version">What was installed, or null when there was nothing to install.</param>
/// <param name="Error">Why not, when it did not happen.</param>
public sealed record SelfUpdateOutcome(bool Ok, string? Version, string? Error)
{
    public static SelfUpdateOutcome Failed(string error) => new(false, null, error);
}

/// <summary>
/// Downloading a newer QuickRun and putting it in place of the running one.
/// <para>
/// Shared by the CLI and the window on purpose: updating from a button and updating from a command
/// have to be the same act, verified the same way. Everything a release publishes is checksummed in
/// its own SHA256SUMS, the download must come from the release's own URL, and a mismatch stops the
/// update rather than replacing the binary with whatever arrived.
/// </para>
/// </summary>
public static class SelfUpdate
{
    /// <summary>
    /// Fetches the newest release and replaces <paramref name="executable"/> with it.
    /// <para>
    /// The caller decides what happens afterwards - the CLI says to restart, the window restarts
    /// itself - because the running process is still the old one either way.
    /// </para>
    /// </summary>
    public static async Task<SelfUpdateOutcome> RunAsync(
        string executable, InstallSource source, Action<string>? log = null, UpdateChecker? checker = null)
    {
        log ??= _ => { };
        checker ??= new UpdateChecker();

        // A package manager owns this file. Two updaters writing the same binary is how a machine
        // ends up with a version neither of them believes in.
        if (!source.MayReplaceItself())
            return SelfUpdateOutcome.Failed(
                $"{source.ToString().ToLowerInvariant()} installed this - update with: {source.UpgradeCommand()}");

        var status = await checker.CheckAsync(BuildInfo.Version, source);
        if (status.Error is { } checkError) return SelfUpdateOutcome.Failed(checkError);
        if (!status.UpdateAvailable) return new SelfUpdateOutcome(false, null, null);

        var release = await checker.LatestAsync();
        if (release is null) return SelfUpdateOutcome.Failed("release details unavailable");

        var rid = RuntimeInformation.RuntimeIdentifier;
        var asset = release.AssetFor(rid);
        if (asset is null) return SelfUpdateOutcome.Failed($"release {release.Tag} has no asset for {rid}");

        log($"downloading {asset.Name}");
        var (archive, error) = await new Updater().FetchVerifiedAsync(release, asset.Name);
        if (error is { } fetchError) return SelfUpdateOutcome.Failed(fetchError);

        var binary = ExtractBinary(archive!, asset.Name);
        if (binary is null)
            return SelfUpdateOutcome.Failed($"{asset.Name} did not contain a quickrun binary");

        log($"installing {release.Version}");
        var swap = Updater.Swap(executable, binary, release.Version);

        return swap.Ok
            ? new SelfUpdateOutcome(true, release.Version, null)
            : SelfUpdateOutcome.Failed(swap.Error ?? "update failed");
    }

    /// <summary>Pulls the quickrun executable out of the release archive, in memory.</summary>
    public static byte[]? ExtractBinary(byte[] archive, string assetName)
    {
        using var source = new MemoryStream(archive);

        return assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? FromZip(source)
            : FromTarGz(source);
    }

    private static byte[]? FromZip(Stream source)
    {
        using var zip = new ZipArchive(source, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => IsBinary(e.Name));
        if (entry is null) return null;

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[]? FromTarGz(Stream source)
    {
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.DataStream is null || !IsBinary(Path.GetFileName(entry.Name))) continue;

            using var buffer = new MemoryStream();
            entry.DataStream.CopyTo(buffer);
            return buffer.ToArray();
        }

        return null;
    }

    private static bool IsBinary(string name) => name is "quickrun" or "quickrun.exe";
}
