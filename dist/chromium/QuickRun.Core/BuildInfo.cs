using System.Reflection;

namespace QuickRun.Core;

/// <summary>
/// The version this build reports to the browser extension and compares against the latest
/// release. Read from the assembly rather than a constant, so the release workflow's
/// <c>-p:InformationalVersion=</c> is the single source of truth.
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = Read();

    /// <summary>The repository auto-update and the docs point at.</summary>
    public const string Repository = "fgilde/QuickRun";

    private static string Read()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational)) return "0.0.0";

        // The SDK appends "+<commit sha>" to InformationalVersion.
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
