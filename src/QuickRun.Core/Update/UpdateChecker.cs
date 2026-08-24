using System.Net.Http.Headers;
using System.Text.Json;
using QuickRun.Core.Requires;

namespace QuickRun.Core.Update;

public sealed record ReleaseAsset(string Name, string Url);

public sealed record ReleaseInfo(string Version, string Tag, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) =>
        Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The single archive for a runtime identifier, e.g. <c>win-x64</c>.</summary>
    public ReleaseAsset? AssetFor(string rid) =>
        Assets.FirstOrDefault(a =>
            a.Name.Contains($"-{rid}.", StringComparison.OrdinalIgnoreCase)
            && (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            && !a.Name.Contains(".app.", StringComparison.OrdinalIgnoreCase));
}

public sealed record UpdateStatus(string Current, string? Latest, bool UpdateAvailable, InstallSource Source, string? Error)
{
    /// <summary>What the user should do about it - either QuickRun acts, or it tells them the command.</summary>
    public string Advice => !UpdateAvailable
        ? "up to date"
        : Source.MayReplaceItself()
            ? $"update to {Latest} available"
            : $"update to {Latest} available - run: {Source.UpgradeCommand()}";
}

/// <summary>Asks GitHub what the newest release is. The fetcher is injectable so tests stay offline.</summary>
public sealed class UpdateChecker(Func<string, Task<string>>? fetch = null)
{
    public const string LatestReleaseUrl =
        "https://api.github.com/repos/" + BuildInfo.Repository + "/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private readonly Func<string, Task<string>> _fetch = fetch ?? (url => Http.GetStringAsync(url));

    public async Task<UpdateStatus> CheckAsync(string currentVersion, InstallSource source)
    {
        try
        {
            var release = await LatestAsync();
            if (release is null) return new(currentVersion, null, false, source, "no release found");

            var newer = VersionCheck.Satisfies(release.Version, ">" + currentVersion);
            return new(currentVersion, release.Version, newer, source, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(currentVersion, null, false, source, e.Message);
        }
    }

    public async Task<ReleaseInfo?> LatestAsync()
    {
        var json = await _fetch(LatestReleaseUrl);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out var tag)) return null;
        var tagName = tag.GetString() ?? "";

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetArray) && assetArray.ValueKind == JsonValueKind.Array)
            foreach (var asset in assetArray.EnumerateArray())
                if (asset.TryGetProperty("name", out var name)
                    && asset.TryGetProperty("browser_download_url", out var url))
                    assets.Add(new(name.GetString() ?? "", url.GetString() ?? ""));

        return new ReleaseInfo(tagName.TrimStart('v', 'V'), tagName, assets);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // The GitHub API rejects requests without one.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("QuickRun", BuildInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
