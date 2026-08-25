using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using QuickRun.App.Commands;
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
    private readonly ContentControl _header = new();

    /// <summary>
    /// Whether the native view is what is on screen. With the page in a WebView there is nothing
    /// here to redraw, and redrawing it anyway is what made dragging the window stutter: every tick
    /// rebuilt lists nobody could see, on the thread the WebView needs to answer the mouse.
    /// </summary>
    private bool _native;
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

        // Opens where it was left, at the size it was left at.
        WindowPlacement.Remember(this, store.Root);

        Content = BuildLayout();

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += (_, _) => Refresh();
        if (_native) _timer.Start();

        Opened += (_, _) =>
        {
            if (_native) Refresh();
        };
        Closed += (_, _) => _timer.Stop();

        _ = CheckUpdateAsync();
    }

    // ---- layout -------------------------------------------------------------

    /// <summary>
    /// One interface where the system can render it: the window shows the page served on 127.0.0.1,
    /// which is where features are built, rather than a second implementation of it. Without a
    /// system WebView - Linux without WebKitGTK, a macOS build, an opt-out, a WebView that fails to
    /// start - the native view appears instead, with the same data from the same registry.
    /// </summary>
    private Control BuildLayout()
    {
        var shell = new ContentControl();
        _header.Content = Header();

        // ?shell=window tells the page it is inside this window: it drops nothing, but it does
        // offer a way out into the real browser, which is the one thing a window cannot provide.
        var browser = EmbeddedBrowser.TryCreate($"{_listenerUrl}/?shell=window", reason =>
            Dispatcher.UIThread.Post(() =>
            {
                Output.Warn($"the embedded browser could not start ({reason}) - using the native view");
                _native = true;
                _header.IsVisible = true;
                shell.Content = NativeLayout();
                _timer.Start();
                Refresh();
            }), PageBackground());

        // The page has its own header with the same logo and version in it. Two of them, one above
        // the other, is what made the window look wrong.
        _native = browser is null;
        _header.IsVisible = _native;
        shell.Content = _native ? NativeLayout() : browser;

        return new DockPanel
        {
            Children =
            {
                _header.With(d => DockPanel.SetDock(d, Dock.Top)),
                shell,
            },
        };
    }

    /// <summary>
    /// The page's own background colour, which the window and the engine both paint. The page picks
    /// its colours from the system theme, so this has to follow the same choice or a resize shows a
    /// band of the wrong one.
    /// </summary>
    private uint PageBackground()
    {
        var dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var colour = dark ? Color.FromRgb(0x0d, 0x11, 0x17) : Color.FromRgb(0xff, 0xff, 0xff);

        Background = new SolidColorBrush(colour);
        return colour.ToUInt32();
    }

    /// <summary>Reads the disk when the workspaces are looked at, not every two seconds.</summary>
    private void WhenTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedItem: TabItem { Header: "Workspaces" } })
            _ = RefreshWorkspacesAsync();
    }

    private Control NativeLayout()
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
                Tab("Settings", Scroll(SettingsPage())),
                Tab("About", Scroll(AboutPage())),
            },
        };

        tabs.SelectionChanged += WhenTabChanged;
        return tabs;
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
        // Runs come from the registry in this process: a dictionary read, no I/O.
        RenderRuns();
    }

    /// <summary>
    /// Reads the workspaces from disk on a background thread. Sizes are walked directory by
    /// directory, which is far too slow to do on the thread that has to keep the window responsive.
    /// </summary>
    private async Task RefreshWorkspacesAsync()
    {
        var all = await Task.Run(() => _store.List());
        await Dispatcher.UIThread.InvokeAsync(() => RenderWorkspaces(all));
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

            // Per task, because "running" says nothing about which of five services is up. Each
            // one carries the address it reported, as something clickable.
            foreach (var task in run.Tasks ?? Array.Empty<RunTaskStatus>())
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        Mono(task.Name).With(t => t.MinWidth = 90),
                        Muted(task.State),
                        Muted(task.Pid is { } pid ? $"pid {pid}" : ""),
                    },
                };

                if (task.Url is { } address)
                    row.Children.Add(Link(address, () => UiCommand.Launch(address)));

                card.Children.Add(row);
            }

            // Where it is reachable and where it lives: the two things a reader of a finished run
            // actually wants, and both were previously only findable in the log.
            if (run.Url is { } url && !(run.Tasks ?? Array.Empty<RunTaskStatus>()).Any(t => t.Url is not null))
                card.Children.Add(Link(url, () => UiCommand.Launch(url)));

            if (run.Workspace is { } workspace)
                card.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        Mono(workspace).With(t => t.MaxWidth = 520),
                        Button("Open folder", () => UiCommand.Launch(workspace)),
                    },
                });

            if (run.State == RunState.Stopping)
                card.Children.Add(Muted("stopping..."));

            if (run.State == RunState.Running)
            {
                var id = run.Id;
                var stop = Button("Stop", () => { _runs.Stop(id); Refresh(); });

                // Nothing left to stop: the run is winding down, and a button that does nothing is
                // worse than one that is visibly unavailable.
                stop.IsEnabled = run.LiveTasks > 0;
                card.Children.Add(stop);
            }

            _runList.Children.Add(Card(card));
        }
    }

    private static string State(RunState state) => state switch
    {
        RunState.AwaitingConfirmation => "awaiting confirmation in the browser",
        RunState.AwaitingInput => "waiting for values",
        RunState.Running => "running",
        RunState.Stopping => "stopping...",
        RunState.Succeeded => "succeeded",
        RunState.Failed => "failed",
        _ => "cancelled",
    };

    private void RenderWorkspaces(IReadOnlyList<WorkspaceInfo> all)
    {
        _workspaceList.Children.Clear();

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
                        _ = RefreshWorkspacesAsync();
                    }),
                },
            };

            _workspaceList.Children.Add(Card(row));
        }
    }

    /// <summary>The port the listener is on, which the autostart entry has to name.</summary>
    private int Port()
    {
        try { return new Uri(_listenerUrl).Port; }
        catch (UriFormatException) { return DaemonHost.DefaultPort; }
    }

    /// <summary>
    /// The two things that make QuickRun an installed program rather than a downloaded file: coming
    /// back after a reboot, and being a command in a terminal. The same switches the page has, so
    /// they are reachable on a system with no WebView as well - which is every Linux and macOS one.
    /// </summary>
    private Control SettingsPage()
    {
        var rows = Rows();

        var autostartDetail = Muted("");
        var autostart = new CheckBox { Content = "Start QuickRun when I sign in" };

        var pathDetail = Muted("");
        var path = new CheckBox { Content = "Make quickrun work in a terminal" };

        // Show() sets the boxes, which raises the same event a click does - so a flag says which of
        // the two is happening.
        var showing = false;

        void Show()
        {
            showing = true;

            var state = SystemIntegration.Autostart();
            autostart.IsChecked = state.Enabled;
            autostartDetail.Text = state.Stale
                ? $"{state.Detail} - but it points at a different executable"
                : state.Detail;

            var terminal = SystemIntegration.PathState();
            path.IsChecked = terminal.Available;
            pathDetail.Text = terminal.Detail;

            showing = false;
        }

        void Apply(Func<bool, IntegrationStep> change, CheckBox box, TextBlock detail)
        {
            var step = change(box.IsChecked == true);
            if (!step.Ok) detail.Text = $"{step.What} failed: {step.Detail}";
            Show();
        }

        autostart.IsCheckedChanged += (_, _) =>
        {
            if (showing) return;
            Apply(on => SystemIntegration.SetAutostart(on, Environment.ProcessPath ?? "", Port()),
                autostart, autostartDetail);
        };

        path.IsCheckedChanged += (_, _) =>
        {
            if (showing) return;
            Apply(on => SystemIntegration.SetPath(on, Environment.ProcessPath ?? ""),
                path, pathDetail);
        };

        Show();

        rows.Children.Add(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                autostart,
                Muted("A per-user entry - no administrator rights, nothing system-wide."),
                autostartDetail,
            },
        }));

        rows.Children.Add(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                path,
                Muted(OSKinds.Current == OSKind.Windows
                    ? "Adds this program's directory to your own PATH. Open a new terminal afterwards."
                    : "Links quickrun into a bin directory that is already on the PATH."),
                pathDetail,
            },
        }));

        rows.Children.Add(Card(new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Heading("From a terminal"),
                Mono("quickrun run owner/repo\nquickrun validate\nquickrun detect . --save\n"
                     + "quickrun ls\nquickrun clean --older-than 30d\nquickrun --help"),
                Link("https://fgilde.github.io/QuickRun/cli",
                    () => Open("https://fgilde.github.io/QuickRun/cli")),
                Mono($"running from {Environment.ProcessPath}"),
            },
        }));

        return rows;
    }

    private void RemoveAllWorkspaces()
    {
        _store.RemoveAll();
        _ = RefreshWorkspacesAsync();
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

    private static TextBlock Mono(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Consolas, Menlo, monospace"),
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.85,
    };

    /// <summary>An address, shown as one and openable. Avalonia has no hyperlink control.</summary>
    private static Control Link(string text, Action action)
    {
        var button = new Button
        {
            Content = Mono(text).With(t => t.Foreground = Brushes.SteelBlue),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        button.Click += (_, _) => action();
        return button;
    }

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
