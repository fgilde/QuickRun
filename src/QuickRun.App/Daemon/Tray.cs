using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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

    public static Action? OpenPairing { get; set; }

    public static Action? Quit { get; set; }

    public static string Tooltip { get; set; } = "QuickRun";

    private TrayIcon? _icon;

    public override void Initialize() => Name = "QuickRun";

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
    }

    private static NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        menu.Add(Item("Open QuickRun", () => OpenDashboard?.Invoke()));
        menu.Add(Item("Pair browser extension", () => OpenPairing?.Invoke()));
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
