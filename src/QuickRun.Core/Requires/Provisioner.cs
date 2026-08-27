using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using QuickRun.Core.Config;
using QuickRun.Core.Process;

namespace QuickRun.Core.Requires;

/// <param name="Tool">What would be installed.</param>
/// <param name="Version">The version or channel it would be installed at.</param>
/// <param name="Directory">Where it would go - inside QuickRun's own folder, never the system.</param>
/// <param name="Source">Who it comes from, so the plan can say it out loud.</param>
public sealed record ProvisionPlan(string Tool, string Version, string Directory, string Source);

/// <summary>
/// Installs the tools a config requires but the machine does not have.
/// <para>
/// The point of QuickRun is that someone can run a repository without reading its setup
/// documentation, and "install .NET 10 first" is exactly that documentation wearing a different
/// hat. So a missing requirement is installed rather than reported - as far as that can be done
/// honestly.
/// </para>
/// <para>
/// Honestly means: into QuickRun's own folder, never into the system; from the vendor's own
/// distribution and no other host; without administrator rights; and without touching a version the
/// machine already has. Nothing here is put on the machine's PATH - the directory is prepended to
/// the PATH of the run's own processes and nowhere else, so a provisioned toolchain disappears
/// completely when the workspace is deleted.
/// </para>
/// </summary>
public static class Provisioner
{
    /// <summary>The only hosts anything is downloaded from.</summary>
    private static readonly string[] TrustedHosts =
        ["dot.net", "dotnet.microsoft.com", "builds.dotnet.microsoft.com", "nodejs.org"];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static bool Handles(string tool) => Name(tool) is "dotnet" or "node";

    private static string Name(string tool) => (tool ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// What installing this requirement would mean, for a plan to show before anyone approves it.
    /// Null when the requirement is already met, optional, or not something QuickRun can install.
    /// </summary>
    public static ProvisionPlan? PlanFor(ToolCheckResult check, string toolRoot)
    {
        if (check.Satisfied || check.Requirement.Optional) return null;

        var tool = Name(check.Requirement.Tool);
        if (!Handles(tool)) return null;

        var wanted = string.IsNullOrWhiteSpace(check.Requirement.Version)
            ? tool == "dotnet" ? "LTS" : "latest"
            : check.Requirement.Version!;

        return new ProvisionPlan(tool, wanted, Directory(toolRoot, tool),
            tool == "dotnet" ? "Microsoft's dotnet-install script" : "nodejs.org");
    }

    /// <summary>
    /// Makes sure the requirement is met, installing it if it is not, and returns the directory to
    /// put in front of the run's PATH. Null means it could not be provided - the caller reports
    /// that as the blocked requirement it is, rather than starting a run that cannot work.
    /// </summary>
    public static async Task<string?> EnsureAsync(
        ToolRequirement requirement, string toolRoot, Action<string> log, CancellationToken ct)
    {
        var tool = Name(requirement.Tool);
        if (!Handles(tool)) return null;

        var directory = Directory(toolRoot, tool);

        // Installed by an earlier run and still good enough: nothing to download.
        if (Installed(directory, tool) is { } already
            && VersionCheck.Satisfies(already, requirement.Version))
        {
            log($"{tool} {already} is already in {directory}");
            return BinDirectory(directory, tool);
        }

        System.IO.Directory.CreateDirectory(directory);

        if (tool == "dotnet") await InstallDotnetAsync(requirement, directory, log, ct);
        else await InstallNodeAsync(requirement, directory, log, ct);

        var version = Installed(directory, tool);

        if (version is null)
        {
            log($"{tool} was not installed - the requirement stands");
            return null;
        }

        if (!VersionCheck.Satisfies(version, requirement.Version))
        {
            log($"installed {tool} {version}, which does not satisfy {requirement.Version}");
            return null;
        }

        log($"{tool} {version} is ready in {directory}");
        return BinDirectory(directory, tool);
    }

    // ---- dotnet ---------------------------------------------------------------------------

    /// <summary>
    /// Runs Microsoft's own dotnet-install script, which is the supported way to put a .NET SDK
    /// somewhere without administrator rights - the same script every CI image uses.
    /// </summary>
    private static async Task InstallDotnetAsync(
        ToolRequirement requirement, string directory, Action<string> log, CancellationToken ct)
    {
        var channel = Channel(requirement.Version);
        var windows = OperatingSystem.IsWindows();
        var url = windows ? "https://dot.net/v1/dotnet-install.ps1" : "https://dot.net/v1/dotnet-install.sh";

        var script = Path.Combine(directory, windows ? "dotnet-install.ps1" : "dotnet-install.sh");
        log($"installing .NET {channel} into {directory} (this happens once)");
        await DownloadAsync(url, script, ct);

        // Started directly rather than through a shell: the paths here carry spaces, and quoting
        // them for cmd.exe is how the first attempt at this ended up passing "" to -File.
        var (program, args) = windows
            ? ("powershell", new[]
              {
                  "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                  "-Channel", channel, "-InstallDir", directory, "-NoPath",
              })
            : ("sh", new[] { script, "--channel", channel, "--install-dir", directory, "--no-path" });

        var result = CommandRunner.Capture(program, args, timeoutMs: 15 * 60 * 1000);

        foreach (var line in Tail(result.Output)) log(line);
        if (result.ExitCode != 0) log($"the .NET installer exited with code {result.ExitCode}");
    }

    /// <summary>
    /// The channel to install. A requirement says what is acceptable (">=10", "10.0.100", "8");
    /// the installer wants a channel ("10.0", "LTS"), so the major and minor are what carries over.
    /// </summary>
    public static string Channel(string? version)
    {
        var wanted = VersionCheck.Extract(version ?? "");
        if (wanted is null) return "LTS";

        var parts = wanted.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : $"{parts[0]}.0";
    }

    // ---- node -----------------------------------------------------------------------------

    /// <summary>
    /// Downloads a Node build from nodejs.org and unpacks it. There is no vendor installer that
    /// works without administrator rights, but the distribution archives are the same bits, and
    /// unpacking one into a folder is what every version manager does underneath.
    /// </summary>
    private static async Task InstallNodeAsync(
        ToolRequirement requirement, string directory, Action<string> log, CancellationToken ct)
    {
        var version = await NodeVersionAsync(requirement.Version, ct);
        if (version is null)
        {
            log($"nodejs.org lists no version matching {requirement.Version}");
            return;
        }

        var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "darwin" : "linux";
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => "x86",
        };

        var stem = $"node-{version}-{platform}-{architecture}";
        var archive = OperatingSystem.IsWindows() ? $"{stem}.zip" : $"{stem}.tar.gz";
        var url = $"https://nodejs.org/dist/{version}/{archive}";

        log($"installing Node {version} into {directory} (this happens once)");

        var download = Path.Combine(directory, archive);
        await DownloadAsync(url, download, ct);

        // The archive carries its own top-level folder; the contents of that folder are what is
        // wanted here, so it is unpacked next door and then moved into place.
        var staging = Path.Combine(directory, ".unpack");
        if (System.IO.Directory.Exists(staging)) System.IO.Directory.Delete(staging, recursive: true);
        System.IO.Directory.CreateDirectory(staging);

        if (OperatingSystem.IsWindows()) ZipFile.ExtractToDirectory(download, staging, overwriteFiles: true);
        else await ExtractTarGzAsync(download, staging, ct);

        var unpacked = System.IO.Directory.EnumerateDirectories(staging).FirstOrDefault() ?? staging;
        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(unpacked))
            Move(entry, Path.Combine(directory, Path.GetFileName(entry)));

