using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using Avalonia.Threading;
using QuickRun.App.Commands;
using QuickRun.App.Daemon;
using QuickRun.Core.Git;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Ui;

/// <summary>
/// Starting a repository without the browser extension: a repository, an optional ref, an optional
/// token for a private one.
/// <para>
/// The confirmation gate applies here exactly as it does everywhere else. Preparing checks the
/// repository out and builds the plan; the commands are shown, and only a second, explicit click
/// runs them.
/// </para>
/// </summary>
public sealed class RunPage : UserControl
{
    private readonly RunRegistry _runs;
    private readonly WorkspaceStore _store;

    private readonly TextBox _repo = new()
    {
        PlaceholderText = "owner/repo, a git URL, or a folder on this machine",
        MinWidth = 320,
    };

    private readonly TextBox _reference = new() { PlaceholderText = "branch, tag or commit (optional)", Width = 220 };
    private readonly TextBox _token = new() { PlaceholderText = "token for a private repository (optional)", PasswordChar = '•', Width = 260 };

    /// <summary>What the field was read as, so the two possibilities are visible before running.</summary>
    private readonly TextBlock _kind = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };

    /// <summary>Only for a folder: a checkout has nothing to leave untouched.</summary>
    private readonly CheckBox _copy = new()
    {
        Content = "Run a copy, leaving my folder untouched",
        IsVisible = false,
    };

    /// <summary>A branch and a token are about a repository, and nothing to do with a folder.</summary>
    private readonly StackPanel _repoOnly = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
    };

    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly StackPanel _plan = new() { Spacing = 6, IsVisible = false };
    private readonly Button _prepare;
    private readonly Button _confirm;

    private string? _preparedId;

    public RunPage(RunRegistry runs, WorkspaceStore store)
    {
        _runs = runs;
        _store = store;

        _prepare = MakeButton("Prepare", PrepareAsync);
        _confirm = MakeButton("Run these commands", ConfirmAsync);
        _confirm.IsVisible = false;

        Content = Build();
    }

    private Control Build()
    {
        var form = new StackPanel { Spacing = 10 };

        form.Add(Muted(
            "Start any repository without the browser extension. Nothing runs until you have seen "
            + "the commands."));

        // One field for the one question, with the picker beside it - the same shape as the page in
        // the browser, so the two do not have to be learned separately.
        var browse = MakeButton("Browse…", BrowseAsync);

        form.Add(Labelled("Repository or folder", new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _repo, browse },
        }));

        form.Add(_kind);

        _repoOnly.Children.Add(Labelled("Ref", _reference));
        _repoOnly.Children.Add(Labelled("Token", _token));
        form.Add(_repoOnly);
        form.Add(_copy);

        form.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _prepare, _confirm },
        });

        form.Add(_status);
        form.Add(_plan);

        // Enter in the repository field is the obvious shortcut.
        _repo.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) _ = PrepareAsync();
        };

        _repo.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ReadTarget();
        };

        ReadTarget();

        return new ScrollViewer { Content = form, Padding = new Thickness(18) };
    }

    /// <summary>
    /// Whether what is in the field is a folder on this machine.
    /// <para>
    /// Asked of the file system rather than guessed from the shape of the string, which is what the
    /// page in the browser has to do - this window can simply look. Fully qualified only, so that a
    /// directory that happens to sit beside the working directory cannot turn "acme/app" into
    /// something local.
    /// </para>
    /// </summary>
    internal static bool IsFolder(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        try
        {
            return Path.IsPathFullyQualified(text) && Directory.Exists(text);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <param name="Folder">Whether the field names a folder on this machine.</param>
    /// <param name="ShowCopy">The copy switch, which only a folder has any use for.</param>
    /// <param name="ShowRepoFields">The branch and token, which only a repository has any use for.</param>
    /// <param name="Explanation">What the field was read as, in words.</param>
    internal readonly record struct TargetView(
        bool Folder, bool ShowCopy, bool ShowRepoFields, string Explanation);

    /// <summary>
    /// What the form should look like for what is in the field.
    /// <para>
    /// A value rather than a set of assignments, so that every case can be checked without a window:
    /// the controls follow from it, and there is nothing in between to get wrong.
    /// </para>
    /// </summary>
    internal static TargetView Read(string? value)
    {
        var text = value?.Trim() ?? "";
        var folder = IsFolder(text);

        // A path written like one that is not there is worth saying out loud: otherwise QuickRun
        // tries to check it out as a repository and fails for a stranger reason.
        var missing = !folder && text.Length > 0 && LooksLikeAPath(text);

        var explanation = text.Length == 0
            ? "A repository is checked out. A folder on this machine is run where it lies."
            : folder
                ? "a folder on this machine - it runs where it lies, nothing is checked out"
                : missing
                    ? "that folder is not there - as a repository this would be checked out"
                    : "a repository - QuickRun checks it out and reuses that checkout next time";

        return new TargetView(folder, folder, !folder, explanation);
    }

    /// <summary>
    /// Whether a string is written like a path, whether or not anything is there.
    /// <para>
    /// Deliberately not "contains a directory separator": on Linux and macOS that separator is the
    /// slash, so every owner/repo in existence would qualify - which is exactly what this said about
    /// "acme/app" on both of them. What marks a path is being anchored: a root, a drive, a home, or
    /// a relative step said out loud.
    /// </para>
    /// </summary>
    private static bool LooksLikeAPath(string text)
    {
        // A URL is not a path, however many slashes it has.
        if (text.Contains("://", StringComparison.Ordinal)) return false;

        // A Windows path, typed anywhere.
        if (text.Contains('\\')) return true;

        if (text.StartsWith('~')) return true;
        if (text.StartsWith("./", StringComparison.Ordinal)) return true;
        if (text.StartsWith("../", StringComparison.Ordinal)) return true;
        if (text is "." or "..") return true;

        try { return Path.IsPathFullyQualified(text); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Says what the field was read as, and shows what belongs with it.</summary>
    private void ReadTarget()
    {
        var view = Read(_repo.Text);

        _copy.IsVisible = view.ShowCopy;
        _repoOnly.IsVisible = view.ShowRepoFields;
        _kind.Text = view.Explanation;
    }

    /// <summary>The system's folder picker, which this window can open directly.</summary>
    private async Task BrowseAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to run",
            AllowMultiple = false,
        });

        if (folders.Count == 0) return;
        if (folders[0].TryGetLocalPath() is not { } path) return;

        _repo.Text = path;
        ReadTarget();
    }

    private async Task PrepareAsync()
    {
        var target = _repo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            Report("Enter a repository or a folder first.");
            return;
        }

        var folder = Read(target).Folder;

        Reset();
        _prepare.IsEnabled = false;
        Report(folder
            ? (_copy.IsChecked == true ? $"Copying {target}…" : $"Reading {target}…")
            : $"Checking out {target}…");

        var args = new RunArgs(
            folder ? "" : target,
            string.IsNullOrWhiteSpace(_reference.Text) || folder ? null : _reference.Text!.Trim(),
            PullRequest: null,
            Subdir: null,
            Inputs: Array.Empty<string>(),
            Token: string.IsNullOrWhiteSpace(_token.Text) || folder ? null : _token.Text,
            Fresh: false,
            Yes: true,
            NoOpen: true,
            ConfigPath: null,
            LocalPath: folder ? target : null,
            Copy: folder && _copy.IsChecked == true);

        var (summary, error) = await _runs.PrepareAsync(args);

        _prepare.IsEnabled = true;

        if (error is not null || summary is null)
        {
            Report(error ?? "could not prepare the run");
            return;
        }

        _preparedId = summary.Id;
        ShowPlan(summary);
    }

    private void ShowPlan(RunSummary summary)
    {
        Report($"{summary.DisplayName} — {summary.Ref}"
               + (summary.Commit is null ? "" : $" ({summary.Commit[..Math.Min(7, summary.Commit.Length)]})"));

        _plan.Children.Clear();
        _plan.Children.Add(new TextBlock
        {
            Text = "These commands will run on your machine",
            FontWeight = FontWeight.SemiBold,
        });

        foreach (var command in summary.Commands)
            _plan.Children.Add(new TextBlock
            {
                Text = $"  {command.Phase}   {command.Command}"
                       + (string.IsNullOrEmpty(command.Cwd) ? "" : $"   (in {command.Cwd})"),
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                TextWrapping = TextWrapping.Wrap,
            });

        _plan.Children.Add(Muted(
            "QuickRun executes these with your privileges, outside any sandbox. Only continue if "
            + "you would be willing to run them by hand."));

        _plan.IsVisible = true;
        _confirm.IsVisible = true;
    }

    private Task ConfirmAsync()
    {
        if (_preparedId is null) return Task.CompletedTask;

        if (!_runs.Confirm(_preparedId))
        {
            Report("that run has already been started");
            return Task.CompletedTask;
        }

        Report("Started. Watch it on the Runs tab.");
        Reset();
        return Task.CompletedTask;
    }

    private void Reset()
    {
        _preparedId = null;
        _plan.Children.Clear();
        _plan.IsVisible = false;
        _confirm.IsVisible = false;
    }

    private void Report(string message) =>
        Dispatcher.UIThread.Post(() => _status.Text = message);

    // ---- small builders -----------------------------------------------------

    private static Control Labelled(string label, Control field) => new StackPanel
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Opacity = 0.7, FontSize = 12 },
            field,
        },
    };

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Button MakeButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 6) };
        button.Click += (_, _) => _ = action();
        return button;
    }
}

internal static class PanelExtensions
{
    /// <summary>Reads better than Children.Add when a method builds a form top to bottom.</summary>
    public static void Add(this Panel panel, Control child) => panel.Children.Add(child);
}
