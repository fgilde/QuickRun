using Avalonia.Threading;
using QuickRun.App.Daemon;
using QuickRun.Core.Run;
using QuickRun.Core.Workspace;

namespace QuickRun.App.Ui;

/// <summary>
/// Opens the desktop window, and keeps there being only one of it. Named AppWindows rather
/// than Windows, which collides with a framework namespace. Avalonia windows must be created
/// on its UI thread, which the tray's event loop owns.
/// </summary>
public static class AppWindows
{
    private static DashboardWindow? _dashboard;

    public static void Show(RunRegistry runs, WorkspaceStore store, string listenerUrl) =>
        Dispatcher.UIThread.Post(() =>
        {
            // Clicking the tray icon twice should raise the window, not stack another one.
            if (_dashboard is { } existing)
            {
                existing.Show();
                existing.Activate();
                return;
            }

            var window = new DashboardWindow(runs, store, listenerUrl);
            window.Closed += (_, _) => _dashboard = null;
            _dashboard = window;
            window.Show();
        });
}
