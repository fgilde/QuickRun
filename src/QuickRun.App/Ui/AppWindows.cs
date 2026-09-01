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
