using Avalonia;
using Avalonia.Controls;
using QuickRun.App.Daemon;

namespace QuickRun.App.Ui;

/// <summary>
/// Builds the desktop pieces once and takes them down again, to find out whether they work here.
/// <para>
/// The same tray application the daemon runs, so the same icon is loaded the same way - and then a
/// window is shown, because showing a window is what loads the executable's own icon. That load is
/// where a malformed icon file ended the process on the first right-click of the tray menu, inside
/// Avalonia, with nothing to catch it. Creating the loop at all is what failed on macOS when it was
/// not on the process's first thread. Neither had anything watching it; now both do.
/// </para>
/// </summary>
public static class DesktopProbe
{
    /// <summary>Null when it all worked, otherwise what went wrong.</summary>
    public static string? Run(TimeSpan timeout)
    {
        string? failure = null;
        var reached = false;

        using var shutdown = new CancellationTokenSource(timeout);
        var previous = TrayApp.Started;

        TrayApp.Started = () =>
        {
            reached = true;

            try
            {
                // Off-screen and undecorated, because this is a check and not a thing to look at.
                // Show() is the point: it is what loads the icon.
                var window = new Window
                {
                    Title = "QuickRun self-check",
                    Width = 160,
                    Height = 90,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = new PixelPoint(-32000, -32000),
                };

                window.Show();
                window.Close();
            }
            catch (Exception e)
            {
                failure = $"{e.GetType().Name}: {e.Message}";
            }
            finally
            {
                shutdown.Cancel();
            }
        };

        try
        {
            UiHost.RunLoop(() => TrayApp.Run(shutdown.Token));
        }
        catch (Exception e)
        {
            failure ??= $"{e.GetType().Name}: {e.Message}";
        }
        finally
        {
            TrayApp.Started = previous;
        }

        if (failure is null && !reached)
            failure = $"the tray loop did not start within {timeout.TotalSeconds:0}s";

        return failure;
    }
}
