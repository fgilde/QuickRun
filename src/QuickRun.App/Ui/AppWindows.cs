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

    /// <param name="hash">
    /// What to show once it is open - the dashboard's own <c>#run?repo=...</c>, when a link named a
    /// repository. Empty means the window opens where it always does.
    /// </param>
    public static void Show(RunRegistry runs, WorkspaceStore store, string listenerUrl, string hash = "") =>
        Dispatcher.UIThread.Post(() =>
        {
            // Clicking the tray icon twice should raise the window, not stack another one.
            if (_dashboard is { } existing)
            {
                existing.Show();
                existing.Activate();
                if (hash.Length > 0) existing.GoTo(hash);
                return;
            }

            var window = new DashboardWindow(runs, store, listenerUrl);
            window.Closed += (_, _) => _dashboard = null;
            _dashboard = window;
            window.Show();

            // After Show, because there is no view to point anywhere before the window exists.
            if (hash.Length > 0) window.GoTo(hash);
        });
}
