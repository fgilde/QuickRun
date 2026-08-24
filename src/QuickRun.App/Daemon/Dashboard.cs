using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace QuickRun.App.Daemon;

/// <summary>
/// Serves the local dashboard.
/// <para>
/// One embedded HTML page rather than Blazor Server: Blazor needs static web assets, and those are
/// published as loose files next to the executable, which would break the single-binary promise.
/// </para>
/// <para>
/// The page carries a per-session token that its own requests must send back. CORS stops another
/// origin reading responses but not sending requests, so without it any web page could POST to the
/// dashboard's endpoints.
/// </para>
/// </summary>
public sealed class Dashboard
{
    private const string ResourceName = "QuickRun.App.Daemon.dashboard.html";
    public const string TokenHeader = "X-QuickRun-Dashboard";

    private readonly string _template = LoadTemplate();

    public string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public string Render(int port) =>
        _template
            .Replace("{{TOKEN}}", Token, StringComparison.Ordinal)
            .Replace("{{PORT}}", port.ToString(), StringComparison.Ordinal)
            .Replace("{{VERSION}}", Core.BuildInfo.Version, StringComparison.Ordinal);

    public bool Authorized(string? token) =>
        !string.IsNullOrEmpty(token)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Token), Encoding.UTF8.GetBytes(token));

    private static string LoadTemplate()
    {
        using var stream = typeof(Dashboard).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded resource {ResourceName} is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
