using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace QuickRun.App.Ui;

/// <summary>
/// Where the window was last time.
/// <para>
/// A program that opens where you left it feels installed; one that jumps back to the middle of the
/// screen at its default size feels like a downloaded file. The state lives next to the workspaces,
/// so it follows QUICKRUN_HOME like everything else QuickRun writes.
/// </para>
/// </summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximised)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Applies the remembered placement, and remembers changes from then on.</summary>
    public static void Remember(Window window, string root)
    {
        var file = Path.Combine(root, "window.json");
        var saved = Read(file);

        // What to come back to. Maximised windows report the screen as their size, so the size and
        // position that get written are the last ones the window had while it was a normal window.
        var restore = saved is not null && Sane(saved, window)
            ? saved
            : new WindowPlacement(0, 0, (int)window.Width, (int)window.Height, false);

        if (saved is not null && Sane(saved, window))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(saved.X, saved.Y);
            window.Width = saved.Width;
            window.Height = saved.Height;
            if (saved.Maximised) window.WindowState = WindowState.Maximized;
        }

        void Track()
        {
            if (window.WindowState != WindowState.Normal) return;
            restore = restore with
            {
                X = window.Position.X,
                Y = window.Position.Y,
                Width = (int)window.Width,
                Height = (int)window.Height,
            };
        }

        window.PositionChanged += (_, _) => Track();
        window.SizeChanged += (_, _) => Track();

        // Written on close, not on every mouse move: a resize is not a reason to touch the disk.
        window.Closing += (_, _) => Write(file,
            restore with { Maximised = window.WindowState == WindowState.Maximized });
    }

    /// <summary>
    /// Whether a remembered placement can still be used. A window restored onto a monitor that is
    /// no longer there is a window nobody can reach.
    /// </summary>
    private static bool Sane(WindowPlacement placement, Window window)
    {
        if (placement.Width < 400 || placement.Height < 300) return false;

        var screens = window.Screens;
        if (screens is null || screens.ScreenCount == 0) return true;

        var point = new PixelPoint(placement.X + 40, placement.Y + 40);
        return screens.All.Any(screen => screen.Bounds.Contains(point));
    }

    private static WindowPlacement? Read(string file)
    {
        try
        {
            return File.Exists(file)
                ? JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(file))
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
    }

    private static void Write(string file, WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(placement, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember where the window was is not worth a message.
        }
    }
}
