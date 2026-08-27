using System.Diagnostics;
using System.IO.Compression;
using QuickRun.App.Commands;
using QuickRun.Core;
using QuickRun.Core.Update;

namespace QuickRun.App.Daemon;

/// <param name="Ok">Whether anything was actually started.</param>
/// <param name="Route">
/// Which way was taken: <c>store</c> when the browser's own listing was opened, <c>unpacked</c>
/// when the extension was unpacked and the browser's extensions page opened, <c>none</c> when
/// there is nothing to do.
/// </param>
/// <param name="Folder">Where the unpacked extension is, when that route was taken.</param>
/// <param name="Message">What the person in front of the screen now has to do, if anything.</param>
public sealed record InstallOutcome(bool Ok, string Route, string? Folder, string Message);

/// <summary>
/// Gets QuickRun's extension into a browser, as far as a browser allows.
/// <para>
/// Which is not far, and deliberately so: Chrome removed inline installation in 2018, and the only
/// remaining way for a program to put an extension into Chrome or Edge is an enterprise policy that
/// force-installs it and takes away the user's ability to remove it. That is a thing malware does.
/// So this stops one click short on purpose - it opens the listing, in the right browser, and the
/// last click is the browser's own.
/// </para>
/// <para>
/// Until the listings are live there is still something better than a documentation link: the
/// packaged extension is downloaded from the release, unpacked, and the browser's extensions page
/// is opened next to the folder. That leaves "load unpacked, pick this folder" - two clicks with
/// nothing to look up.
/// </para>
/// </summary>
public static class ExtensionInstall
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static string AssetFor(string family) =>
        family == "firefox" ? "quickrun-extension-firefox.zip" : "quickrun-extension-chromium.zip";

    /// <summary>Where the browser lists what it has installed, so the folder can be dropped on it.</summary>
    private static string ExtensionsPage(string browserId) => browserId switch
    {
        "edge" => "edge://extensions",
        "brave" => "brave://extensions",
        "vivaldi" => "vivaldi://extensions",
        "opera" => "opera://extensions",
        "firefox" => "about:debugging#/runtime/this-firefox",
        _ => "chrome://extensions",
    };

    public static async Task<InstallOutcome> RunAsync(BrowserInstall browser, string home, CancellationToken ct)
    {
        // The store, when there is one. One click left, and it is the browser's own install button.
        if (browser.Store is { } listing)
        {
            if (!Launch(browser.Executable, listing))
                return new(false, "none", null, $"{browser.Name} could not be started.");

            return new(true, "store", null,
                $"{browser.Name} is opening the listing. Press Add there to install it.");
        }

        var folder = Path.Combine(home, "extension", browser.Family);

        try
        {
            await UnpackAsync(browser.Family, folder, ct);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException)
        {
            return new(false, "none", null, $"The extension could not be downloaded: {e.Message}");
        }

        Launch(browser.Executable, ExtensionsPage(browser.Id));
        Reveal(folder);

        return new(true, "unpacked", folder, browser.Family == "firefox"
            ? "Firefox is opening its debugging page. Choose \"Load Temporary Add-on\" and pick "
              + "manifest.json in the folder that just opened. Firefox forgets a temporary add-on "
              + "when it closes - the listing on addons.mozilla.org is the permanent way, and it is "
              + "still in review."
            : $"{browser.Name} is opening its extensions page. Turn on Developer mode, choose "
              + "\"Load unpacked\", and pick the folder that just opened.");
    }

    /// <summary>
    /// Downloads the packaged extension from the newest release and unpacks it, replacing whatever
    /// was there. Trusted URLs only - the same rule auto-update follows, because this is the same
    /// kind of act: running code fetched from the internet.
    /// </summary>
    private static async Task UnpackAsync(string family, string folder, CancellationToken ct)
    {
        var asset = AssetFor(family);

        // This build's own release, not whatever is newest. The extension and the listener speak a
        // contract, and pairing a new extension with an older QuickRun is a mismatch nobody would
        // think to suspect - so the version that ships with this binary is the one installed, and
        // "latest" is only the fallback for a build that has no release of its own.
        var pinned = $"https://github.com/{BuildInfo.Repository}/releases/download/v{BuildInfo.Version}/{asset}";
        var latest = $"https://github.com/{BuildInfo.Repository}/releases/latest/download/{asset}";

        var zip = await FetchAsync(pinned, ct) ?? await FetchAsync(latest, ct)
            ?? throw new HttpRequestException($"neither {pinned} nor {latest} could be downloaded");

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        using var buffer = new MemoryStream(zip);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;

            var target = Path.GetFullPath(Path.Combine(folder, entry.FullName));

            // A zip entry may not escape the folder it is being unpacked into.
            if (!target.StartsWith(Path.GetFullPath(folder), StringComparison.Ordinal)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>One download attempt. Null when that URL has nothing, so the caller can fall back.</summary>
    private static async Task<byte[]?> FetchAsync(string url, CancellationToken ct)
    {
        if (!Updater.IsTrustedAssetUrl(url))
            throw new HttpRequestException($"{url} is not a release asset URL");

        try
        {
            return await Http.GetByteArrayAsync(url, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static bool Launch(string executable, string url)
    {
        try
        {
            // A .app bundle is a directory: macOS starts it through open, with the URL as argument.
            if (OperatingSystem.IsMacOS() && executable.EndsWith(".app", StringComparison.Ordinal))
            {
                Process.Start(new ProcessStartInfo("open", ["-a", executable, url]) { UseShellExecute = false });
                return true;
            }

            Process.Start(new ProcessStartInfo(executable, [url]) { UseShellExecute = false });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Opens the folder, so the browser's file picker has somewhere obvious to land.</summary>
    private static void Reveal(string folder)
    {
        try { UiCommand.Launch(folder); }
        catch (Exception) { /* a file manager that will not open is not worth failing over */ }
    }
}
