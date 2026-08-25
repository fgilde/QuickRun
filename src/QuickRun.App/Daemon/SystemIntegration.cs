using System.Runtime.Versioning;
using Microsoft.Win32;
using QuickRun.Core;

namespace QuickRun.App.Daemon;

public sealed record IntegrationStep(string What, bool Ok, string Detail);

/// <param name="Command">What the system would run for a <c>quickrun://</c> URL, as registered.</param>
/// <param name="Stale">
/// Registered, but to a different executable than the one running. That is the failure nobody sees:
/// the scheme looks installed and opens a binary that has been moved, renamed or deleted.
/// </param>
public sealed record SchemeStatus(bool Registered, string? Command, bool Stale, string Detail);

/// <param name="Enabled">Whether QuickRun starts with the machine.</param>
/// <param name="Detail">Where that is written, so it can be undone by hand as well.</param>
/// <param name="Stale">Enabled, but pointing at a different executable than the one running.</param>
public sealed record AutostartStatus(bool Enabled, string Detail, bool Stale = false);

/// <param name="Available">Whether typing <c>quickrun</c> in a terminal finds this executable.</param>
/// <param name="Detail">What was found, or what is missing.</param>
/// <param name="Directory">The directory that has to be reachable - the one on PATH, or linked into.</param>
/// <param name="NeedsShellRestart">
/// True right after a change: an open terminal keeps the environment it started with.
/// </param>
public sealed record PathStatus(bool Available, string Detail, string Directory, bool NeedsShellRestart = false);

/// <summary>
/// Registers the <c>quickrun://</c> scheme and the autostart entry.
/// <para>
/// The scheme has exactly one job: starting a daemon that is installed but not running. A browser
/// cannot be asked whether a handler exists, so this is what makes the extension's fallback work.
/// </para>
/// </summary>
public static class SystemIntegration
{
    private const string Scheme = "quickrun";

    public static IReadOnlyList<IntegrationStep> Install(string executable, int port)
    {
        if (OperatingSystem.IsWindows()) return InstallWindows(executable, port);
        if (OperatingSystem.IsMacOS()) return InstallMacOs(executable, port);
        return InstallLinux(executable, port);
    }

    public static IReadOnlyList<IntegrationStep> Uninstall()
    {
        if (OperatingSystem.IsWindows()) return UninstallWindows();
        if (OperatingSystem.IsMacOS()) return UninstallMacOs();
        return UninstallLinux();
    }

    // ---- Windows ------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<IntegrationStep> InstallWindows(string executable, int port)
    {
        var steps = new List<IntegrationStep>();

        steps.Add(SchemeStepWindows(executable));

        steps.Add(AutostartWindows(executable, port));

        return steps;
    }

