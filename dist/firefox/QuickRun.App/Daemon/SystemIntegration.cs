using System.Runtime.Versioning;
using Microsoft.Win32;
using QuickRun.Core;

namespace QuickRun.App.Daemon;

public sealed record IntegrationStep(string What, bool Ok, string Detail);

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

        steps.Add(Try("register quickrun://", () =>
        {
            // HKCU, not HKLM: no administrator rights, and the registration belongs to this user.
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            key.SetValue("", "URL:QuickRun");
            key.SetValue("URL Protocol", "");

            using var command = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\{Scheme}\shell\open\command");
            command.SetValue("", $"\"{executable}\" handle \"%1\"");

            return $@"HKCU\Software\Classes\{Scheme}";
        }));

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

        steps.Add(Try("register quickrun://", () =>
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
        }));

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
        var bundle = FindEnclosingBundle(executable);
        steps.Add(bundle is null
            ? new IntegrationStep("register quickrun://", false,
                "not running from QuickRun.app - download the .app.zip asset to register the scheme")
            : Try("register quickrun://", () =>
            {
                RunQuiet("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister",
                    "-f", bundle);
                return bundle;
            }));

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
