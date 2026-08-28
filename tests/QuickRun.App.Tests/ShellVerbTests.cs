using QuickRun.App.Daemon;

namespace QuickRun.App.Tests;

/// <summary>
/// "Run with QuickRun" in the file manager, checked as content rather than by installing it.
/// <para>
/// What these entries say is the whole of the feature - a wrong placeholder means the click passes
/// the wrong thing, or nothing - and a test must not write to the registry or the home directory of
/// whoever is running it. So the entries are built, and what would be written is what is checked.
/// </para>
/// </summary>
public class ShellVerbTests
{
    private const string Exe = @"C:\Program Files\QuickRun\quickrun.exe";

    [Fact]
    public void Explorer_gets_an_entry_for_a_folder_its_background_and_a_config()
    {
        var verbs = SystemIntegration.ExplorerVerbs(Exe);

        Assert.Equal(4, verbs.Count);
        Assert.All(verbs, verb => Assert.Equal("Run with QuickRun", verb.Label));

        // Under HKCU, so no administrator rights - and SystemFileAssociations, which adds a verb
        // beside whatever already opens .yml instead of taking the file type over.
        Assert.All(verbs, verb => Assert.StartsWith(@"Software\Classes\", verb.Key));
        Assert.Contains(verbs, verb => verb.Key.Contains(@"SystemFileAssociations\.yml"));
        Assert.Contains(verbs, verb => verb.Key.Contains(@"SystemFileAssociations\.yaml"));
    }

    /// <summary>
    /// %V on a directory and %1 on a file. The background of a folder has no %1 at all, and a verb
    /// that used it there would be handed an empty string.
    /// </summary>
    [Theory]
    [InlineData(@"Software\Classes\Directory\shell\QuickRun", "%V")]
    [InlineData(@"Software\Classes\Directory\Background\shell\QuickRun", "%V")]
    [InlineData(@"Software\Classes\SystemFileAssociations\.yml\shell\QuickRun", "%1")]
    public void The_command_passes_what_the_shell_offers_in_that_place(string key, string placeholder)
    {
        var verb = Assert.Single(SystemIntegration.ExplorerVerbs(Exe), v => v.Key == key);

        Assert.Equal($"\"{Exe}\" open \"{placeholder}\"", verb.Command);
    }

    /// <summary>
    /// The shell cannot filter a verb by file name, so it is asked to with a query. A folder needs
    /// no filter, and giving it one would hide the entry.
    /// </summary>
    [Fact]
    public void Only_the_file_entries_are_narrowed_to_a_config()
    {
        foreach (var verb in SystemIntegration.ExplorerVerbs(Exe))
        {
            if (verb.Key.Contains(@"\Directory")) Assert.Null(verb.AppliesTo);
            else Assert.Contains("quickrun.yml", verb.AppliesTo);
        }
    }

    [Fact]
    public void Every_supported_file_manager_gets_a_file()
    {
        var entries = SystemIntegration.FileManagerEntries("/usr/local/bin/quickrun");

        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Path.Contains("kio/servicemenus") || e.Path.Contains(@"kio\servicemenus"));
        Assert.Contains(entries, e => e.Path.EndsWith(".nemo_action"));
        Assert.Contains(entries, e => e.Path.EndsWith("Run with QuickRun"));
    }

    [Fact]
    public void The_kde_entry_offers_itself_on_folders_and_on_yaml()
    {
        var kde = Assert.Single(SystemIntegration.FileManagerEntries("/usr/local/bin/quickrun"),
            e => e.Path.Contains("servicemenus"));

        Assert.Contains("MimeType=inode/directory;", kde.Content);
        Assert.Contains("text/x-yaml;", kde.Content);
        Assert.Contains("Exec=/usr/local/bin/quickrun open %f", kde.Content);
        Assert.Contains("Name=Run with QuickRun", kde.Content);
    }

    [Fact]
    public void The_nemo_action_names_the_kinds_it_appears_on()
    {
        var nemo = Assert.Single(SystemIntegration.FileManagerEntries("/usr/local/bin/quickrun"),
            e => e.Path.EndsWith(".nemo_action"));

        Assert.Contains("Extensions=dir;yml;yaml;", nemo.Content);
        Assert.Contains("Exec=/usr/local/bin/quickrun open %F", nemo.Content);
    }

    /// <summary>
    /// Nautilus runs a script and passes the selection in the environment, so the script has to read
    /// it - and fall back to the folder it was invoked in, which is what a click on the background
    /// means.
    /// </summary>
    [Fact]
    public void The_nautilus_script_reads_the_selection_and_falls_back_to_the_folder()
    {
        var script = Assert.Single(SystemIntegration.FileManagerEntries("/usr/local/bin/quickrun"),
            e => e.Path.EndsWith("Run with QuickRun"));

        Assert.StartsWith("#!/bin/sh", script.Content);
        Assert.Contains("NAUTILUS_SCRIPT_SELECTED_FILE_PATHS", script.Content);
        Assert.Contains("target=$(pwd)", script.Content);
        Assert.Contains("open \"$target\"", script.Content);
    }

    /// <summary>A path with a space in it is one word to the shell, or the script runs the wrong thing.</summary>
    [Fact]
    public void The_nautilus_script_quotes_the_binary()
    {
        var script = Assert.Single(SystemIntegration.FileManagerEntries("/home/me/my tools/quickrun"),
            e => e.Path.EndsWith("Run with QuickRun"));

        Assert.Contains("exec '/home/me/my tools/quickrun' open", script.Content);
    }
}
