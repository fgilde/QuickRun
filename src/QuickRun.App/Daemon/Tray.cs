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

    public static Action? OpenPairing { get; set; }

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
        _icon = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = Tooltip,
            IsVisible = true,
            Menu = BuildMenu(),
        };

        // Clicking the icon itself is the shortest path to the thing people want.
        _icon.Clicked += (_, _) => OpenDashboard?.Invoke();

        base.OnFrameworkInitializationCompleted();

        // Only safe once the framework is up: a window cannot be created before this point.
        Started?.Invoke();
    }

    private static NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        menu.Add(Item("Open QuickRun", () => OpenDashboard?.Invoke()));
        menu.Add(Item("Pair browser extension", () => OpenPairing?.Invoke()));
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
        lifetime.Start([]);
    }
}
