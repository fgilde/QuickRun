namespace QuickRun.Core.Update;

public enum InstallSource
{
    /// <summary>Downloaded or built directly; QuickRun owns the binary and may replace it.</summary>
    Standalone,
    Winget,
    Scoop,
    Brew,
    Apt,
}

/// <summary>
/// Works out who owns the binary on disk. Two updaters fighting over the same file produces
/// version chaos and broken package-manager state, so QuickRun only replaces itself when nothing
/// else is managing it.
/// </summary>
public static class InstallSources
{
    public const string MarkerFileName = "install-source";

    /// <summary>
    /// Detection is primarily path-based: every package manager installs into a known location,
    /// and that needs no cooperation from the manifest or formula. A marker file overrides it, for
    /// packagers who want to be explicit.
    /// </summary>
    public static InstallSource Detect(string executablePath, string? markerContent = null)
    {
        if (Parse(markerContent) is { } declared) return declared;

        var path = (executablePath ?? "").Replace('\\', '/');

        if (Contains(path, "/scoop/apps/")) return InstallSource.Scoop;
        if (Contains(path, "/Microsoft/WinGet/Packages/")) return InstallSource.Winget;
        if (Contains(path, "/Cellar/") || Contains(path, "/linuxbrew/")
            || path.StartsWith("/opt/homebrew/", StringComparison.OrdinalIgnoreCase))
            return InstallSource.Brew;
        if (path.StartsWith("/usr/bin/", StringComparison.Ordinal)
            || path.StartsWith("/usr/lib/quickrun", StringComparison.Ordinal))
            return InstallSource.Apt;

        return InstallSource.Standalone;
    }

    public static InstallSource? Parse(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return Enum.TryParse<InstallSource>(value, ignoreCase: true, out var source) ? source : null;
    }

    /// <summary>Whether QuickRun may overwrite its own binary.</summary>
    public static bool MayReplaceItself(this InstallSource source) => source == InstallSource.Standalone;

    /// <summary>What to tell the user to run when QuickRun must not update itself.</summary>
    public static string UpgradeCommand(this InstallSource source) => source switch
    {
        InstallSource.Winget => "winget upgrade fgilde.QuickRun",
        InstallSource.Scoop => "scoop update quickrun",
        InstallSource.Brew => "brew upgrade quickrun",
        InstallSource.Apt => "apt upgrade quickrun",
        _ => "quickrun update",
    };

    private static bool Contains(string path, string fragment) =>
        path.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