    [SupportedOSPlatform("windows")]
    private static IntegrationStep AutostartWindows(string executable, int port) =>
        Try("add autostart entry", () =>
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            run.SetValue("QuickRun", $"\"{executable}\" daemon --port {port}");
            return $@"HKCU\{RunKey}\QuickRun";
        });

    /// <summary>
    /// The user's own PATH, unexpanded. Reading it through the environment would return the merged
    /// machine-and-user value, and writing that back would copy every machine entry into the user's.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? UserPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    [SupportedOSPlatform("windows")]
    private static void WriteUserPath(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment");

        // REG_EXPAND_SZ: a PATH entry may contain %USERPROFILE%, and rewriting it as a plain string
        // would leave those literal.
        key.SetValue("Path", value, RegistryValueKind.ExpandString);

        // Without this, only processes started after the next sign-in see the change.
        SendMessageTimeout(new nint(0xFFFF), WM_SETTINGCHANGE, nint.Zero, "Environment",
            SMTO_ABORTIFHUNG, 2000, out _);
    }

    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wparam, string lparam,
        uint flags, uint timeout, out nint result);

    [SupportedOSPlatform("windows")]
    private static IntegrationStep SchemeStepWindows(string executable) =>
        Try("register quickrun://", () =>
        {
            // HKCU, not HKLM: no administrator rights, and the registration belongs to this user.
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            key.SetValue("", "URL:QuickRun");
            key.SetValue("URL Protocol", "");

            using var command = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{Scheme}\shell\open\command");
            command.SetValue("", $"\"{executable}\" handle \"%1\"");

            return $@"HKCU\Software\Classes\{Scheme}";
        });

    [SupportedOSPlatform("windows")]
    private static SchemeStatus StatusWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Scheme}\shell\open\command");
        var command = key?.GetValue("") as string;

        if (string.IsNullOrWhiteSpace(command))
            return new SchemeStatus(false, null, false, $@"nothing registered under HKCU\Software\Classes\{Scheme}");

        var registered = ExecutableFrom(command!);
        var stale = !SameExecutable(registered, Environment.ProcessPath);

        return new SchemeStatus(true, command, stale, stale
            ? $"registered to {registered}, which is not the QuickRun running now"
            : $@"HKCU\Software\Classes\{Scheme}");
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<IntegrationStep> UninstallWindows()
    {
        var steps = new List<IntegrationStep>();

        steps.Add(Try("unregister quickrun://", () =>
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Scheme}", throwOnMissingSubKey: false);
            return "removed";
        }));

        steps.Add(Try("remove autostart entry", () =>
        {
            using var run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            run?.DeleteValue("QuickRun", throwOnMissingValue: false);
            return "removed";
        }));

        return steps;
    }

    // ---- Linux --------------------------------------------------------------

    private static IReadOnlyList<IntegrationStep> InstallLinux(string executable, int port)
    {
        var steps = new List<IntegrationStep>();
        var applications = Path.Combine(Home(), ".local", "share", "applications");
        var desktopFile = Path.Combine(applications, "quickrun.desktop");

        steps.Add(SchemeStepLinux(executable));

        steps.Add(AutostartLinux(executable, port));

        return steps;
    }

    private static IntegrationStep AutostartLinux(string executable, int port) =>
        Try("add autostart entry", () =>
        {
            var autostart = Path.Combine(Home(), ".config", "autostart");
            Directory.CreateDirectory(autostart);
            var path = Path.Combine(autostart, "quickrun-daemon.desktop");

            File.WriteAllText(path, $"""
                [Desktop Entry]
                Type=Application
                Name=QuickRun daemon
                Exec={executable} daemon --port {port}
                Icon=quickrun
                Terminal=false
                X-GNOME-Autostart-enabled=true

                """);
            return path;
        });

    private static IntegrationStep SchemeStepLinux(string executable)
    {
        var applications = Path.Combine(Home(), ".local", "share", "applications");
        var desktopFile = Path.Combine(applications, "quickrun.desktop");

        return Try("register quickrun://", () =>
        {
            Directory.CreateDirectory(applications);
            WriteIconLinux();
            File.WriteAllText(desktopFile, $"""
                [Desktop Entry]
                Type=Application
                Name=QuickRun
                Comment=Run any git repository with one click
                Exec={executable} handle %u
                Icon=quickrun
                Terminal=false
                NoDisplay=true
                MimeType=x-scheme-handler/{Scheme};

                """);

            // And one entry that is meant to be seen: a program you can start from the menu, with
            // an icon, rather than a handler hidden behind a URL scheme.
            File.WriteAllText(Path.Combine(applications, "quickrun-ui.desktop"), $"""
                [Desktop Entry]
                Type=Application
                Name=QuickRun
                GenericName=Repository runner
                Comment=Run any git repository with one click
                Exec={executable} ui
                Icon=quickrun
                Terminal=false
                Categories=Development;Utility;
                Keywords=git;repository;run;
                StartupWMClass=QuickRun

                """);

            // Without this the .desktop file exists but nothing routes the scheme to it.
            RunQuiet("xdg-mime", "default", "quickrun.desktop", $"x-scheme-handler/{Scheme}");
            RunQuiet("update-desktop-database", applications);
            return desktopFile;
        });
    }

    /// <summary>
    /// The icon a desktop environment looks for. A <c>.desktop</c> file naming <c>Icon=quickrun</c>
    /// finds nothing unless the file is in the icon theme, and the launcher then shows a blank tile.
    /// </summary>
    private static void WriteIconLinux()
    {
        try
        {
            var directory = Path.Combine(Home(), ".local", "share", "icons", "hicolor", "256x256", "apps");
            Directory.CreateDirectory(directory);

            using var source = typeof(SystemIntegration).Assembly
                .GetManifestResourceStream("QuickRun.App.Daemon.icon.png");
            if (source is null) return;

            using var file = File.Create(Path.Combine(directory, "quickrun.png"));
            source.CopyTo(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A missing icon is a cosmetic problem, not a reason to fail the registration.
        }
    }

    private static SchemeStatus StatusLinux()
    {
        var desktopFile = Path.Combine(Home(), ".local", "share", "applications", "quickrun.desktop");
        if (!File.Exists(desktopFile))
            return new SchemeStatus(false, null, false, $"no {desktopFile}");

        string? exec = null;
        try
        {
            exec = File.ReadAllLines(desktopFile)
                .FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal))?["Exec=".Length..];
        }
        catch (IOException)
        {
            // Unreadable is as good as absent for this purpose.
        }

        var registered = exec is null ? null : ExecutableFrom(exec);
        var stale = !SameExecutable(registered, Environment.ProcessPath);

        return new SchemeStatus(true, exec, stale, stale
            ? $"registered to {registered}, which is not the QuickRun running now"
            : desktopFile);
    }

    private static IReadOnlyList<IntegrationStep> UninstallLinux()
    {
        var files = new[]
        {
            Path.Combine(Home(), ".local", "share", "applications", "quickrun.desktop"),
            Path.Combine(Home(), ".local", "share", "applications", "quickrun-ui.desktop"),
            Path.Combine(Home(), ".config", "autostart", "quickrun-daemon.desktop"),
            Path.Combine(Home(), ".local", "share", "icons", "hicolor", "256x256", "apps", "quickrun.png"),
        };

        return files
            .Select(file => Try($"remove {Path.GetFileName(file)}", () =>
            {
                if (File.Exists(file)) File.Delete(file);
                return "removed";
            }))
            .ToList();
    }

    // ---- macOS --------------------------------------------------------------

    private static IReadOnlyList<IntegrationStep> InstallMacOs(string executable, int port)
    {
        var steps = new List<IntegrationStep>();

        // The scheme lives in the app bundle's Info.plist; a bare binary cannot claim it. If this
        // is the bundled binary, tell Launch Services about the bundle. Otherwise say so plainly
        // rather than pretending the registration happened.
        steps.Add(SchemeStepMacOs(executable));

        steps.Add(AutostartMacOs(executable, port));

        return steps;
    }

    private static IntegrationStep AutostartMacOs(string executable, int port) =>
        Try("add launch agent", () =>
        {
            var agents = Path.Combine(Home(), "Library", "LaunchAgents");
            Directory.CreateDirectory(agents);
            var path = Path.Combine(agents, "org.fgilde.quickrun.plist");

            File.WriteAllText(path, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                  <key>Label</key>
                  <string>org.fgilde.quickrun</string>
                  <key>ProgramArguments</key>
                  <array>
                    <string>{executable}</string>
                    <string>daemon</string>
                    <string>--port</string>
                    <string>{port}</string>
                  </array>
                  <key>RunAtLoad</key>
                  <true/>
                  <key>KeepAlive</key>
                  <false/>
                </dict>
                </plist>

                """);

            RunQuiet("launchctl", "load", "-w", path);
            return path;
        });

    private static IntegrationStep SchemeStepMacOs(string executable)
    {
        var bundle = FindEnclosingBundle(executable);

        return bundle is null
            ? new IntegrationStep("register quickrun://", false,
                "not running from QuickRun.app - download the .app.zip asset to register the scheme")
            : Try("register quickrun://", () =>
            {
                RunQuiet("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister",
                    "-f", bundle);
                return bundle;
            });
    }

    private static SchemeStatus StatusMacOs()
    {
        var bundle = FindEnclosingBundle(Environment.ProcessPath ?? "");

        // Launch Services owns the answer and has no queryable command line, so the bundle is the
        // honest one: inside it the scheme can be claimed, outside it cannot.
        return bundle is null
            ? new SchemeStatus(false, null, false,
                "not running from QuickRun.app - the .app bundle is what can claim the scheme")
            : new SchemeStatus(true, bundle, false, bundle);
    }

    private static IReadOnlyList<IntegrationStep> UninstallMacOs()
    {
        var plist = Path.Combine(Home(), "Library", "LaunchAgents", "org.fgilde.quickrun.plist");

        return new[]
        {
            Try("remove launch agent", () =>
            {
                if (File.Exists(plist))
                {
                    RunQuiet("launchctl", "unload", "-w", plist);
                    File.Delete(plist);
                }
                return "removed";
            }),
        };
    }

    /// <summary>
    /// Registers only the scheme, without touching autostart. This is what the button in the local
    /// UI does: a handler that opens the wrong binary is the common failure, and re-registering it
    /// should not also change whether QuickRun starts with the machine.
    /// </summary>
    public static IntegrationStep RegisterScheme(string executable)
    {
        if (OperatingSystem.IsWindows()) return SchemeStepWindows(executable);
        if (OperatingSystem.IsMacOS()) return SchemeStepMacOs(executable);
        return SchemeStepLinux(executable);
    }

    /// <summary>Whether QuickRun starts with the machine, and where that is written.</summary>
    public static AutostartStatus Autostart()
    {
        var executable = Environment.ProcessPath ?? "";

        if (OperatingSystem.IsWindows())
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            var value = run?.GetValue("QuickRun") as string;

            if (string.IsNullOrWhiteSpace(value))
                return new AutostartStatus(false, $@"nothing under HKCU\{RunKey}\QuickRun");

            var registered = ExecutableFrom(value!);
            return new AutostartStatus(true, $@"HKCU\{RunKey}\QuickRun",
                !SameExecutable(registered, executable));
        }

        var file = AutostartFile();
        if (!File.Exists(file)) return new AutostartStatus(false, $"no {file}");

        var text = Read(file);
        return new AutostartStatus(true, file,
            text is not null && executable.Length > 0 && !text.Contains(executable, StringComparison.Ordinal));
    }

    /// <summary>
    /// Turns starting with the machine on or off. The same entry <c>quickrun install</c> writes, so
    /// switching it here and there is the same switch.
    /// </summary>
    public static IntegrationStep SetAutostart(bool enabled, string executable, int port)
    {
        if (!enabled)
        {
            return Try("remove autostart entry", () =>
            {
                if (OperatingSystem.IsWindows())
                {
                    using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                    run?.DeleteValue("QuickRun", throwOnMissingValue: false);
                    return "removed";
                }

                var file = AutostartFile();
                if (File.Exists(file))
                {
                    if (OperatingSystem.IsMacOS()) RunQuiet("launchctl", "unload", "-w", file);
                    File.Delete(file);
                }

                return "removed";
            });
        }

        if (OperatingSystem.IsWindows()) return AutostartWindows(executable, port);
        if (OperatingSystem.IsMacOS()) return AutostartMacOs(executable, port);
        return AutostartLinux(executable, port);
    }

    /// <summary>
    /// Whether <c>quickrun</c> works in a terminal, and which directory has to be reachable for it
    /// to. On Windows that is the directory of the executable; elsewhere a link into a bin
    /// directory, because a downloaded binary can sit anywhere.
    /// </summary>
    public static PathStatus PathState()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable)) return new PathStatus(false, "unknown executable", "");

        if (OperatingSystem.IsWindows())
        {
            var directory = Path.GetDirectoryName(executable) ?? "";
            var user = UserPath();
            var listed = Entries(user).Concat(Entries(Environment.GetEnvironmentVariable("PATH")))
                .Any(entry => SamePath(entry, directory));

            return new PathStatus(listed,
                listed ? $"{directory} is on your PATH" : $"{directory} is not on your PATH",
                directory);
        }

        var link = Path.Combine(BinDirectory(), "quickrun");
        var target = File.Exists(link) ? (new FileInfo(link).LinkTarget ?? link) : null;

        if (target is null) return new PathStatus(false, $"no {link}", BinDirectory());

        return SameExecutable(target, executable)
            ? new PathStatus(true, $"{link} -> this executable", BinDirectory())
            : new PathStatus(false, $"{link} points at {target}", BinDirectory());
    }

    /// <summary>
    /// Makes <c>quickrun</c> work in a terminal, or stops doing so. Windows appends the directory to
    /// the user's PATH and tells the shell about it; elsewhere a symlink into a bin directory does
    /// the job without editing anyone's profile.
    /// </summary>
    public static IntegrationStep SetPath(bool wanted, string executable)
    {
        if (OperatingSystem.IsWindows()) return SetPathWindows(wanted, executable);

        var link = Path.Combine(BinDirectory(), "quickrun");

        return Try(wanted ? "link into a bin directory" : "remove the link", () =>
        {
            if (!wanted)
            {
                if (File.Exists(link)) File.Delete(link);
                return "removed";
            }

            Directory.CreateDirectory(BinDirectory());
            if (File.Exists(link)) File.Delete(link);
            File.CreateSymbolicLink(link, executable);
            return link;
        });
    }

    [SupportedOSPlatform("windows")]
    private static IntegrationStep SetPathWindows(bool wanted, string executable)
    {
        var directory = Path.GetDirectoryName(executable) ?? "";

        return Try(wanted ? "add to PATH" : "remove from PATH", () =>
        {
            var entries = Entries(UserPath()).ToList();
            var without = entries.Where(entry => !SamePath(entry, directory)).ToList();

            if (wanted)
            {
                if (without.Count == entries.Count) without.Add(directory);
                else return $"{directory} was already there";
            }
            else if (without.Count == entries.Count)
            {
                return "it was not there";
            }

            WriteUserPath(string.Join(';', without));
            return wanted ? $"added {directory}" : $"removed {directory}";
        });
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Where a link belongs. <c>~/.local/bin</c> is the user-level convention on Linux and is on the
    /// PATH of every current distribution; on macOS the Homebrew directories are the ones already
    /// there, so one of those is used when it can be written to.
    /// </summary>
    private static string BinDirectory()
    {
        if (!OperatingSystem.IsMacOS()) return Path.Combine(Home(), ".local", "bin");

        foreach (var candidate in new[] { "/opt/homebrew/bin", "/usr/local/bin" })
        {
            try
            {
                if (Directory.Exists(candidate) && Writable(candidate)) return candidate;
            }
            catch (IOException)
            {
                // Not writable, or not there at all. The fallback below always works.
            }
        }

        return Path.Combine(Home(), ".local", "bin");
    }

    private static bool Writable(string directory)
    {
        var probe = Path.Combine(directory, $".quickrun-{Environment.ProcessId}");

        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string AutostartFile() => OperatingSystem.IsMacOS()
        ? Path.Combine(Home(), "Library", "LaunchAgents", "org.fgilde.quickrun.plist")
        : Path.Combine(Home(), ".config", "autostart", "quickrun-daemon.desktop");

    private static string? Read(string file)
    {
        try { return File.ReadAllText(file); }
        catch (IOException) { return null; }
    }

    private static IEnumerable<string> Entries(string? path) =>
        (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left.Trim('"'))),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Whether <c>quickrun://</c> would reach this executable.</summary>
    public static SchemeStatus Status()
    {
        if (OperatingSystem.IsWindows()) return StatusWindows();
        if (OperatingSystem.IsMacOS()) return StatusMacOs();
        return StatusLinux();
    }

    /// <summary>
    /// The program out of a registered command line. Windows quotes it, a .desktop file does not and
    /// appends placeholders like <c>%u</c>.
    /// </summary>
    internal static string? ExecutableFrom(string command)
    {
        var text = command.Trim();
        if (text.Length == 0) return null;

        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }

    private static bool SameExecutable(string? registered, string? running)
    {
        if (registered is null || running is null) return false;

        try
        {
            return string.Equals(Path.GetFullPath(registered), Path.GetFullPath(running),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The <c>.app</c> directory this executable sits inside, if any.</summary>
    private static string? FindEnclosingBundle(string executable)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(executable)) ?? "");

        while (directory is not null)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }

    // ---- helpers ------------------------------------------------------------

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static IntegrationStep Try(string what, Func<string> action)
    {
        try
        {
            return new IntegrationStep(what, true, action());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new IntegrationStep(what, false, e.Message);
        }
    }

    /// <summary>Best effort: these helpers are absent on plenty of systems and that is not fatal.</summary>
    private static void RunQuiet(string file, params string[] args) =>
        Core.Process.CommandRunner.Capture(file, args, timeoutMs: 10_000);
}
