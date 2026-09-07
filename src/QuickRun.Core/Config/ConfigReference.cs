namespace QuickRun.Core.Config;

/// <summary>What kind of thing a config reference names.</summary>
public enum ConfigReferenceKind
{
    /// <summary>Nothing that may be acted on.</summary>
    Invalid,

    /// <summary>The config QuickRun keeps for this repository.</summary>
    Collection,

    /// <summary>A file in the repository being run, named relative to its root.</summary>
    InsideRepository,

    /// <summary>A config published at an https address.</summary>
    Remote,
}

/// <summary>
/// Which config a link, a button or a request may ask for - and which it may not.
/// <para>
/// One place decides this because three parts of QuickRun have to agree on it: the parser for a
/// <c>quickrun://</c> link, the daemon's endpoint, and the pipeline that finally reads the file. A
/// second opinion anywhere would be a way in.
/// </para>
/// <para>
/// What is never allowed, whatever it looks like: commands. A reference names a file - by a path
/// inside the repository, by an address, or by the word <c>collection</c> - and QuickRun fetches it
/// itself, so what runs is always something a person can read in the window before approving it.
/// A path on the reader's own disk is not a reference either: that is something you point at
/// yourself, from the command line or a shell verb.
/// </para>
/// </summary>
public sealed record ConfigReference(ConfigReferenceKind Kind, string Value)
{
    /// <summary>The keyword for the config QuickRun keeps, rather than the repository's own.</summary>
    public const string CollectionKeyword = "collection";

    /// <summary>An address may be long. A name inside a repository has no reason to be.</summary>
    private const int MostCharacters = 400;
    private const int MostPathCharacters = 200;

    public static readonly ConfigReference None = new(ConfigReferenceKind.Invalid, "");

    public bool Usable => Kind != ConfigReferenceKind.Invalid;

    /// <summary>Reads a reference, or <see cref="None"/> when it is not one.</summary>
    public static ConfigReference Read(string? value)
    {
        var text = (value ?? "").Trim();

        if (text.Length == 0 || text.Length > MostCharacters) return None;
        if (text.Any(char.IsControl)) return None;

        if (string.Equals(text, CollectionKeyword, StringComparison.OrdinalIgnoreCase))
            return new(ConfigReferenceKind.Collection, CollectionKeyword);

        return text.Contains("://", StringComparison.Ordinal) ? Remote(text) : InsideRepository(text);
    }

    /// <summary>
    /// A published config, at an address the daemon will fetch itself.
    /// <para>
    /// https only, and never an address on this machine or its network. The plan is still read and
    /// approved in QuickRun's window, so a config from elsewhere cannot run anything on its own -
    /// but a fetch is a request the daemon makes from inside a network, and without this a page
    /// could use it to find out what answers on 192.168.x.x. That is not a config feature.
    /// </para>
    /// </summary>
    private static ConfigReference Remote(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return None;
        if (uri.Scheme != Uri.UriSchemeHttps) return None;

        // Credentials in the address would be sent by the daemon on somebody else's behalf.
        if (!string.IsNullOrEmpty(uri.UserInfo)) return None;

        if (Reachable(uri.Host)) return None;
        if (!IsConfigFile(uri.AbsolutePath)) return None;

        return new(ConfigReferenceKind.Remote, uri.ToString());
    }

    private static ConfigReference InsideRepository(string text)
    {
        if (text.Length > MostPathCharacters) return None;
        if (Path.IsPathRooted(text)) return None;

        // A home-relative path is a path on the reader's machine, whatever the shell would make of
        // it - and a name inside a repository never starts with one.
        if (text.StartsWith('~')) return None;

        // A drive letter or a UNC path, which Path.IsPathRooted does not call rooted on every
        // platform - and a Windows path arriving from a link is not a name inside a repository.
        if (text.Length > 1 && text[1] == ':') return None;
        if (text.StartsWith("\\\\", StringComparison.Ordinal)) return None;

        var segments = text.Split('/', '\\');
        if (segments.Any(s => s.Length == 0 || s is "." or "..")) return None;

        return IsConfigFile(text) ? new(ConfigReferenceKind.InsideRepository, text) : None;
    }

    private static bool IsConfigFile(string path) =>
        path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a host is on this machine or the network around it.
    /// <para>
    /// A name with no dot in it is included: on a company network that is how an internal service is
    /// addressed, and it is exactly what must not be reachable this way.
    /// </para>
    /// </summary>
    public static bool Reachable(string host)
    {
        var name = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (name.Length == 0) return true;

        if (name is "localhost" || name.EndsWith(".localhost", StringComparison.Ordinal)) return true;
        if (name.EndsWith(".local", StringComparison.Ordinal)) return true;
        if (name.EndsWith(".internal", StringComparison.Ordinal)) return true;
        if (name.EndsWith(".home.arpa", StringComparison.Ordinal)) return true;

        if (System.Net.IPAddress.TryParse(name.Trim('[', ']'), out var address))
        {
            if (System.Net.IPAddress.IsLoopback(address)) return true;

            var bytes = address.GetAddressBytes();

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return bytes[0] == 10
                       || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                       || (bytes[0] == 192 && bytes[1] == 168)
                       || (bytes[0] == 169 && bytes[1] == 254)
                       || bytes[0] == 0;

            // Unique-local (fc00::/7) and link-local (fe80::/10).
            return (bytes[0] & 0xfe) == 0xfc || (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80);
        }

        // Not an address and not a public name: a single label is an intranet name.
        return !name.Contains('.');
    }
}
