using System.ComponentModel;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using QuickRun.Core;
using QuickRun.Core.Update;
using Spectre.Console;
using Spectre.Console.Cli;

namespace QuickRun.App.Commands;

public sealed class UpdateCommand : AsyncCommand<UpdateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--check")]
        [Description("Report whether an update exists and do nothing else.")]
        public bool CheckOnly { get; init; }

        [CommandOption("-y|--yes")]
        [Description("Install without asking.")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath;
        if (executable is null)
        {
            Output.Error("cannot determine the running executable - update manually");
            return 1;
        }

        var source = InstallSources.Detect(executable);
        var checker = new UpdateChecker();
        var status = await checker.CheckAsync(BuildInfo.Version, source);

        if (status.Error is { } error)
        {
            Output.Error($"could not check for updates: {error}");
            return 1;
        }

        Output.Info($"installed {status.Current} ({source.ToString().ToLowerInvariant()})");

        if (!status.UpdateAvailable)
        {
            Output.Info("up to date");
            return 0;
        }

        // A package manager owns this binary: two updaters fighting over one file is how version
        // chaos starts, so report the command and stop.
        if (!source.MayReplaceItself())
        {
            Output.Warn(status.Advice);
            return 0;
        }

        Output.Info($"update available: {status.Latest}");
        if (settings.CheckOnly) return 0;

        if (!settings.Yes && !AnsiConsole.Confirm($"Install QuickRun {status.Latest}?", defaultValue: false))
        {
            Output.Info("cancelled");
            return 0;
        }

        return await InstallAsync(checker, executable, status.Latest!);
    }

    private static async Task<int> InstallAsync(UpdateChecker checker, string executable, string version)
    {
        var release = await checker.LatestAsync();
        if (release is null)
        {
            Output.Error("release details unavailable");
            return 1;
        }

        var rid = RuntimeInformation.RuntimeIdentifier;
        var asset = release.AssetFor(rid);
        if (asset is null)
        {
            Output.Error($"release {release.Tag} has no asset for {rid}");
            return 1;
        }

        Output.Info($"downloading {asset.Name}");
        var (archive, error) = await new Updater().FetchVerifiedAsync(release, asset.Name);
        if (error is { } fetchError)
        {
            Output.Error(fetchError);
            return 1;
        }

        var binary = ExtractBinary(archive!, asset.Name);
        if (binary is null)
        {
            Output.Error($"{asset.Name} did not contain a quickrun binary");
            return 1;
        }

        var result = Updater.Swap(executable, binary, version);
        if (!result.Ok)
        {
            Output.Error(result.Error ?? "update failed");
            return 1;
        }

        Output.Info($"updated to {version} - restart the daemon to use it");
        return 0;
    }

    /// <summary>Pulls the quickrun executable out of the release archive, in memory.</summary>
    private static byte[]? ExtractBinary(byte[] archive, string assetName)
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

    private static bool IsBinary(string name) =>
        name is "quickrun" or "quickrun.exe";
}
