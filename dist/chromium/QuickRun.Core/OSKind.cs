using System.Runtime.InteropServices;

namespace QuickRun.Core;

/// <summary>The three platforms QuickRun runs on, matching the platform keys used in quickrun.yml.</summary>
public enum OSKind
{
    Windows,
    Linux,
    MacOs,
}

public static class OSKinds
{
    public static OSKind Current =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSKind.Windows
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSKind.MacOs
        : OSKind.Linux;

    /// <summary>The name this platform goes by in a config's platform maps and <c>when</c> filters.</summary>
    public static string Key(this OSKind os) => os switch
    {
        OSKind.Windows => "windows",
        OSKind.MacOs => "macos",
        _ => "linux",
    };
}
