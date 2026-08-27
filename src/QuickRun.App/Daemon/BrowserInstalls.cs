using System.Text.Json;

namespace QuickRun.App.Daemon;

/// <param name="Id">Stable key the dashboard posts back to say which browser to act on.</param>
/// <param name="Name">What the browser calls itself.</param>
/// <param name="Family">chromium or firefox - it decides what installing even means.</param>
/// <param name="Executable">Where it was found, so it can be started with a URL.</param>
/// <param name="Extension">
/// Whether QuickRun's extension is there: "installed" when it was found in a profile, "connected"
/// when it has talked to this daemon, "missing" when neither. Not a guess in either direction -
/// each answer names what was actually observed.
/// </param>
/// <param name="Store">Where this browser installs extensions from, once the listing is live.</param>
public sealed record BrowserInstall(
    string Id,
    string Name,
    string Family,
    string Executable,
    string Extension,
    string? Store);

/// <summary>
/// The browsers on this machine, and whether each one has QuickRun's extension.
/// <para>
/// Installing an extension into Chrome or Edge from the outside is not possible, and has not been
/// since inline installation was removed in 2018 - the only mechanism left is an enterprise policy,
/// which force-installs an extension the user cannot then remove. QuickRun does not do that. What
/// it can do is find the browsers, say which of them already has the extension, and open the right
/// page in the right browser so the remaining click is the browser's own.
/// </para>
/// </summary>
public static class BrowserInstalls
{
    /// <summary>The add-on id from the Firefox manifest, which is what a profile lists it under.</summary>
    private const string FirefoxId = "quickrun@fgilde.org";

