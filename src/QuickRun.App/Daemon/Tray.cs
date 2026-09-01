using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace QuickRun.App.Daemon;

/// <summary>
/// The tray icon, and with it the reason QuickRun does not vanish when you double-click it.
/// <para>
/// Avalonia rather than hand-written Win32 P/Invoke: one code path for Windows, Linux and macOS,
/// and a shell notification icon with a message loop and a popup menu is exactly the kind of code
/// nobody wants to debug at 3am.
/// </para>
/// </summary>
public sealed class TrayApp : Application
{
    /// <summary>What the menu items do. Set before <see cref="Run"/>.</summary>
    public static Action? OpenDashboard { get; set; }

    public static Action? OpenInBrowser { get; set; }


    public static Action? Quit { get; set; }

    /// <summary>Runs once the UI thread is up, for whatever should be shown at startup.</summary>
    public static Action? Started { get; set; }

    public static string Tooltip { get; set; } = "QuickRun";

    private TrayIcon? _icon;

    public override void Initialize()
    {
        Name = "QuickRun";

        // Without a theme, templated controls (TabControl, Button, ProgressBar) have no template
        // and render as nothing at all, while raw TextBlocks still show. Easy to misread as a
        // layout bug.
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // A tray icon is a courtesy; the window is the program. Not every desktop has somewhere to
        // put an icon - GNOME needs an extension for it, a bare window manager has nothing at all -
        // and when creating one threw, everything below it was skipped: no window opened, and
        // QuickRun looked like it had started and drawn nothing.
        try
        {
            _icon = new TrayIcon
            {
                Icon = LoadIcon(),
                ToolTipText = Tooltip,
                IsVisible = true,
                Menu = BuildMenu(),
            };

            // Clicking the icon itself is the shortest path to the thing people want.
            _icon.Clicked += (_, _) => OpenDashboard?.Invoke();
        }
        catch (Exception e)
        {
            Output.Warn($"no tray icon on this desktop ({e.Message}) - the window is still here");
        }

        base.OnFrameworkInitializationCompleted();

        // Only safe once the framework is up: a window cannot be created before this point.
        Started?.Invoke();
    }

    private static NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        menu.Add(Item("Open QuickRun", () => OpenDashboard?.Invoke()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Open in browser", () => OpenInBrowser?.Invoke()));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit", () => Quit?.Invoke()));

        return menu;
    }

    private static NativeMenuItem Item(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            using var stream = typeof(TrayApp).Assembly
                .GetManifestResourceStream("QuickRun.App.Daemon.icon.png");
            return stream is null ? null : new WindowIcon(new Bitmap(stream));
        }
        catch (Exception)
        {
            // A missing icon must not stop the daemon; the dashboard is still reachable.
            return null;
        }
    }

    /// <summary>
    /// Runs the tray's event loop on the calling thread, which must be the process's main thread.
    /// Returns when <see cref="Quit"/> shuts it down.
    /// </summary>
    public static void Run(CancellationToken shutdown)
    {
        var builder = AppBuilder.Configure<TrayApp>().UsePlatformDetect();

        using var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        builder.SetupWithLifetime(lifetime);

        shutdown.Register(() => Dispatcher.UIThread.Post(() => lifetime.Shutdown()));

        // Quitting cannot be driven from a test - a tray menu needs a desktop and a real click -
        // so this is the seam that makes it reachable. It fires the same Quit the menu item does.
        if (int.TryParse(Environment.GetEnvironmentVariable("QUICKRUN_QUIT_AFTER_MS"), out var delay))
            DispatcherTimer.RunOnce(() => Quit?.Invoke(), TimeSpan.FromMilliseconds(delay));

        lifetime.Start([]);

        // Avalonia installs its own SynchronizationContext on this thread, and the dispatcher
        // backing it is gone the moment the loop ends. Anything awaited afterwards would try to
        // resume on a dispatcher that will never run another callback and would wait forever -
        // which is exactly how quitting used to hang, in app.StopAsync.
        SynchronizationContext.SetSynchronizationContext(null);
    }
}
