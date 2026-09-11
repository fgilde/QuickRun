using Avalonia.Controls;
using QuickRun.Core.Config;
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
    /// The windows handed-over repositories have opened: one plan each, read once and answered.
    /// <para>
    /// Its own window rather than a tab in the big one, because that is what a hand-over is - the
    /// browser extension has always opened exactly this, and a plan arriving from quickrun.org or a
    /// quickrun:// link deserves the same thing rather than the whole interface with a panel
    /// somewhere in it.
    /// </para>
    /// <para>
    /// One per target rather than one in total. There used to be a single window, and a second
    /// hand-over pointed it at the new plan - which meant a run somebody had started went off
    /// screen, log and Stop with it, and stopping it meant finding the run in the main window. So
    /// the same target raises the window it already has, and a different one gets its own.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, DashboardWindow> _confirm = new(StringComparer.Ordinal);

    /// <summary>
    /// How many plans may be waiting in their own windows at once.
    /// <para>
    /// A trusted site may ask for a window without anybody clicking, so "one per target" needs a
    /// ceiling or a page in a loop is a screen full of windows. Past it the oldest is reused, which
    /// is the window least likely to still be being read.
    /// </para>
    /// </summary>
    private const int MostConfirmWindows = 5;

    /// <summary>
    /// How long a window that has just appeared stays above everything else.
    /// <para>
    /// Long enough to be seen and to survive the moment the system decides who has the foreground;
    /// short enough that it is not in the way of what somebody was doing. Windows will not simply
    /// let a background process take the foreground - it flashes the taskbar button instead - and a
    /// plan waiting behind three other windows is a plan nobody notices.
    /// </para>
    /// </summary>
    private static readonly TimeSpan RaisedFor = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Brings a window to the front, and leaves it there only if that is what the reader asked for.
    /// </summary>
    private static void Raise(Window window, WorkspaceStore store)
    {
        window.Show();
        window.Topmost = true;
        window.Activate();

        if (new WindowPreferences(store.Root).AlwaysOnTop) return;

        // Back to an ordinary window once it has been seen. On a timer rather than on first focus:
        // a window nobody clicks would otherwise sit on top for ever, and that is the complaint
        // this is meant to prevent rather than cause.
        DispatcherTimer.RunOnce(() =>
        {
            if (window.IsVisible) window.Topmost = false;
        }, RaisedFor);
    }

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
            // The same plan again: the window showing it is the answer, not a second one beside it.
            if (_confirm.TryGetValue(hash, out var existing))
            {
                Raise(existing, store);
                return;
            }

            // At the ceiling the oldest window takes this plan instead - the one whose plan has been
            // on screen longest, and least likely to still be being read.
            if (_confirm.Count >= MostConfirmWindows)
            {
                var (oldestHash, oldest) = _confirm.First();
                _confirm.Remove(oldestHash);
                _confirm[hash] = oldest;

                oldest.GoTo(hash);
                Raise(oldest, store);
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

            // Stacked exactly on top of each other, two waiting plans look like one, and the second
            // one is the only one anybody can see.
            window.Opened += (_, _) => Offset(window, _confirm.Count - 1);

            window.Closed += (_, _) => _confirm.Remove(hash);
            _confirm[hash] = window;
            Raise(window, store);
        });
    }

    /// <summary>Moves a window clear of the ones already open, so both are findable.</summary>
    private static void Offset(Window window, int already)
    {
        if (already <= 0) return;

        var step = 30 * Math.Min(already, MostConfirmWindows);
        window.Position = window.Position.WithX(window.Position.X + step)
            .WithY(window.Position.Y + step);
    }

    public static void Show(RunRegistry runs, WorkspaceStore store, string listenerUrl, string hash = "") =>
        Dispatcher.UIThread.Post(() =>
        {
            // Clicking the tray icon twice should raise the window, not stack another one.
            if (_dashboard is { } existing)
            {
                if (hash.Length > 0) existing.GoTo(hash);

                existing.Show();
                existing.Activate();
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