    /// <summary>
    /// The extension ids each store hands out. Chrome's listing does not exist yet, so there is
    /// nothing to look for and nothing to open - the dashboard says so rather than pretending.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> StoreIds =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["chrome"] = null,
            ["edge"] = "dbnknhijahmiildfabckibabpieobnhd",
        };

    /// <summary>
    /// Extension origins this daemon has answered. An extension that has made a request is
    /// installed, whatever a profile directory does or does not show - and that covers the unpacked
    /// case, which leaves no predictable directory at all.
    /// </summary>
    private static readonly HashSet<string> Seen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Called for every authorised request, so an origin is remembered the first time.</summary>
    public static void Remember(string origin)
    {
        if (string.IsNullOrEmpty(origin)) return;
        lock (Seen) Seen.Add(origin);
    }

    private static bool HasConnected(string family)
    {
        lock (Seen)
            return family == "firefox"
                ? Seen.Any(o => o.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase))
                : Seen.Any(o => o.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<BrowserInstall> All()
    {
        var found = new List<BrowserInstall>();

        foreach (var candidate in Candidates())
        {
            var executable = candidate.Paths.FirstOrDefault(Exists);
            if (executable is null) continue;

            found.Add(new BrowserInstall(
                candidate.Id,
                candidate.Name,
                candidate.Family,
                executable,
                State(candidate),
                Store(candidate)));
        }

        return found;
    }

    private static string State(Candidate candidate)
    {
        if (InProfile(candidate)) return "installed";

        // Weaker, and only usable per family: an origin says which browser engine asked, never
        // which of two Chromium browsers it was.
        if (HasConnected(candidate.Family)) return "connected";

        return "missing";
    }

    /// <summary>Whether a profile on disk carries the extension.</summary>
    private static bool InProfile(Candidate candidate)
    {
        try
        {
            foreach (var root in candidate.Profiles.Where(Directory.Exists))
            {
                if (candidate.Family == "firefox" && FirefoxHas(root)) return true;
                if (candidate.Family != "firefox" && ChromiumHas(root, candidate.Id)) return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked or unreadable profile is not an answer, and not worth an error either.
        }

        return false;
    }

    /// <summary>
    /// Whether a Chromium profile carries the extension, installed either way.
    /// <para>
    /// A store install has a directory named after its id. An unpacked one has no such directory
    /// and no predictable id at all - it is listed in <c>Secure Preferences</c>, where Chromium
    /// keeps extension settings, with the folder it was loaded from. And for an unpacked build the
    /// manifest name is often not cached there either, so the path is what actually answers.
    /// </para>
    /// </summary>
    private static bool ChromiumHas(string userData, string browser)
    {
        foreach (var profile in Directory.EnumerateDirectories(userData))
        {
            if (StoreIds.GetValueOrDefault(browser) is { } id
                && Directory.Exists(Path.Combine(profile, "Extensions", id)))
                return true;

            // Secure Preferences first: that is where an unpacked extension is recorded.
            foreach (var name in new[] { "Secure Preferences", "Preferences" })
                if (ListedIn(Path.Combine(profile, name)))
                    return true;
        }

        return false;
    }

    /// <summary>Looks through one preferences file's extension list for ours.</summary>
    private static bool ListedIn(string file)
    {
        if (!File.Exists(file)) return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));

            if (!document.RootElement.TryGetProperty("extensions", out var extensions)) return false;
            if (!extensions.TryGetProperty("settings", out var settings)) return false;

            foreach (var entry in settings.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;

                // An unpacked extension carries the folder it was loaded from, and that folder is
                // named after what was unpacked into it.
                if (entry.Value.TryGetProperty("path", out var path)
                    && path.GetString() is { } location
                    && location.Contains("quickrun", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (entry.Value.TryGetProperty("manifest", out var manifest)
                    && manifest.ValueKind == JsonValueKind.Object
                    && manifest.TryGetProperty("name", out var declared)
                    && declared.GetString() == "QuickRun")
                    return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A browser holds these open while it runs, and a half-written one is not an answer.
        }

        return false;
    }

    private static bool FirefoxHas(string profilesRoot)
    {
        foreach (var profile in Directory.EnumerateDirectories(profilesRoot))
        {
            if (File.Exists(Path.Combine(profile, "extensions", $"{FirefoxId}.xpi"))) return true;

            var list = Path.Combine(profile, "extensions.json");
            if (!File.Exists(list)) continue;

            try
            {
                if (File.ReadAllText(list).Contains(FirefoxId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    /// <summary>The page that installs it, or null while that store has no listing yet.</summary>
    private static string? Store(Candidate candidate) => candidate.Family == "firefox"
        ? null
        : StoreIds.GetValueOrDefault(candidate.Id) is { } id
            ? candidate.Id == "edge"
                ? $"https://microsoftedge.microsoft.com/addons/detail/quickrun/{id}"
                : $"https://chromewebstore.google.com/detail/{id}"
            : null;

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private sealed record Candidate(
        string Id, string Name, string Family, string[] Paths, string[] Profiles);

    private static IEnumerable<Candidate> Candidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programsX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (OperatingSystem.IsWindows())
        {
            yield return new("chrome", "Google Chrome", "chromium",
                [Path.Combine(programs, @"Google\Chrome\Application\chrome.exe"),
                 Path.Combine(programsX86, @"Google\Chrome\Application\chrome.exe")],
                [Path.Combine(local, @"Google\Chrome\User Data")]);

            yield return new("edge", "Microsoft Edge", "chromium",
                [Path.Combine(programsX86, @"Microsoft\Edge\Application\msedge.exe"),
                 Path.Combine(programs, @"Microsoft\Edge\Application\msedge.exe")],
                [Path.Combine(local, @"Microsoft\Edge\User Data")]);

            yield return new("brave", "Brave", "chromium",
                [Path.Combine(programs, @"BraveSoftware\Brave-Browser\Application\brave.exe")],
                [Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data")]);

            yield return new("vivaldi", "Vivaldi", "chromium",
                [Path.Combine(local, @"Vivaldi\Application\vivaldi.exe"),
                 Path.Combine(programs, @"Vivaldi\Application\vivaldi.exe")],
                [Path.Combine(local, @"Vivaldi\User Data")]);

            yield return new("opera", "Opera", "chromium",
                [Path.Combine(local, @"Programs\Opera\opera.exe"),
                 Path.Combine(programs, @"Opera\opera.exe")],
                [Path.Combine(roaming, @"Opera Software\Opera Stable")]);

            yield return new("firefox", "Mozilla Firefox", "firefox",
                [Path.Combine(programs, @"Mozilla Firefox\firefox.exe"),
                 Path.Combine(programsX86, @"Mozilla Firefox\firefox.exe")],
                [Path.Combine(roaming, @"Mozilla\Firefox\Profiles")]);

            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            var support = Path.Combine(home, "Library/Application Support");

            yield return new("chrome", "Google Chrome", "chromium",
                ["/Applications/Google Chrome.app"], [Path.Combine(support, "Google/Chrome")]);

            yield return new("edge", "Microsoft Edge", "chromium",
                ["/Applications/Microsoft Edge.app"], [Path.Combine(support, "Microsoft Edge")]);

            yield return new("brave", "Brave", "chromium",
                ["/Applications/Brave Browser.app"],
                [Path.Combine(support, "BraveSoftware/Brave-Browser")]);

            yield return new("vivaldi", "Vivaldi", "chromium",
                ["/Applications/Vivaldi.app"], [Path.Combine(support, "Vivaldi")]);

            yield return new("opera", "Opera", "chromium",
                ["/Applications/Opera.app"], [Path.Combine(support, "com.operasoftware.Opera")]);

            yield return new("firefox", "Mozilla Firefox", "firefox",
                ["/Applications/Firefox.app"], [Path.Combine(support, "Firefox/Profiles")]);

            yield break;
        }

        var config = Path.Combine(home, ".config");

        yield return new("chrome", "Google Chrome", "chromium",
            ["/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/snap/bin/chromium"],
            [Path.Combine(config, "google-chrome"), Path.Combine(config, "chromium")]);

        yield return new("edge", "Microsoft Edge", "chromium",
            ["/usr/bin/microsoft-edge", "/usr/bin/microsoft-edge-stable"],
            [Path.Combine(config, "microsoft-edge")]);

        yield return new("brave", "Brave", "chromium",
            ["/usr/bin/brave-browser", "/snap/bin/brave"],
            [Path.Combine(config, "BraveSoftware/Brave-Browser")]);

        yield return new("vivaldi", "Vivaldi", "chromium",
            ["/usr/bin/vivaldi", "/usr/bin/vivaldi-stable"], [Path.Combine(config, "vivaldi")]);

        yield return new("opera", "Opera", "chromium",
            ["/usr/bin/opera"], [Path.Combine(config, "opera")]);

        yield return new("firefox", "Mozilla Firefox", "firefox",
            ["/usr/bin/firefox", "/snap/bin/firefox"], [Path.Combine(home, ".mozilla/firefox")]);
    }
}
