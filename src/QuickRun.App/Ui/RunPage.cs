using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
        PlaceholderText = "owner/repo, or a full repository URL",
        MinWidth = 320,
    };

    private readonly TextBox _reference = new() { PlaceholderText = "branch, tag or commit (optional)", Width = 220 };
    private readonly TextBox _token = new() { PlaceholderText = "token for a private repository (optional)", PasswordChar = '•', Width = 260 };

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

        form.Add(Labelled("Repository", _repo));
        form.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { Labelled("Ref", _reference), Labelled("Token", _token) },
        });

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

        return new ScrollViewer { Content = form, Padding = new Thickness(18) };
    }

    private async Task PrepareAsync()
    {
        var repo = _repo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(repo))
        {
            Report("Enter a repository first.");
            return;
        }

        Reset();
        _prepare.IsEnabled = false;
        Report($"Checking out {repo}…");

        var args = new RunArgs(
            repo,
            string.IsNullOrWhiteSpace(_reference.Text) ? null : _reference.Text!.Trim(),
            PullRequest: null,
            Subdir: null,
            Inputs: Array.Empty<string>(),
            Token: string.IsNullOrWhiteSpace(_token.Text) ? null : _token.Text,
            Fresh: false,
            Yes: true,
            NoOpen: true,
            ConfigPath: null);

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
