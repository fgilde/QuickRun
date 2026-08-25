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

        steps.Add(Try("add autostart entry", () =>
        {
            using var run = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            run.SetValue("QuickRun", $"\"{executable}\" daemon --port {port}");
            return @"HKCU\...\CurrentVersion\Run\QuickRun";
        }));

        return steps;
    }

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

        steps.Add(Try("add autostart entry", () =>
        {
            var autostart = Path.Combine(Home(), ".config", "autostart");
            Directory.CreateDirectory(autostart);
            var path = Path.Combine(autostart, "quickrun-daemon.desktop");

            File.WriteAllText(path, $"""
                [Desktop Entry]
                Type=Application
                Name=QuickRun daemon
                Exec={executable} daemon --port {port}
                Terminal=false
                X-GNOME-Autostart-enabled=true

                """);
            return path;
        }));

        return steps;
    }

    private static IntegrationStep SchemeStepLinux(string executable)
    {
        var applications = Path.Combine(Home(), ".local", "share", "applications");
        var desktopFile = Path.Combine(applications, "quickrun.desktop");

        return Try("register quickrun://", () =>
        {
            Directory.CreateDirectory(applications);
            File.WriteAllText(desktopFile, $"""
                [Desktop Entry]
                Type=Application
                Name=QuickRun
                Comment=Run any git repository with one click
                Exec={executable} handle %u
                Terminal=false
                NoDisplay=true
                MimeType=x-scheme-handler/{Scheme};

                """);

            // Without this the .desktop file exists but nothing routes the scheme to it.
            RunQuiet("xdg-mime", "default", "quickrun.desktop", $"x-scheme-handler/{Scheme}");
            RunQuiet("update-desktop-database", applications);
            return desktopFile;
        });
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
            Path.Combine(Home(), ".config", "autostart", "quickrun-daemon.desktop"),
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

        steps.Add(Try("add launch agent", () =>
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
        }));

        return steps;
    }

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
