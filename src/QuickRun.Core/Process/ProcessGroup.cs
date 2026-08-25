using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SysProcess = System.Diagnostics.Process;

namespace QuickRun.Core.Process;

/// <summary>
/// Everything a run started, killable as a unit and for as long as the run is remembered.
/// <para>
/// Killing a process tree is not enough, and killing it only while the command is alive is not
/// either. Two failures, both seen in the wild: an application whose intermediate process went away
/// is orphaned, so the parent chain no longer leads to it; and a task that launches something in the
/// background and exits leaves a server running under a command that has already finished. In both
/// cases the run reported itself stopped while the thing kept answering on its port.
/// </para>
/// <para>
/// On Windows a job object holds every process the run starts. Processes inherit it, no re-parenting
/// escapes it, it can be asked how many members are still alive, and terminating it kills them all -
/// including what a finished task left behind. Elsewhere this falls back to killing the trees of the
/// processes it was given, which is what .NET offers.
/// </para>
/// </summary>
public sealed class ProcessGroup : IDisposable
{
    private readonly nint _job;

    /// <summary>
    /// The processes handed to this group, by id. Ids rather than <c>Process</c> objects: the caller
    /// owns and disposes those, and a disposed one can no longer be asked anything.
    /// </summary>
    private readonly ConcurrentBag<int> _members = new();

    private ProcessGroup(nint job) => _job = job;

    /// <summary>Whether a job object is behind this group, or only the tree fallback.</summary>
    public bool Grouped => _job != nint.Zero;

    /// <summary>One group, for one run.</summary>
    public static ProcessGroup Create()
    {
        if (!OperatingSystem.IsWindows()) return new(nint.Zero);

        try
        {
            // No kill-on-close: closing the handle when QuickRun exits must not take a running
            // application with it. Stopping is something a user asks for, explicitly.
            return new(CreateJobObject(nint.Zero, null));
        }
        catch (DllNotFoundException)
        {
            return new(nint.Zero);
        }
    }

    /// <summary>
    /// Puts a process, and everything it goes on to start, into the group. Called immediately after
    /// the process starts: a child born before this point is not a member.
    /// </summary>
    public void Add(SysProcess process)
    {
        try { _members.Add(process.Id); }
        catch (InvalidOperationException) { return; }

        if (_job == nint.Zero) return;

        try { AssignProcessToJobObject(_job, process.Handle); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException
                                     or EntryPointNotFoundException or DllNotFoundException)
        {
            // Exited between starting and being adopted, or no job to put it in. The tree fallback
            // in Terminate still applies.
        }
    }

    /// <summary>
    /// How many processes of this run are still alive - including ones a finished task left behind,
    /// which is what makes "it says stopped but it is still running" answerable rather than a claim.
    /// </summary>
    public int LiveCount()
    {
        if (_job == nint.Zero) return 0;

        // JOBOBJECT_BASIC_PROCESS_ID_LIST is two counts followed by as many ids as fit, so the
        // buffer sets the ceiling. A few hundred is far more than a run ever has.
        const int capacity = 512;
        var size = (sizeof(uint) * 2) + (nint.Size * capacity);
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            return QueryInformationJobObject(_job, JobObjectBasicProcessIdList, buffer, size, out _)
                ? Marshal.ReadInt32(buffer, sizeof(uint))
                : 0;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Kills the group, and the trees of what it was given - whichever finds more.</summary>
    public void Terminate()
    {
        if (_job != nint.Zero)
        {
            try { TerminateJobObject(_job, 1); }
            catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { }
        }

        foreach (var pid in _members)
        {
            try
            {
                using var process = SysProcess.GetProcessById(pid);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone, its id reused, or we lost the right to signal it. The job above is
                // the reliable half of this; the tree walk is the fallback where there is no job.
            }
        }
    }

    public void Dispose()
    {
        if (_job != nint.Zero) CloseHandle(_job);
    }

    private const int JobObjectBasicProcessIdList = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(nint job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(nint job, int infoClass, nint info,
        int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
