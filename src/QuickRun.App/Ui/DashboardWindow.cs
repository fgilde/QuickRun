using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using QuickRun.App.Daemon;
using QuickRun.Core;
using QuickRun.Core.Run;
using QuickRun.Core.Update;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Ui;

/// <summary>
/// The desktop window. Built in code rather than XAML, and drawn natively rather than in a WebView:
/// a WebView would mean shipping a browser engine (WebView2 on Windows, CEF or WKWebView elsewhere,
/// each with its own native dependencies) to render a handful of lists. Avalonia is already in the
/// binary for the tray icon, so this costs almost nothing extra.
/// <para>
/// It talks to the registry and the store directly. Same process, so no HTTP, no token, and no
/// cross-site concerns to defend against.
/// </para>
/// </summary>
public sealed class DashboardWindow : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly RunRegistry _runs;
    private readonly WorkspaceStore _store;
    private readonly string _listenerUrl;

    private readonly StackPanel _runList = Rows();
    private readonly StackPanel _workspaceList = Rows();
    private readonly TextBlock _updateState = Muted("checking…");
    private readonly DispatcherTimer _timer;

    public DashboardWindow(RunRegistry runs, WorkspaceStore store, string listenerUrl)
    {
        _runs = runs;
        _store = store;
        _listenerUrl = listenerUrl;

        Title = $"QuickRun {BuildInfo.Version}";
        Width = 900;
        Height = 640;
        MinWidth = 620;
        MinHeight = 420;
        Icon = LoadIcon();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildLayout();

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Opened += (_, _) => Refresh();
        Closed += (_, _) => _timer.Stop();

        _ = CheckUpdateAsync();
    }

    // ---- layout -------------------------------------------------------------

    private Control BuildLayout()
    {
        var tabs = new TabControl
        {
            Padding = new Thickness(0),
            Items =
            {
                Tab("Run a repository", new RunPage(_runs, _store)),
                Tab("Runs", Scroll(_runList)),
                Tab("Workspaces", Scroll(WorkspacesPage())),
                Tab("Browser extension", Scroll(ExtensionPage())),
                Tab("About", Scroll(AboutPage())),
            },
        };

        return new DockPanel
        {
            Children =
            {
                Header().With(d => DockPanel.SetDock(d, Dock.Top)),
                tabs,
            },
        };
    }

    private Control Header()
    {
        var logo = new Image { Width = 28, Height = 28, Source = LoadBitmap() };

        return new Border
        {
            Padding = new Thickness(16, 12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = Brushes.Gray,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    logo,
                    new TextBlock
                    {
                        Text = "QuickRun",
                        FontSize = 17,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    Muted(BuildInfo.Version).With(t => t.VerticalAlignment = VerticalAlignment.Center),
                    new Panel { Width = 16 },
                    Link(_listenerUrl).With(b => b.VerticalAlignment = VerticalAlignment.Center),
                },
            },
        };
    }

    /// <summary>A button that reads as a link, for the listener URL and the documentation.</summary>
    private static Button Link(string url)
    {
        var button = new Button
        {
            Content = url,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.CornflowerBlue,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        button.Click += (_, _) => Open(url);
        return button;
    }

    private Control WorkspacesPage()
    {
        var page = Rows();

        page.Children.Add(Muted(
            "Checked-out repositories. A second run of the same repository and ref reuses its "
            + "workspace, so starting again takes seconds."));

        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                Muted(_store.Root).With(t => t.VerticalAlignment = VerticalAlignment.Center),
                Button("Remove all", RemoveAllWorkspaces),
            },
        });

        page.Children.Add(_workspaceList);
        return page;
    }

    private Control ExtensionPage()
    {
        var page = Rows();

        page.Children.Add(Muted(
            "Only a browser extension may start a run. QuickRun checks the request's origin, which "
            + "a web page cannot forge, so no site can drive it - and there is nothing to set up."));

        page.Children.Add(Heading("How it works"));
        page.Children.Add(Muted(
            "1. Install the extension from the download page. Until the store listings are live it "
            + "is loaded unpacked; that page has the steps."));
        page.Children.Add(Muted(
            "2. That is all - there is nothing to pair."));
        page.Children.Add(Muted(
            "3. Open any repository on GitHub. A \"Run this\" button appears next to the branch "
            + "dropdown, in pull request headers, and on every row of the branch list."));
        page.Children.Add(Muted(
            "4. Clicking it does not start anything yet: QuickRun checks the repository out, then "
            + "the extension shows you the exact commands and waits for your confirmation."));

        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Button("Download the extension", () => Open("https://fgilde.github.io/QuickRun/download#the-browser-extension")),
                Button("Documentation", () => Open("https://fgilde.github.io/QuickRun/extension")),
            },
        });

        return page;
    }

    private Control AboutPage()
    {
        var page = Rows();

        page.Children.Add(Field("Version", BuildInfo.Version));
        page.Children.Add(Field("Installed as", InstallSources.DetectCurrent(_store.Root).ToString().ToLowerInvariant()));
        page.Children.Add(Field("Listening on", _listenerUrl));

        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Updates", Width = 120, VerticalAlignment = VerticalAlignment.Center },
                _updateState.With(t => t.VerticalAlignment = VerticalAlignment.Center),
                Button("Check now", () => _ = CheckUpdateAsync()),
            },
        });

        page.Children.Add(Muted(
            "QuickRun runs commands from the repositories you point it at, with your privileges and "
            + "outside any sandbox. Every run shows the exact commands first and waits for your "
            + "confirmation."));

        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Button("Security model", () => Open("https://fgilde.github.io/QuickRun/security")),
                Button("Open in browser", () => Open(_listenerUrl)),
            },
        });

        return page;
    }

    // ---- data ---------------------------------------------------------------

    private void Refresh()
    {
        RenderRuns();
        RenderWorkspaces();

    }

    private void RenderRuns()
    {
        _runList.Children.Clear();
        var all = _runs.All();

        if (all.Count == 0)
        {
            _runList.Children.Add(Muted(
                "Nothing has run yet. Start one from the browser extension, or with "
                + "quickrun run owner/repo."));
            return;
        }

        foreach (var run in all)
        {
            var card = Rows();
            card.Children.Add(new TextBlock { Text = run.DisplayName, FontWeight = FontWeight.SemiBold });
            card.Children.Add(Muted($"{run.Repo} @ {run.Ref}"
                                    + (run.Commit is null ? "" : $" ({run.Commit[..Math.Min(7, run.Commit.Length)]})")));
            card.Children.Add(Muted(State(run.State)));

            var percent = run.Progress?.Percent ?? (run.State == RunState.Succeeded ? 100 : 0);
            card.Children.Add(new ProgressBar { Value = percent, Maximum = 100, Height = 6 });
            card.Children.Add(Muted(run.Progress?.Detail ?? ""));

            if (run.Error is { } error) card.Children.Add(Muted(error));

            if (run.State == RunState.Running)
            {
                var id = run.Id;
                card.Children.Add(Button("Stop", () => { _runs.Stop(id); Refresh(); }));
            }

            _runList.Children.Add(Card(card));
        }
    }

    private static string State(RunState state) => state switch
    {
        RunState.AwaitingConfirmation => "awaiting confirmation in the browser",
        RunState.Running => "running",
        RunState.Succeeded => "succeeded",
        RunState.Failed => "failed",
        _ => "cancelled",
    };

    private void RenderWorkspaces()
    {
        _workspaceList.Children.Clear();
        var all = _store.List();

        if (all.Count == 0)
        {
            _workspaceList.Children.Add(Muted("No workspaces yet."));
            return;
        }

        foreach (var workspace in all)
        {
            var id = workspace.Id;
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{workspace.Repo} @ {workspace.Ref}",
                        Width = 420,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    Muted(Output.Size(workspace.Bytes)).With(t =>
                    {
                        t.Width = 80;
                        t.VerticalAlignment = VerticalAlignment.Center;
                    }),
                    Muted(workspace.LastUsed.LocalDateTime.ToString("yyyy-MM-dd HH:mm")).With(t =>
                        t.VerticalAlignment = VerticalAlignment.Center),
                    Button("Remove", () =>
                    {
                        try { _store.Remove(id); } catch (ArgumentException) { }
                        Refresh();
                    }),
                },
            };

            _workspaceList.Children.Add(Card(row));
        }
    }

    private void RemoveAllWorkspaces()
    {
        _store.RemoveAll();
        Refresh();
    }

    private async Task CheckUpdateAsync()
    {
        var source = InstallSources.DetectCurrent(_store.Root);
        var status = await new UpdateChecker().CheckAsync(BuildInfo.Version, source);

        await Dispatcher.UIThread.InvokeAsync(() =>
            _updateState.Text = status.Error is null ? status.Advice : "could not check");
    }

    private static void Open(string url) => Commands.UiCommand.Launch(url);

    // ---- small builders -----------------------------------------------------

    private static TabItem Tab(string header, Control content) => new() { Header = header, Content = content };

    private static ScrollViewer Scroll(Control content) =>
        new() { Content = content, Padding = new Thickness(18) };

    private static StackPanel Rows() => new() { Spacing = 8 };

    private static Border Card(Control content) => new()
    {
        Padding = new Thickness(14),
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        BorderBrush = Brushes.Gray,
        Child = content,
    };

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 10, 0, 0),
    };

    private static Control Field(string label, string value) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Children =
        {
            new TextBlock { Text = label, Width = 120 },
            new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap },
        },
    };

    private static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 5) };
        button.Click += (_, _) => action();
        return button;
    }

    private static Bitmap? LoadBitmap()
    {
        try
        {
            using var stream = typeof(DashboardWindow).Assembly
                .GetManifestResourceStream("QuickRun.App.Daemon.icon.png");
            return stream is null ? null : new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static WindowIcon? LoadIcon()
    {
        var bitmap = LoadBitmap();
        return bitmap is null ? null : new WindowIcon(bitmap);
    }
}

internal static class ControlExtensions
{
    /// <summary>Lets a control be configured inline while still being used as an initialiser value.</summary>
    public static T With<T>(this T control, Action<T> configure)
    {
        configure(control);
        return control;
    }
}
