using Avalonia.Platform.Storage;
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

    /// <summary>
    /// The window a handed-over repository opens in: one plan, read once and answered.
    /// <para>
    /// Its own window rather than a tab in the big one, because that is what a hand-over is - the
    /// browser extension has always opened exactly this, and a plan arriving from quickrun.org or a
    /// quickrun:// link deserves the same thing rather than the whole interface with a panel
    /// somewhere in it. Kept as one: a second hand-over points this window at the new plan instead
    /// of stacking another.
    /// </para>
    /// </summary>
    private static DashboardWindow? _confirm;

    /// <param name="hash">
    /// What to show once it is open - the dashboard's own <c>#run?repo=...</c>, when a link named a
    /// repository. Empty means the window opens where it always does.
    /// </param>
    /// <summary>
    /// Opens the confirmation window on a target, or the whole interface when there is none.
    /// <para>
    /// A tray click has nothing to confirm and gets the dashboard; everything that names a
    /// repository, a file or a prepared run is something to read and answer, and gets the window
    /// for that.
    /// </para>
    /// </summary>
    public static void ShowTarget(RunRegistry runs, WorkspaceStore store, string listenerUrl, string hash)
    {
        if (hash.Length == 0) { Show(runs, store, listenerUrl); return; }

        Dispatcher.UIThread.Post(() =>
        {
            if (_confirm is { } existing)
            {
                existing.Show();
                existing.Activate();
                existing.GoTo(hash);
                return;
            }

            // The size the browser extension's window has used all along, because this is the same
            // window by another route and two different shapes for one thing is a thing to explain.
            var window = new DashboardWindow(runs, store, listenerUrl, hash, "confirm")
            {
                Title = "QuickRun - confirm",
                Width = 760,
                Height = 720,
            };

            window.Closed += (_, _) => _confirm = null;
            _confirm = window;
            window.Show();
        });
    }

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

            // The target is handed to the window rather than navigated to afterwards. Afterwards
            // raced the WebView's own start-up and lost: the page loaded with no target, so the
            // first hand-over to a closed window did nothing and only a second one worked.
            var window = new DashboardWindow(runs, store, listenerUrl, hash);
            window.Closed += (_, _) => _dashboard = null;
            _dashboard = window;
            window.Show();
        });

    /// <summary>
    /// Asks for a folder with the system's own picker, over the window if there is one.
    /// <para>
    /// The page cannot do this itself: a file input hands a browser the contents of a selection and
    /// never the path, on purpose. So the page asks the host, and the host - which is a desktop
    /// application - opens the picker every other program opens.
    /// </para>
    /// </summary>
    /// <returns>The folder, or null when the picker was dismissed or there was no window.</returns>
    /// <summary>
    /// The system's file picker, limited to configs. What the green Run file button opens.
    /// <para>
    /// The filter is a courtesy, not the guard: whatever comes back is checked again before it is
    /// read, because a picker can be talked into returning anything on some platforms.
    /// </para>
    /// </summary>
    public static async Task<string?> PickConfigAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_dashboard is not { } window) return null;

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a quickrun.yml to run",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("QuickRun config")
                    {
                        Patterns = new[] { "*.yml", "*.yaml" },
                    },
                },
            });

            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        });
    }

    public static async Task<string?> PickFolderAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_dashboard is not { } window) return null;

            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose a folder to run",
                AllowMultiple = false,
            });

            return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
        });
    }
}
