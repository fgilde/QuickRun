namespace QuickRun.Core.Config;

/// <summary>
/// The web sites allowed to ask QuickRun to open its window.
/// <para>
/// A page cannot start anything, and this does not change that. What it changes is one step of the
/// handover: a trusted page may ask the local QuickRun to open its own window on a plan, instead of
/// the reader being sent through a <c>quickrun://</c> link or a new tab to get there. The plan still
/// appears in QuickRun's window, and it still waits for a person. The worst a trusted site can do is
/// make a window appear.
/// </para>
/// <para>
/// quickrun.org is trusted to begin with because it is where the application comes from: a reader
/// who installed QuickRun from that page has already trusted it with rather more than a window.
/// Anything else is the user's to add.
/// </para>
/// <para>
/// One pattern per line in a file, because that is a setting somebody can read, comment and check
/// into their own notes - and because the list has to be inspectable to be worth anything. With no
/// file at all the default applies; with an empty file nothing is trusted, which is how the default
/// is turned off.
/// </para>
/// </summary>
public sealed class TrustedSites(string root)
{
    public const string FileName = "trusted-sites.txt";

    /// <summary>Trusted until the user says otherwise: the site QuickRun is downloaded from.</summary>
    public static readonly string[] Default = { "*.quickrun.org" };

    private static readonly string Header = string.Join('\n',
        "# Web sites allowed to ask QuickRun to open its window on a plan.",
        "#",
        "# One host per line. A leading *. also covers subdomains, so *.example.com matches",
        "# example.com and app.example.com - and nothing else, in particular not",
        "# notexample.com or example.com.attacker.net.",
        "#",
        "# A site listed here still cannot start a run: it can only ask for the window, and",
        "# whatever it asks for waits there until you approve it. Only https counts, except on",
        "# this machine, where http://localhost is allowed too.",
        "#",
        "# Delete every line to trust nothing. Delete the whole file to go back to the default.",
        "");

    public string Path => System.IO.Path.Combine(root, FileName);

    /// <summary>
    /// The list as it stands. The file wins whenever it exists - including when it is empty, which
    /// is a decision rather than an accident.
    /// </summary>
    public IReadOnlyList<string> Patterns
    {
        get
        {
            var text = Read();
            if (text is null) return Default;

            return text
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && line[0] != '#')
                .Select(Normalise)
                .Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Whether this <c>Origin</c> header may ask for the window.</summary>
    public bool Trusts(string? origin) => Trusts(origin, Patterns);

    /// <summary>
    /// The same decision, against a given list. Separated so the rule can be tested as a rule.
    /// </summary>
    public static bool Trusts(string? origin, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

        // http is not good enough for a site that gets to reach into this machine: on the way here
        // anything can rewrite it, and the origin header would still read as the trusted name. The
        // exception is this machine itself, where there is no way in between.
        var loopback = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !(loopback && uri.Scheme == Uri.UriSchemeHttp))
            return false;

        // An Origin is scheme, host and port and nothing else. A path, a query or a fragment means
        // this is not an Origin header, and reading one out of it would mean matching on text that
        // whoever sent it controls.
        if (uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        var host = uri.Host;

        return patterns.Any(pattern => Matches(pattern, host));
    }

    /// <summary>
    /// Whether a host is covered by one pattern.
    /// <para>
    /// The subdomain form matches on whole labels only. Comparing text ends instead would let
    /// <c>notquickrun.org</c> through <c>*.quickrun.org</c>, which is how this kind of check is
    /// usually got wrong.
    /// </para>
    /// </summary>
    public static bool Matches(string pattern, string host)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(host)) return false;

        pattern = Normalise(pattern);
        host = host.Trim().TrimEnd('.');

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var domain = pattern[2..];

            return domain.Length > 0
                && (host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
        }

        return host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Adds a site, writing the file - with the default already in it - if there is none.</summary>
    public void Add(string pattern)
    {
        var wanted = Normalise(pattern);
        if (wanted.Length == 0) return;

        var current = Patterns.ToList();
        if (current.Any(p => p.Equals(wanted, StringComparison.OrdinalIgnoreCase))) return;

        current.Add(wanted);
        Write(current);
    }

    /// <summary>
    /// Removes a site, and with it any pattern that was covering it.
    /// <para>
    /// Removing <c>quickrun.org</c> from a list that says <c>*.quickrun.org</c> has to work, or the
    /// site stays trusted and the user has been told it is not. Removing the last entry leaves an
    /// empty list rather than the default: the file exists, so it is what the user said.
    /// </para>
    /// </summary>
    public void Remove(string pattern)
    {
        var wanted = Normalise(pattern);
        if (wanted.Length == 0) return;

        var wildcard = wanted.StartsWith("*.", StringComparison.Ordinal);

        var left = Patterns
            .Where(p => !p.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                        && !(!wildcard && Matches(p, wanted)))
            .ToList();

        if (left.Count != Patterns.Count) Write(left);
    }

    /// <summary>
    /// A host, from whatever was typed. A whole URL is accepted because that is what somebody has in
    /// the clipboard, and the host is the only part this can act on.
    /// </summary>
    public static string Normalise(string pattern)
    {
        var text = (pattern ?? "").Trim().Trim('"', '\'');
        if (text.Length == 0) return "";

        if (text.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return uri.Host.ToLowerInvariant();

        // A path or a port typed after the host is not part of it.
        text = text.Split('/', 2)[0].Split(':', 2)[0].TrimEnd('.').ToLowerInvariant();

        return text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '*' or '_')
            ? text
            : "";
    }

    private string? Read()
    {
        try { return File.Exists(Path) ? File.ReadAllText(Path) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void Write(IReadOnlyList<string> patterns)
    {
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path, Header + string.Join('\n', patterns) + '\n');
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
