namespace QuickRun.Core.Config;

/// <summary>
/// What the windows QuickRun opens by itself should do, kept between runs.
/// <para>
/// A file rather than a registry key or a database: it is one line, a person may want to read it,
/// and everything else QuickRun remembers about a machine already lives beside it in the workspace
/// root.
/// </para>
/// </summary>
public sealed class WindowPreferences(string root)
{
    public const string FileName = "windows.txt";

    private const string OnTopKey = "always-on-top";

    private static readonly string Header = string.Join('\n',
        "# How the windows QuickRun opens by itself behave.",
        "#",
        "# always-on-top = yes   a confirmation window stays above everything until it is closed",
        "# always-on-top = no    it comes to the front when it opens, and behaves normally after",
        "#",
        "# Either way the window is raised when it appears: a plan waiting behind three other",
        "# windows, announced by a blinking taskbar button, is a plan nobody sees.",
        "");

    public string Path => System.IO.Path.Combine(root, FileName);

    /// <summary>Whether a window QuickRun opens should stay above the other windows.</summary>
    public bool AlwaysOnTop
    {
        get
        {
            try
            {
                if (!File.Exists(Path)) return false;

                foreach (var line in File.ReadAllLines(Path))
                {
                    var text = line.Trim();
                    if (text.Length == 0 || text.StartsWith('#')) continue;

                    var parts = text.Split('=', 2);
                    if (parts.Length != 2) continue;
                    if (!parts[0].Trim().Equals(OnTopKey, StringComparison.OrdinalIgnoreCase)) continue;

                    return Yes(parts[1].Trim());
                }
            }
            catch (IOException)
            {
                // Unreadable is the same answer as absent: the default, rather than a crash on the
                // way to opening a window.
            }

            return false;
        }
    }

    public void SetAlwaysOnTop(bool on)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path, $"{Header}\n{OnTopKey} = {(on ? "yes" : "no")}\n");
    }

    private static bool Yes(string value) =>
        value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value == "1"
        || value.Equals("on", StringComparison.OrdinalIgnoreCase);
}
