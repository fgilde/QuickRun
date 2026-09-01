namespace QuickRun.Core.Config;

/// <summary>
/// Turning a single quickrun.yml into something runnable.
/// </summary>
/// <param name="Text">The config itself, handed on rather than looked up again.</param>
/// <param name="Repo">The repository to check out, when the config names one.</param>
/// <param name="LocalFolder">The directory to run in, when it does not and the file is local.</param>
/// <param name="Ref">The ref the config asks for, if any.</param>
/// <param name="Error">Why this cannot run, in words a person can act on.</param>
/// <param name="NeedsRepository">
/// True when the only thing missing is a repository - which the window can ask for, rather than
/// treating it as a failure.
/// </param>
public sealed record ConfigFileTarget(
    string? Text,
    string? Repo,
    string? LocalFolder,
    string? Ref,
    string? Error,
    bool NeedsRepository = false);

/// <summary>
/// Reads a config file on its own and works out what it is for.
/// <para>
/// Two shapes. A config with a <c>repository:</c> is complete in itself: that is what gets checked
/// out, and the file decides how it runs. A config without one is about the code beside it, so the
/// directory it was opened from is what runs - which only means anything when the file is on this
/// machine and somebody pointed at it. Neither of those, and there is nothing to run: the caller is
/// told what is missing instead of a checkout of nothing being attempted.
/// </para>
/// <para>
/// The result is expressed as a config text plus a target, so the run itself takes the path it
/// always took. Nothing about how a repository is prepared, planned or confirmed changes here.
/// </para>
/// </summary>
public static class ConfigFileRun
{
    /// <summary>How large a config may be. A YAML file is kilobytes; anything else is a mistake.</summary>
    private const int MaxBytes = 512 * 1024;

    /// <summary>
    /// What running this file means.
    /// </summary>
    /// <param name="path">The file, as given.</param>
    /// <param name="allowFolder">
    /// Whether the file's own directory may be used when the config names no repository. True for a
    /// file somebody chose here - a picker, a command line - and false for one that arrived from
    /// somewhere else, because "run whatever is next to this file" is not a thing a link may decide.
    /// </param>
    public static ConfigFileTarget Read(string? path, OSKind os, bool allowFolder)
    {
        var file = (path ?? "").Trim();

        if (file.Length == 0) return Failed("no config file was named");

        if (!file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            && !file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            return Failed($"'{file}' is not a .yml file");

        string full;
        try { full = Path.GetFullPath(file); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed($"'{file}' is not a usable path");
        }

        if (!File.Exists(full)) return Failed($"there is no file at {full}");

        string text;
        try
        {
            var length = new FileInfo(full).Length;
            if (length > MaxBytes) return Failed($"{full} is {length / 1024}KB - too large to be a config");

            text = File.ReadAllText(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Failed($"could not read {full}: {e.Message}");
        }

        RunConfig config;
        try { config = ConfigParser.Parse(text, os); }
        catch (ConfigException e) { return Failed($"{Path.GetFileName(full)}: {e.Message}"); }

        // The repository the config names, if it names one. That is the whole point of the field:
        // this file can be somewhere else entirely and still say what it is for.
        if (config.Repository is { Length: > 0 } repo)
            return new ConfigFileTarget(text, repo.Trim(), null, config.Ref, null);

        // Otherwise it is about the code beside it.
        var directory = Path.GetDirectoryName(full);

        if (!allowFolder || directory is null || !Directory.Exists(directory))
            return new ConfigFileTarget(text, null, null, config.Ref,
                "this config does not say which repository it is for - name one",
                NeedsRepository: true);

        return new ConfigFileTarget(text, null, directory, config.Ref, null);
    }

    private static ConfigFileTarget Failed(string why) => new(null, null, null, null, why);
}
