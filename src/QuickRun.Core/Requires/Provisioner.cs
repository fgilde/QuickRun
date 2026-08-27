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
/// completely when that folder is deleted.
/// </para>
/// <para>
/// Some of these need each other: pnpm is installed by npm, and pwsh by the .NET CLI. A missing
/// helper is provisioned first, into the same folder, so "install pnpm" works on a machine with no
/// Node at all.
/// </para>
/// </summary>
public static class Provisioner
{
    /// <summary>The only hosts this downloads from itself.</summary>
    private static readonly string[] TrustedHosts =
        ["dot.net", "dotnet.microsoft.com", "builds.dotnet.microsoft.com", "nodejs.org"];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>What each tool comes from, in the words the plan shows before anyone approves it.</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dotnet"] = "Microsoft's dotnet-install script",
            ["node"] = "nodejs.org",
            ["pnpm"] = "npm",
            ["yarn"] = "npm",
            ["pwsh"] = "the PowerShell package on NuGet, as a .NET tool",
        };

    /// <summary>
    /// What each of these needs to work at all. pnpm is a script that starts Node; PowerShell as a
    /// .NET tool is a launcher that needs a .NET runtime. Installing one without the other produces
    /// a tool that reports its version and then fails at the first real command.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Needs =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["pnpm"] = ["node"],
            ["yarn"] = ["node"],
            ["pwsh"] = ["dotnet"],
        };

    public static bool Handles(string tool) => Sources.ContainsKey(Name(tool));

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

        return new ProvisionPlan(tool, wanted, Directory(toolRoot, tool), Sources[tool]);
    }

    /// <summary>
    /// Makes sure the requirement is met, installing it if it is not, and returns the directory to
    /// put in front of the run's PATH. Null means it could not be provided - the caller reports
    /// that as the blocked requirement it is, rather than starting a run that cannot work.
    /// </summary>
    /// <param name="pathAhead">
    /// Directories of tools provisioned earlier in this run. A toolchain installed a moment ago is
    /// not on the machine's PATH, and pnpm cannot be installed by a Node that cannot be found.
    /// </param>
    public static async Task<IReadOnlyList<string>?> EnsureAsync(
        ToolRequirement requirement, string toolRoot, IReadOnlyList<string> pathAhead,
        Action<string> log, CancellationToken ct)
    {
        var tool = Name(requirement.Tool);
        if (!Handles(tool)) return null;

        var directory = Directory(toolRoot, tool);

        // Grows while this runs: installing pnpm may install Node first, and the check afterwards
        // has to be able to find that Node - pnpm's shim is a script that starts it.
        var context = pathAhead.ToList();

        // Installed by an earlier run and still good enough: nothing to download.
        foreach (var need in Needs.GetValueOrDefault(tool, []))
        {
            if (OnPath(need, context)) continue;

            var provisioned = BinDirectory(Directory(toolRoot, need), need);
            if (System.IO.Directory.Exists(provisioned)) context.Add(provisioned);
        }

        if (Installed(directory, tool, context) is { } already
            && VersionCheck.Satisfies(already, requirement.Version))
        {
            log($"{tool} {already} is already in {directory}");
            return Needed(directory, tool, context, pathAhead);
        }

        System.IO.Directory.CreateDirectory(directory);

        switch (tool)
        {
            case "dotnet":
                await InstallDotnetAsync(requirement, directory, log, ct);
                break;
            case "node":
                await InstallNodeAsync(requirement, directory, log, ct);
                break;
            case "pnpm":
            case "yarn":
                await InstallNpmToolAsync(tool, requirement, directory, toolRoot, context, log, ct);
                break;
            case "pwsh":
                await InstallPwshAsync(requirement, directory, toolRoot, context, log, ct);
                break;
        }

        var version = Installed(directory, tool, context);

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
        return Needed(directory, tool, context, pathAhead);
    }

    /// <summary>
    /// Everything the run has to carry for this tool: its own folder, and any helper installed here
    /// that the machine itself does not have. Handing back only the tool's folder was enough to make
    /// pnpm report its version and then fail with "node is not recognised".
    /// </summary>
    private static IReadOnlyList<string> Needed(
        string directory, string tool, IReadOnlyList<string> context, IReadOnlyList<string> pathAhead) =>
        context.Where(path => !pathAhead.Contains(path, StringComparer.OrdinalIgnoreCase))
            .Append(BinDirectory(directory, tool))
            .ToList();

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

    // ---- pnpm and yarn --------------------------------------------------------------------

    /// <summary>
    /// Installs a package manager with the package manager everyone already has. `npm install
    /// --global --prefix` is how npm puts an executable somewhere that is not the system: the shim
    /// lands in the folder itself on Windows and in its <c>bin</c> on everything else.
    /// </summary>
    private static async Task InstallNpmToolAsync(
        string tool, ToolRequirement requirement, string directory, string toolRoot,
        List<string> path, Action<string> log, CancellationToken ct)
    {
        await AlsoAsync("node", toolRoot, path, log, ct);

        if (!OnPath("npm", path))
        {
            log($"npm is needed to install {tool}, and is not there");
            return;
        }

        // A range becomes a major: npm understands pnpm@9, not pnpm@">=9".
        var wanted = VersionCheck.Extract(requirement.Version ?? "")?.Split('.')[0];
        var package = wanted is null ? tool : $"{tool}@{wanted}";

        log($"installing {package} into {directory} (this happens once)");

        var result = Run("npm", ["install", "--global", "--prefix", directory, package], path,
            timeoutMs: 10 * 60 * 1000);

        foreach (var line in Tail(result.Output)) log(line);
        if (result.ExitCode != 0) log($"npm exited with code {result.ExitCode}");
    }

    // ---- pwsh -----------------------------------------------------------------------------

    /// <summary>
    /// Installs PowerShell as a .NET tool, which is Microsoft's own per-user route: no installer,
    /// no administrator, no change to the machine's PATH. A build step calling <c>pwsh</c> then
    /// finds it, which is the whole point - a repository whose build shells out to PowerShell
    /// should not be a dead end on a machine that only has Windows PowerShell.
    /// </summary>
    private static async Task InstallPwshAsync(
        ToolRequirement requirement, string directory, string toolRoot,
        List<string> path, Action<string> log, CancellationToken ct)
    {
        await AlsoAsync("dotnet", toolRoot, path, log, ct);

        if (!OnPath("dotnet", path))
        {
            log("the .NET CLI is needed to install PowerShell, and is not there");
            return;
        }

        var version = VersionCheck.Extract(requirement.Version ?? "");
        var pinned = version is not null && version.Split('.').Length >= 3
            ? new[] { "--version", version }
            : [];

        log($"installing PowerShell into {directory} (this happens once)");

        var result = Run("dotnet",
            ["tool", "install", "--tool-path", directory, "PowerShell", .. pinned],
            path, timeoutMs: 10 * 60 * 1000);

        foreach (var line in Tail(result.Output)) log(line);
        if (result.ExitCode != 0) log($"the .NET CLI exited with code {result.ExitCode}");
    }

    // ---- shared ---------------------------------------------------------------------------

    /// <summary>
    /// Makes sure the tool this installation needs is among these paths, installing it if the
    /// machine does not have it. A helper the machine already has costs nothing here.
    /// </summary>
    private static async Task AlsoAsync(
        string dependency, string toolRoot, List<string> path, Action<string> log, CancellationToken ct)
    {
        if (OnPath(dependency, path)) return;

        log($"{dependency} is needed for this and is missing too");

        var directories = await EnsureAsync(
            new ToolRequirement(dependency, null, null, Optional: false), toolRoot, path, log, ct);

        if (directories is not null) path.AddRange(directories);
    }

    /// <summary>Whether a tool can be started with these directories in front of the PATH.</summary>
    private static bool OnPath(string tool, IReadOnlyList<string> pathAhead) =>
        Locate(tool, pathAhead) is not null
        && Run(tool, ToolChecker.ProbeArgs(tool), pathAhead, timeoutMs: 30_000).ExitCode == 0;

    /// <summary>
    /// Runs one of these tools with the given directories in front of the PATH.
    /// <para>
    /// Every argument is passed as an argument rather than pasted into a command line: the paths
    /// here carry spaces, and quoting them by hand for cmd.exe is how two attempts at this ended up
    /// handing the quotes themselves to the installer. Only a .cmd shim - which npm, pnpm and yarn
    /// are on Windows - goes through cmd.exe, because a process cannot be started from one.
    /// </para>
    /// </summary>
    private static CommandResult Run(
        string program, IReadOnlyList<string> args, IReadOnlyList<string> pathAhead, int timeoutMs)
    {
        var file = Locate(program, pathAhead) ?? program;
        var env = PathEnv(pathAhead);

        if (OperatingSystem.IsWindows()
            && (file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
            return CommandRunner.Capture("cmd.exe", new[] { "/c", file }.Concat(args),
                cwd: null, env: env, timeoutMs: timeoutMs);

        return CommandRunner.Capture(file, args, cwd: null, env: env, timeoutMs: timeoutMs);
    }

    /// <summary>The executable a tool name means, looked for in these directories and then the PATH.</summary>
    private static string? Locate(string tool, IReadOnlyList<string> pathAhead)
    {
        var names = OperatingSystem.IsWindows()
            ? new[] { $"{tool}.exe", $"{tool}.cmd", $"{tool}.bat" }
            : new[] { tool };

        var directories = pathAhead.Concat(
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));

        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            foreach (var name in names)
            {
                var candidate = Path.Combine(directory.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string>? PathEnv(IReadOnlyList<string> pathAhead) =>
        pathAhead.Count == 0
            ? null
            : EnvironmentFor(pathAhead, Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// What these tool directories mean for an environment: they go in front of the PATH, and a
    /// .NET installed here also becomes DOTNET_ROOT.
    /// <para>
    /// The second half is not decoration. A .NET tool - PowerShell is one - is a small launcher that
    /// looks for its runtime through DOTNET_ROOT and the machine's registered install, and never
    /// through the PATH. Without this, pwsh installed next to a .NET that only QuickRun knows about
    /// starts and immediately reports that it cannot find a runtime.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> EnvironmentFor(
        IReadOnlyList<string> paths, string? inheritedPath)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = string.Join(Path.PathSeparator, paths.Append(inheritedPath ?? "")),
        };

        var dotnet = paths.FirstOrDefault(directory => File.Exists(Path.Combine(directory,
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")));

        if (dotnet is not null) environment["DOTNET_ROOT"] = dotnet;

        return environment;
    }

    private static string Directory(string toolRoot, string tool) => Path.Combine(toolRoot, tool);

    /// <summary>Where the executables are, which is the directory that goes on the run's PATH.</summary>
    private static string BinDirectory(string directory, string tool) =>
        tool is "dotnet" or "pwsh" || OperatingSystem.IsWindows()
            ? directory
            : Path.Combine(directory, "bin");

    /// <summary>
    /// The version installed in QuickRun's own folder, or null if there is nothing there.
    /// <para>
    /// The file has to exist here first, and only then is it asked for its version through the
    /// shell: probing by name alone would find the machine's own copy and report a failed install
    /// as a success.
    /// </para>
    /// </summary>
    private static string? Installed(string directory, string tool, IReadOnlyList<string> pathAhead)
    {
        var bin = BinDirectory(directory, tool);
        var candidates = OperatingSystem.IsWindows()
            ? new[] { $"{tool}.exe", $"{tool}.cmd", $"{tool}.bat", tool }
            : new[] { tool };

        if (!candidates.Any(name => File.Exists(Path.Combine(bin, name)))) return null;

        // With everything provisioned so far behind it: pnpm's shim starts node, and a .NET tool
        // needs the .NET that was just installed. Probing with only its own folder said "not
        // installed" about tools that were installed perfectly well.
        var probe = Run(tool, ToolChecker.ProbeArgs(tool),
            new[] { bin }.Concat(pathAhead).ToList(), timeoutMs: 60_000);

        return probe.ExitCode == 0 ? VersionCheck.Extract(probe.Output) : null;
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

    /// <summary>
    /// Downloads one file, from one of the hosts above and no other. The URL is built here rather
    /// than taken from a config, and this is what keeps it that way.
    /// </summary>
    private static async Task DownloadAsync(string url, string to, CancellationToken ct)
    {
        var address = new Uri(url);

        if (address.Scheme != Uri.UriSchemeHttps
            || !TrustedHosts.Contains(address.Host, StringComparer.OrdinalIgnoreCase))
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
