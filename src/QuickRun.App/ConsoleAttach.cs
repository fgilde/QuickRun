using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace QuickRun.App;

/// <summary>
/// Makes one binary work as both a desktop application and a command line tool on Windows.
/// <para>
/// The executable is built for the GUI subsystem, so double-clicking it opens no console window.
/// A GUI-subsystem process does not inherit its parent's console either, which would leave
/// <c>quickrun run …</c> in a terminal completely silent - so when there is a parent console, this
/// attaches to it and points the standard streams at it.
/// </para>
/// <para>
/// The trade-off is known and accepted: the shell has already printed its next prompt by the time
/// output arrives, so a prompt can appear above the output. Building for the console subsystem
/// instead would flash a black window on every double-click, which is worse.
/// </para>
/// </summary>
public static class ConsoleAttach
{
    private const int AttachParentProcess = -1;

    /// <summary>True when this process has a console to write to.</summary>
    public static bool HasConsole { get; private set; }

    public static void TryAttach()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Everywhere else the distinction does not exist: stdout is stdout.
            HasConsole = true;
            return;
        }

        HasConsole = AttachToParent();
    }

    [SupportedOSPlatform("windows")]
    private static bool AttachToParent()
    {
        // Already have one (started from a console-subsystem host, or a debugger): nothing to do.
        if (GetConsoleWindow() != IntPtr.Zero) return true;

        if (!AttachConsole(AttachParentProcess)) return false;

        try
        {
            // The streams were bound before the console existed, so they have to be rebuilt.
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), Console.OutputEncoding) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // DllImport rather than LibraryImport: the source generator requires AllowUnsafeBlocks for
    // the whole project, which is a steep price for two trivial signatures.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
