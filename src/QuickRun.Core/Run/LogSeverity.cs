namespace QuickRun.Core.Run;

/// <summary>How bad a line of a run's output is.</summary>
public enum LogSeverity
{
    /// <summary>Ordinary output.</summary>
    Info,

    /// <summary>Something the run got past, and somebody may want to know about.</summary>
    Warn,

    /// <summary>Something did not work.</summary>
    Error,

    /// <summary>Something did not work and nothing after it will either.</summary>
    Fatal,
}

/// <summary>
/// Reads a line of somebody else's output and says how bad it is.
/// <para>
/// Decided here, once, because three places show the same log - QuickRun's own window, the
/// extension's window and the command line - and three heuristics would disagree with each other in
/// front of the same run. It travels on the event, so a filter and a colour are reading the same
/// answer rather than each guessing again.
/// </para>
/// <para>
/// A guess, and it says so: this is arbitrary output from an arbitrary program, and no rule covers
/// every logger ever written. What it is tuned for is the mistake that matters - calling something
/// an error when it is not. A build that ends in "0 Errors" painted red teaches the reader to
/// ignore red, and then a real error is invisible. So the negations below are checked first, and
/// anything unrecognised stays Info.
/// </para>
/// </summary>
public static class LogSeverities
{
    /// <summary>
    /// A count of zero, or a denial. Checked before anything else, because the words that name a
    /// problem also appear in every line that says there is none.
    /// <para>
    /// "Build succeeded. 0 Warning(s) 0 Error(s)" ends a successful MSBuild run; "no errors found"
    /// ends a successful check; "error-free" is a compliment. All three carry the word.
    /// </para>
    /// </summary>
    private static readonly string[] Denials =
    {
        "0 error", "0 errors", "0 warning", "0 warnings",
        "no error", "no errors", "no warning", "no warnings",
        "without error", "without errors", "error-free", "errors: 0", "warnings: 0",
        "errors=0", "warnings=0", "0 problems", "0 vulnerabilities", "found 0 ",
        "0 failed", "0 failures", "failed: 0", "failures: 0",
    };

    /// <summary>Nothing after this line is going to work.</summary>
    private static readonly string[] Fatals =
    {
        "fatal:", "fatal error", "[fatal]", "level=fatal", "panic:", "unhandled exception",
        "segmentation fault", "core dumped", "out of memory", "killed process",
    };

    /// <summary>Something did not work.</summary>
    private static readonly string[] Errors =
    {
        "error", "err!", "[err]", "errno", "exception", "traceback (most recent call last)",
        "level=error", "failed", "failure", "no such file", "permission denied",
        "command not found", "connection refused", "undefined reference",
        "econnrefused", "eaddrinuse", "eacces", "enoent",
    };

    /// <summary>
    /// Worth knowing, and the run carried on.
    /// <para>
    /// The vague ones live here rather than with the errors on purpose. "not found" is a cache
    /// miss as often as a missing file, "invalid" is printed by linters about their own input, and
    /// "unable to" is how a tool announces the fallback it is about to take. Calling those errors
    /// is what makes a log full of red that nobody reads.
    /// </para>
    /// </summary>
    private static readonly string[] Warnings =
    {
        "warn", "[warning]", "level=warn", "deprecat", "obsolete", "skipping", "retrying",
        "fallback", "insecure", "vulnerabilit", "cannot find", "not found", "unable to", "invalid",
    };

    /// <summary>
    /// How bad a line is.
    /// </summary>
    /// <param name="line">The line, as the program printed it.</param>
    /// <param name="fromErrorStream">
    /// Whether it came from standard error. Not treated as an error by itself: plenty of tools -
    /// docker, git, npm, every progress bar - write their ordinary output there, and counting all of
    /// it as errors is how "17 error lines" appears above a run that worked perfectly. It lifts an
    /// unrecognised line to <see cref="LogSeverity.Warn"/> and no further, which is what the stream
    /// actually tells you: not the normal output, and not necessarily a problem.
    /// </param>
    public static LogSeverity Of(string? line, bool fromErrorStream = false)
    {
        // Runs of whitespace collapsed first: a test runner ends with "Failed:     0" in a lined-up
        // column, and a denial that only matches one space would have read that as a failure.
        var text = string.Join(' ',
            (line ?? "").ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (text.Length == 0) return LogSeverity.Info;

        // A line that says there are none is not one.
        var denied = Denials.Any(d => text.Contains(d, StringComparison.Ordinal));

        if (Fatals.Any(f => text.Contains(f, StringComparison.Ordinal))) return LogSeverity.Fatal;

        if (!denied && Errors.Any(e => Mentions(text, e))) return LogSeverity.Error;
        if (!denied && Warnings.Any(w => text.Contains(w, StringComparison.Ordinal))) return LogSeverity.Warn;

        return fromErrorStream ? LogSeverity.Warn : LogSeverity.Info;
    }

    /// <summary>
    /// Whether the line mentions that word, rather than merely containing those letters.
    /// <para>
    /// "error" is inside "errorless" and inside a path like <c>src/ErrorPage.tsx</c>, which a build
    /// prints for every file it compiles. So a word has to end at a boundary - anything that is not
    /// a letter - and the words that are already punctuation ("err!", "[err]") are matched as they
    /// are.
    /// </para>
    /// </summary>
    private static bool Mentions(string text, string word)
    {
        if (!char.IsAsciiLetter(word[^1])) return text.Contains(word, StringComparison.Ordinal);

        var at = 0;

        while ((at = text.IndexOf(word, at, StringComparison.Ordinal)) >= 0)
        {
            var after = at + word.Length;

            if (after >= text.Length || !char.IsAsciiLetter(text[after])) return true;

            at = after;
        }

        return false;
    }

    /// <summary>The name this severity travels under, in JSON and in a CSS class.</summary>
    public static string Name(this LogSeverity severity) => severity switch
    {
        LogSeverity.Fatal => "fatal",
        LogSeverity.Error => "error",
        LogSeverity.Warn => "warn",
        _ => "info",
    };
}
