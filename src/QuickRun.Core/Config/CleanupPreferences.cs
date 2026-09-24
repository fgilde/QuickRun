namespace QuickRun.Core.Config;

/// <summary>
/// How long a workspace QuickRun checked out is kept after its last run.
/// <para>
/// A file, like the other preferences, and for the same reason: it is one line, somebody may want
/// to read it, and it belongs beside the workspaces it is about.
/// </para>
/// <para>
/// Off by default would leave the disk filling up quietly, and a short default would take somebody
/// by surprise - a checkout is not always only source code. Configs in QuickRun's own collection
/// put data inside it: 9Router keeps its database and its keys in the workspace, and a month-old
/// one is still somebody's instance. So the default is a month, and the number is theirs to choose.
/// </para>
/// </summary>
public sealed class CleanupPreferences(string root)
{
    public const string FileName = "cleanup.txt";

    private const string DaysKey = "remove-after-days";

    /// <summary>Long enough that a repository run every few weeks is still there.</summary>
    public const int DefaultDays = 30;

    /// <summary>What this may never be: a workspace removed while it is being used.</summary>
    public const int MinimumDays = 1;

    private static readonly string Header = string.Join('\n',
        "# When QuickRun removes a workspace it checked out, counted from its last run.",
        "#",
        "# remove-after-days = 30   the default",
        "# remove-after-days = 7    keep a week",
        "# remove-after-days = 0    never remove anything automatically",
        "#",
        "# Only checkouts under runs/ are removed, and only when nothing is running in them. A",
        "# folder on this machine that QuickRun ran where it lies is never touched.",
        "#",
        "# Worth knowing before choosing a small number: a checkout is not always only source code.",
        "# A config may keep the application's data inside it - a database, its keys, what you set up",
        "# in it - and removing the workspace removes that too.",
        "");

    public string Path => System.IO.Path.Combine(root, FileName);

    /// <summary>
    /// After how many days an unused workspace goes, or null when nothing should be removed.
    /// </summary>
    public int? RemoveAfterDays
    {
        get
        {
            try
            {
                if (!File.Exists(Path)) return DefaultDays;

                foreach (var line in File.ReadAllLines(Path))
                {
                    var text = line.Trim();
                    if (text.Length == 0 || text.StartsWith('#')) continue;

                    var parts = text.Split('=', 2);
                    if (parts.Length != 2) continue;
                    if (!parts[0].Trim().Equals(DaysKey, StringComparison.OrdinalIgnoreCase)) continue;

                    if (!int.TryParse(parts[1].Trim(), out var days)) return DefaultDays;

                    // 0 - and anything below it, which is somebody meaning the same thing - is off.
                    return days < MinimumDays ? null : days;
                }
            }
            catch (IOException)
            {
                // Unreadable is the same answer as absent. Housekeeping is not worth a crash.
            }

            return DefaultDays;
        }
    }

    public void SetRemoveAfterDays(int? days)
    {
        Directory.CreateDirectory(root);

        var value = days is { } chosen && chosen >= MinimumDays ? chosen : 0;
        File.WriteAllText(Path, $"{Header}\n{DaysKey} = {value}\n");
    }
}
