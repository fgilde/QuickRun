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

    /// <summary>
    /// The release's own manifest, which is a file rather than an API call.
    /// <para>
    /// Asked first, because the API above is rate-limited for anyone not sending a token and a
    /// shared address reaches that limit without doing anything unusual. It answers 403, the check
    /// treats it as "could not ask", and what the user sees is "no update" - a release sitting there
    /// while every machine behind that address says it is current. This is a plain download from the
    /// same release, with no limit of that kind.
    /// </para>
    /// </summary>
    public const string ManifestUrl =
        "https://github.com/" + BuildInfo.Repository + "/releases/latest/download/quickrun.json";

    /// <summary>
    /// What each platform's archive is called. Fixed names, which is what makes the manifest enough:
    /// with the version known, every asset's address is known too, and nothing has to be listed by
    /// an API to be found.
    /// </summary>
    private static readonly string[] ArchiveNames =
    {
        "quickrun-win-x64.zip", "quickrun-win-arm64.zip",
        "quickrun-linux-x64.tar.gz", "quickrun-linux-arm64.tar.gz",
        "quickrun-osx-x64.tar.gz", "quickrun-osx-arm64.tar.gz",
        "SHA256SUMS",
    };

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

    public async Task<ReleaseInfo?> LatestAsync() =>
        await FromManifestAsync() ?? await FromApiAsync();

    /// <summary>
    /// The release read from its manifest, or null when there is none to read.
    /// <para>
    /// Null rather than an exception on anything unexpected: this is the first of two ways to ask,
    /// and a manifest that is missing or written by an older release must fall through to the API
    /// rather than end the check.
    /// </para>
    /// </summary>
    private async Task<ReleaseInfo?> FromManifestAsync()
    {
        try
        {
            using var document = JsonDocument.Parse(await _fetch(ManifestUrl));

            if (!document.RootElement.TryGetProperty("version", out var field)) return null;
            if (field.GetString() is not { Length: > 0 } version) return null;

            var tag = "v" + version;
            var assets = ArchiveNames
                .Select(name => new ReleaseAsset(
                    name, $"https://github.com/{BuildInfo.Repository}/releases/download/{tag}/{name}"))
                .ToList();

            return new ReleaseInfo(version, tag, assets);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private async Task<ReleaseInfo?> FromApiAsync()
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
