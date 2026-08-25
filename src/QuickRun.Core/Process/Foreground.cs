using System.Runtime.InteropServices;
using SysProcess = System.Diagnostics.Process;

namespace QuickRun.Core.Process;

/// <summary>
/// Brings a started desktop application to the front.
/// <para>
/// A web app announces a URL and the browser takes care of the rest. A desktop app has no such
/// hook: <c>dotnet run</c> compiles for a minute, the window finally appears behind the browser,
/// and QuickRun looks as if it did nothing. So the process tree the task started is watched, and
/// the first real window in it is raised.
/// </para>
/// <para>
/// Windows only. The X11 and macOS equivalents need a running compositor and extra tooling
/// (wmctrl, AppleScript) that cannot be assumed, so elsewhere this does nothing at all.
/// </para>
/// </summary>
public static class Foreground
{
    /// <summary>How long a window is worth waiting for. A cold build can take most of it.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    /// <summary>Anyone who does not want their focus taken can say so.</summary>
    public static bool Enabled =>
        OperatingSystem.IsWindows()
        && Environment.GetEnvironmentVariable("QUICKRUN_NO_FOREGROUND") is null or "" or "0";

    /// <summary>One process as the window hunt sees it: who started it, and what window it owns.</summary>
    public readonly record struct WindowOwner(int Pid, int ParentPid, nint Window);

    /// <summary>
    /// The window to raise for a task rooted at <paramref name="rootPid"/>, or 0 if the tree has
    /// none yet.
    /// <para>
    /// The tree matters: a task's command is a shell, which starts <c>dotnet run</c>, which starts
    /// the application. Only the descendants count - raising "the newest window on the desktop"
    /// would grab whatever the user opened while waiting.
    /// </para>
    /// </summary>
    public static nint PickWindow(IReadOnlyList<WindowOwner> processes, int rootPid)
    {
        var byParent = processes.GroupBy(p => p.ParentPid)
            .ToDictionary(g => g.Key, g => g.ToList());

        var queue = new Queue<int>();
        var seen = new HashSet<int> { rootPid };
        queue.Enqueue(rootPid);

        // The root's own window first, then its children's: the outermost process that owns a
        // window is the application, not a helper it spawned.
        while (queue.Count > 0)
        {
            var pid = queue.Dequeue();

            var self = processes.FirstOrDefault(p => p.Pid == pid);
            if (self.Pid == pid && self.Window != 0) return self.Window;

            if (!byParent.TryGetValue(pid, out var children)) continue;
            foreach (var child in children)
                if (seen.Add(child.Pid)) queue.Enqueue(child.Pid);
        }

        return 0;
    }

    /// <summary>Whether the tree under <paramref name="rootPid"/> has a window yet.</summary>
    public static bool HasWindow(int rootPid)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try { return PickWindow(Snapshot(), rootPid) != 0; }
        catch { return false; }
    }

    /// <summary>
    /// Watches the tree under <paramref name="rootPid"/> and raises the first window it finds.
    /// Returns when a window was raised, when the token is cancelled, or when patience runs out.
    /// </summary>
    public static async Task RaiseAsync(int rootPid, CancellationToken ct)
    {
        if (!Enabled) return;

        var deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                if (PickWindow(Snapshot(), rootPid) is var window && window != 0)
                {
                    Raise(window);
                    return;
                }
            }
            catch
            {
                // A process can exit between listing and inspecting it. Nothing here is worth
                // failing a run over.
            }

            try { await Task.Delay(700, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Every process in this session, with its parent and its main window.</summary>
    private static List<WindowOwner> Snapshot()
    {
        var owners = new List<WindowOwner>();
        if (!OperatingSystem.IsWindows()) return owners;

        var parents = Parents();

        foreach (var process in SysProcess.GetProcesses())
            using (process)
            {
                nint window;
                try { window = process.MainWindowHandle; }
                catch { continue; }

                if (window != 0 && !IsWindowVisible(window)) window = 0;

                owners.Add(new WindowOwner(process.Id, parents.GetValueOrDefault(process.Id, 0), window));
            }

        return owners;
    }

    /// <summary>
    /// The parent of every process, from a Toolhelp snapshot. .NET exposes no parent process id,
    /// and without it there is no way to tell the application apart from the rest of the desktop.
    /// </summary>
    private static Dictionary<int, int> Parents()
    {
        var parents = new Dictionary<int, int>();

        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == nint.Zero || snapshot == new nint(-1)) return parents;

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (!Process32First(snapshot, ref entry)) return parents;

            do parents[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return parents;
    }

    private static void Raise(nint window)
    {
        // A minimised window has to be restored before it can be focused.
        if (IsIconic(window)) ShowWindow(window, SW_RESTORE);

        AllowSetForegroundWindow(ASFW_ANY);
        SetForegroundWindow(window);
        if (GetForegroundWindow() == window) return;

        // Windows refuses the foreground to a process that is not in the foreground itself - which
        // QuickRun usually is not by the time a build finishes and the window finally appears.
        // Sharing the input queue of whatever holds the foreground lifts that refusal.
        var holder = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var owner = GetWindowThreadProcessId(window, out _);

        if (holder != 0 && owner != 0 && holder != owner && AttachThreadInput(holder, owner, true))
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
            AttachThreadInput(holder, owner, false);
        }

        if (GetForegroundWindow() == window) return;

        SwitchToThisWindow(window, true);

        // Some group policies forbid the foreground outright. Then the taskbar entry flashes, which
        // is still better than the window appearing behind everything with nothing to say it did.
        if (GetForegroundWindow() != window) FlashWindow(window, true);
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const int SW_RESTORE = 9;
    private const uint ASFW_ANY = unchecked((uint)-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32First(nint snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(nint snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint from, uint to, bool attach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(nint window, bool altTab);

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(nint window, bool invert);
}
