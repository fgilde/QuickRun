using System.Text;
using QuickRun.Core;
using QuickRun.Core.Update;

namespace QuickRun.Core.Tests;

public class InstallSourceTests
{
    [Theory]
    [InlineData(@"C:\Users\me\scoop\apps\quickrun\current\quickrun.exe", InstallSource.Scoop)]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WinGet\Packages\fgilde.QuickRun_x\quickrun.exe", InstallSource.Winget)]
    [InlineData("/opt/homebrew/bin/quickrun", InstallSource.Brew)]
    [InlineData("/usr/local/Cellar/quickrun/0.1.0/bin/quickrun", InstallSource.Brew)]
    [InlineData("/home/linuxbrew/.linuxbrew/bin/quickrun", InstallSource.Brew)]
    [InlineData("/usr/bin/quickrun", InstallSource.Apt)]
    [InlineData("/home/me/.local/bin/quickrun", InstallSource.Standalone)]
    [InlineData(@"C:\tools\quickrun\quickrun.exe", InstallSource.Standalone)]
    public void Detect_reads_the_owner_off_the_path(string path, InstallSource expected)
        => Assert.Equal(expected, InstallSources.Detect(path));

    [Fact]
    public void A_marker_file_overrides_path_detection()
        => Assert.Equal(InstallSource.Apt, InstallSources.Detect("/home/me/.local/bin/quickrun", "apt\n"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    public void An_unusable_marker_falls_back_to_the_path(string? marker)
        => Assert.Equal(InstallSource.Standalone, InstallSources.Detect("/home/me/bin/quickrun", marker));

    [Fact]
    public void ReadMarker_returns_null_when_there_is_no_marker()
    {
        using var home = new TempHome();
        Assert.Null(InstallSources.ReadMarker(home.Path));
    }

    [Fact]
    public void ReadMarker_reads_what_an_installer_wrote()
    {
        using var home = new TempHome();
        File.WriteAllText(Path.Combine(home.Path, InstallSources.MarkerFileName), "brew\n");

        Assert.Equal(InstallSource.Brew, InstallSources.Parse(InstallSources.ReadMarker(home.Path)));
    }

    /// <summary>
    /// The case the marker exists for: our own installer put the binary somewhere a package manager
    /// would normally own, and path detection alone would get it wrong.
    /// </summary>
    [Fact]
    public void A_marker_rescues_a_standalone_install_into_usr_bin()
    {
        Assert.Equal(InstallSource.Apt, InstallSources.Detect("/usr/bin/quickrun"));
        Assert.Equal(InstallSource.Standalone, InstallSources.Detect("/usr/bin/quickrun", "standalone"));
    }

    [Fact]
    public void Only_a_standalone_install_may_replace_itself()
    {
        Assert.True(InstallSource.Standalone.MayReplaceItself());
        foreach (var managed in new[] { InstallSource.Winget, InstallSource.Scoop, InstallSource.Brew, InstallSource.Apt })
            Assert.False(managed.MayReplaceItself());
    }

    [Fact]
    public void Every_managed_source_names_its_upgrade_command()
    {
        Assert.Equal("winget upgrade fgilde.QuickRun", InstallSource.Winget.UpgradeCommand());
        Assert.Equal("scoop update quickrun", InstallSource.Scoop.UpgradeCommand());
        Assert.Equal("brew upgrade quickrun", InstallSource.Brew.UpgradeCommand());
        Assert.Equal("apt upgrade quickrun", InstallSource.Apt.UpgradeCommand());
    }
}

public class UpdateCheckerTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "assets": [
            { "name": "quickrun-win-x64.zip",
              "browser_download_url": "https://github.com/fgilde/QuickRun/releases/download/v1.2.0/quickrun-win-x64.zip" },
            { "name": "quickrun-linux-x64.tar.gz",
              "browser_download_url": "https://github.com/fgilde/QuickRun/releases/download/v1.2.0/quickrun-linux-x64.tar.gz" },
            { "name": "QuickRun-osx-arm64.app.zip",
              "browser_download_url": "https://github.com/fgilde/QuickRun/releases/download/v1.2.0/QuickRun-osx-arm64.app.zip" },
            { "name": "quickrun-osx-arm64.tar.gz",
              "browser_download_url": "https://github.com/fgilde/QuickRun/releases/download/v1.2.0/quickrun-osx-arm64.tar.gz" },
            { "name": "SHA256SUMS",
              "browser_download_url": "https://github.com/fgilde/QuickRun/releases/download/v1.2.0/SHA256SUMS" }
          ]
        }
        """;

    private static UpdateChecker Checker(string json = ReleaseJson) => new(_ => Task.FromResult(json));

    [Fact]
    public async Task The_tag_is_read_without_its_v_prefix()
        => Assert.Equal("1.2.0", (await Checker().LatestAsync())!.Version);

    [Fact]
    public async Task Assets_are_listed_with_their_download_urls()
    {
        var release = await Checker().LatestAsync();
        Assert.Equal(5, release!.Assets.Count);
        Assert.StartsWith("https://github.com/", release.Asset("SHA256SUMS")!.Url);
    }

    [Fact]
    public async Task AssetFor_picks_the_archive_and_not_the_app_bundle()
    {
        var release = await Checker().LatestAsync();
        Assert.Equal("quickrun-osx-arm64.tar.gz", release!.AssetFor("osx-arm64")!.Name);
    }

    [Fact]
    public async Task An_older_current_version_reports_an_update()
    {
        var status = await Checker().CheckAsync("1.1.0", InstallSource.Standalone);
        Assert.True(status.UpdateAvailable);
        Assert.Equal("1.2.0", status.Latest);
        Assert.Contains("1.2.0", status.Advice);
    }

    [Fact]
    public async Task The_same_version_reports_no_update()
        => Assert.False((await Checker().CheckAsync("1.2.0", InstallSource.Standalone)).UpdateAvailable);

    [Fact]
    public async Task A_newer_local_build_reports_no_update()
        => Assert.False((await Checker().CheckAsync("1.3.0", InstallSource.Standalone)).UpdateAvailable);

    [Fact]
    public async Task A_managed_install_is_told_which_command_to_run()
    {
        var status = await Checker().CheckAsync("1.1.0", InstallSource.Brew);
        Assert.True(status.UpdateAvailable);
        Assert.Contains("brew upgrade quickrun", status.Advice);
    }

    [Fact]
    public async Task A_failed_request_is_reported_rather_than_thrown()
    {
        var checker = new UpdateChecker(_ => throw new HttpRequestException("no network"));
        var status = await checker.CheckAsync("1.0.0", InstallSource.Standalone);

        Assert.False(status.UpdateAvailable);
        Assert.Contains("no network", status.Error!);
    }

    [Fact]
    public async Task Malformed_json_is_reported_rather_than_thrown()
    {
        var status = await new UpdateChecker(_ => Task.FromResult("{not json")).CheckAsync("1.0.0", InstallSource.Standalone);
        Assert.NotNull(status.Error);
    }
}

public class UpdaterTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("new binary contents");

    private static string PayloadSum => Updater.Sha256(Payload);

    private static ReleaseInfo Release(string assetUrl, string sumsUrl) => new("1.2.0", "v1.2.0", new[]
    {
        new ReleaseAsset("quickrun-linux-x64.tar.gz", assetUrl),
        new ReleaseAsset("SHA256SUMS", sumsUrl),
    });

    private const string GoodAssetUrl =
        "https://github.com/fgilde/QuickRun/releases/download/v1.2.0/quickrun-linux-x64.tar.gz";

    private const string GoodSumsUrl =
        "https://github.com/fgilde/QuickRun/releases/download/v1.2.0/SHA256SUMS";

    [Theory]
    [InlineData(GoodAssetUrl, true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset/1/2", true)]
    [InlineData("http://github.com/fgilde/QuickRun/releases/download/v1/x.zip", false)]
    [InlineData("https://evil.example.com/fgilde/QuickRun/releases/download/v1/x.zip", false)]
    [InlineData("https://github.com/someone-else/QuickRun/releases/download/v1/x.zip", false)]
    [InlineData("https://github.com/fgilde/QuickRun/raw/main/x.zip", false)]
    [InlineData("not a url", false)]
    public void Only_github_release_downloads_are_trusted(string url, bool trusted)
        => Assert.Equal(trusted, Updater.IsTrustedAssetUrl(url));

    [Fact]
    public void ExpectedSum_reads_the_entry_for_one_asset()
    {
        var sums = "aaa  other.zip\nbbb  quickrun-linux-x64.tar.gz\n";
        Assert.Equal("bbb", Updater.ExpectedSum(sums, "quickrun-linux-x64.tar.gz"));
    }

    [Fact]
    public void ExpectedSum_tolerates_the_binary_mode_asterisk()
        => Assert.Equal("bbb", Updater.ExpectedSum("bbb *file.zip\n", "file.zip"));

    [Fact]
    public void ExpectedSum_returns_null_for_an_unlisted_asset()
        => Assert.Null(Updater.ExpectedSum("aaa  other.zip\n", "missing.zip"));

    [Fact]
    public async Task A_matching_checksum_yields_the_content()
    {
        var updater = new Updater(
            download: _ => Task.FromResult(Payload),
            fetchText: _ => Task.FromResult($"{PayloadSum}  quickrun-linux-x64.tar.gz\n"));

        var (content, error) = await updater.FetchVerifiedAsync(
            Release(GoodAssetUrl, GoodSumsUrl), "quickrun-linux-x64.tar.gz");

        Assert.Null(error);
        Assert.Equal(Payload, content);
    }

    [Fact]
    public async Task A_mismatched_checksum_aborts_and_returns_no_content()
    {
        var updater = new Updater(
            download: _ => Task.FromResult(Payload),
            fetchText: _ => Task.FromResult("0000  quickrun-linux-x64.tar.gz\n"));

        var (content, error) = await updater.FetchVerifiedAsync(
            Release(GoodAssetUrl, GoodSumsUrl), "quickrun-linux-x64.tar.gz");

        Assert.Null(content);
        Assert.Contains("checksum mismatch", error!);
    }

    [Fact]
    public async Task An_untrusted_asset_url_is_refused_before_downloading()
    {
        var downloaded = false;
        var updater = new Updater(
            download: _ => { downloaded = true; return Task.FromResult(Payload); },
            fetchText: _ => Task.FromResult($"{PayloadSum}  quickrun-linux-x64.tar.gz\n"));

        var (content, error) = await updater.FetchVerifiedAsync(
            Release("https://evil.example.com/x.tar.gz", GoodSumsUrl), "quickrun-linux-x64.tar.gz");

        Assert.Null(content);
        Assert.Contains("refusing", error!);
        Assert.False(downloaded);
    }

    [Fact]
    public async Task An_untrusted_checksum_url_is_refused()
    {
        var updater = new Updater(
            download: _ => Task.FromResult(Payload),
            fetchText: _ => Task.FromResult($"{PayloadSum}  quickrun-linux-x64.tar.gz\n"));

        var (_, error) = await updater.FetchVerifiedAsync(
            Release(GoodAssetUrl, "https://evil.example.com/SHA256SUMS"), "quickrun-linux-x64.tar.gz");

        Assert.Contains("refusing", error!);
    }

    [Fact]
    public async Task A_release_without_checksums_is_refused()
    {
        var release = new ReleaseInfo("1.2.0", "v1.2.0", new[] { new ReleaseAsset("quickrun-linux-x64.tar.gz", GoodAssetUrl) });
        var updater = new Updater(download: _ => Task.FromResult(Payload), fetchText: _ => Task.FromResult(""));

        var (content, error) = await updater.FetchVerifiedAsync(release, "quickrun-linux-x64.tar.gz");

        Assert.Null(content);
        Assert.Contains("SHA256SUMS", error!);
    }

    [Fact]
    public async Task A_download_failure_is_reported_rather_than_thrown()
    {
        var updater = new Updater(
            download: _ => throw new HttpRequestException("connection reset"),
            fetchText: _ => Task.FromResult($"{PayloadSum}  quickrun-linux-x64.tar.gz\n"));

        var (_, error) = await updater.FetchVerifiedAsync(
            Release(GoodAssetUrl, GoodSumsUrl), "quickrun-linux-x64.tar.gz");

        Assert.Contains("connection reset", error!);
    }

    [Fact]
    public void Swap_replaces_the_binary_and_keeps_the_old_one_aside()
    {
        var dir = Path.Combine(Path.GetTempPath(), "quickrun-swap-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);
        var current = Path.Combine(dir, "quickrun");
        File.WriteAllText(current, "old binary");

        try
        {
            var result = Updater.Swap(current, Payload, "1.2.0");

            Assert.True(result.Ok, result.Error);
            Assert.Equal("1.2.0", result.InstalledVersion);
            Assert.Equal(Payload, File.ReadAllBytes(current));
            Assert.Equal("old binary", File.ReadAllText(current + ".old"));

            Updater.CleanUpAfterSwap(current);
            Assert.False(File.Exists(current + ".old"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Swap_leaves_no_staging_file_behind()
    {
        var dir = Path.Combine(Path.GetTempPath(), "quickrun-swap-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);
        var current = Path.Combine(dir, "quickrun");
        File.WriteAllText(current, "old binary");

        try
        {
            Updater.Swap(current, Payload, "1.2.0");
            Assert.False(File.Exists(current + ".new"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CleanUpAfterSwap_is_safe_when_there_is_nothing_to_clean()
        => Updater.CleanUpAfterSwap(Path.Combine(Path.GetTempPath(), "quickrun-never-existed"));

    [Fact]
    public void Sha256_matches_a_known_value()
        => Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
            Updater.Sha256(Encoding.UTF8.GetBytes("hello world")));
}
