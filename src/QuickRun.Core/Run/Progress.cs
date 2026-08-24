using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickRun.Core.Run;

public enum RunPhase
{
    Checkout,
    Setup,
    Tasks,
}

/// <summary>
/// What a run reports about its own progress. <see cref="Percent"/> is the weighted total across
/// all phases; <see cref="Detail"/> is the honest text behind it.
/// </summary>
public sealed record RunProgress(RunPhase Phase, int Percent, string Detail);

/// <summary>
/// Turns per-phase progress into one number. Nothing here is estimated or interpolated from
/// previous runs - a bar that sits at 90% is worse than no bar.
/// </summary>
public static class ProgressModel
{
    public const int CheckoutWeight = 30;
    public const int SetupWeight = 40;
    public const int TasksWeight = 30;

    /// <param name="phasePercent">How far this phase has got, 0-100.</param>
    public static int Total(RunPhase phase, int phasePercent)
    {
        var clamped = Math.Clamp(phasePercent, 0, 100);
        var before = phase switch
        {
            RunPhase.Checkout => 0,
            RunPhase.Setup => CheckoutWeight,
            _ => CheckoutWeight + SetupWeight,
        };
        var weight = phase switch
        {
            RunPhase.Checkout => CheckoutWeight,
            RunPhase.Setup => SetupWeight,
            _ => TasksWeight,
        };

        return before + weight * clamped / 100;
    }

    /// <summary>Step <paramref name="done"/> of <paramref name="total"/> as a phase percentage.</summary>
    public static int StepPercent(int done, int total) =>
        total <= 0 ? 100 : Math.Clamp(100 * done / total, 0, 100);
}

/// <summary>
/// Parses the counters git writes to stderr under <c>--progress</c>. These are the only genuinely
/// measured numbers a run has, so they are passed through rather than smoothed.
/// </summary>
public static partial class GitProgress
{
    /// <summary>
    /// Where each git stage sits inside the checkout phase. The boundaries reflect which stage
    /// dominates a shallow clone, and keep the reported number monotonic.
    /// </summary>
    private static readonly (string Stage, int From, int To)[] Stages =
    {
        ("counting objects", 0, 10),
        ("compressing objects", 10, 20),
        ("receiving objects", 20, 90),
        ("resolving deltas", 90, 100),
        ("updating files", 90, 100),
    };

    /// <summary>The checkout-phase percentage this line implies, or null if it carries no counter.</summary>
    public static (int Percent, string Detail)? Parse(string line)
    {
        var match = CounterPattern().Match(line ?? "");
        if (!match.Success) return null;

        var stage = match.Groups["stage"].Value.Trim().ToLowerInvariant();
        var within = int.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture);

        var band = Stages.FirstOrDefault(s => stage.EndsWith(s.Stage, StringComparison.Ordinal));
        if (band.Stage is null) return null;

        var percent = band.From + (band.To - band.From) * Math.Clamp(within, 0, 100) / 100;
        return (percent, $"{match.Groups["stage"].Value.Trim()}: {within}%");
    }

    // "Receiving objects:  47% (470/1000)", "remote: Compressing objects:  50% (5/10)"
    [GeneratedRegex(@"(?<stage>[A-Za-z][A-Za-z ]+):\s+(?<percent>\d{1,3})%")]
    private static partial Regex CounterPattern();
}