        System.IO.Directory.Delete(staging, recursive: true);
        File.Delete(download);
    }

    /// <summary>The newest version nodejs.org offers that the requirement accepts.</summary>
    private static async Task<string?> NodeVersionAsync(string? wanted, CancellationToken ct)
    {
        var listing = await Http.GetStringAsync("https://nodejs.org/dist/index.json", ct);
        using var document = JsonDocument.Parse(listing);

        // The index is newest first, so the first acceptable entry is the newest acceptable one.
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("version").GetString() is not { } version) continue;
            if (VersionCheck.Satisfies(version, wanted)) return version;
        }

        return null;
    }

    private static async Task ExtractTarGzAsync(string archive, string into, CancellationToken ct)
    {
        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzip, into, overwriteFiles: true, ct);
    }

    private static void Move(string from, string to)
    {
        if (System.IO.Directory.Exists(from))
        {
            if (System.IO.Directory.Exists(to)) System.IO.Directory.Delete(to, recursive: true);
            System.IO.Directory.Move(from, to);
            return;
        }

        File.Move(from, to, overwrite: true);
    }

    // ---- shared ---------------------------------------------------------------------------

    private static string Directory(string toolRoot, string tool) => Path.Combine(toolRoot, tool);

    /// <summary>Where the executables are, which is the directory that goes on the run's PATH.</summary>
    private static string BinDirectory(string directory, string tool) =>
        tool == "node" && !OperatingSystem.IsWindows() ? Path.Combine(directory, "bin") : directory;

    /// <summary>The version installed in QuickRun's own folder, or null if there is nothing there.</summary>
    private static string? Installed(string directory, string tool)
    {
        var executable = Path.Combine(BinDirectory(directory, tool),
            OperatingSystem.IsWindows() ? $"{tool}.exe" : tool);

        if (!File.Exists(executable)) return null;

        var probe = CommandRunner.Capture(executable, ToolChecker.ProbeArgs(tool), timeoutMs: 30_000);
        return probe.ExitCode == 0 ? VersionCheck.Extract(probe.Output) : null;
    }

    /// <summary>
    /// Downloads one file, from one of the hosts above and no other. The URL is built here rather
    /// than taken from a config, and this is what keeps it that way.
    /// </summary>
    private static async Task DownloadAsync(string url, string to, CancellationToken ct)
    {
        var address = new Uri(url);

        if (address.Scheme != Uri.UriSchemeHttps || !TrustedHosts.Contains(address.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{url} is not a tool distribution URL");

        await using var stream = await Http.GetStreamAsync(address, ct);
        await using var file = File.Create(to);
        await stream.CopyToAsync(file, ct);
    }

    /// <summary>The last few lines of an installer's chatter - enough to see what it did.</summary>
    private static IEnumerable<string> Tail(string output) =>
        (output ?? "").Split('\n')
        .Select(line => line.TrimEnd('\r'))
        .Where(line => line.Trim().Length > 0)
        .TakeLast(3);
}
