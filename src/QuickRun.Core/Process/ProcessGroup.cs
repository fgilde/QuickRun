using System.Runtime.InteropServices;
using SysProcess = System.Diagnostics.Process;

namespace QuickRun.Core.Process;

/// <summary>
/// Everything one command started, killable as a unit.
/// <para>
/// Killing a process tree is not enough. <c>dotnet run</c> builds, launches the application, and
/// the shell in between is gone by the time anyone asks to stop: the application has been orphaned,
/// its parent chain no longer leads back to the command, and it keeps serving. That is exactly what
/// happened to a stopped run whose port stayed answering.
/// </para>
/// <para>
/// On Windows the command is put into a job object, which every process it starts inherits and no
/// re-parenting can escape, and the job is terminated as a whole. Elsewhere this falls back to
/// killing the tree, which is what .NET offers.
/// </para>
/// </summary>
public sealed class ProcessGroup : IDisposable
{
    private readonly SysProcess _process;
    private readonly nint _job;

    private ProcessGroup(SysProcess process, nint job)
    {
        _process = process;
        _job = job;
    }

    /// <summary>Whether the job could be created and the process put into it.</summary>
    public bool Grouped => _job != nint.Zero;

    /// <summary>
    /// Puts <paramref name="process"/> and everything it will start into one group. Called right
    /// after the process starts: a child born before this point is not in the job.
    /// </summary>
    public static ProcessGroup Adopt(SysProcess process)
    {
        if (!OperatingSystem.IsWindows()) return new(process, nint.Zero);

        try
        {
            var job = CreateJobObject(nint.Zero, null);
            if (job == nint.Zero) return new(process, nint.Zero);

            // No kill-on-close: closing this handle when QuickRun exits must not take a running
            // application with it. Stopping is something the user asks for, explicitly.
            if (!AssignProcessToJobObject(job, process.Handle))
            {
                CloseHandle(job);
                return new(process, nint.Zero);
            }

            return new(process, job);
        }
        catch
        {
            // A process that exited between starting and being adopted, or a platform that refuses
            // the job. Either way the tree kill below is still there.
            return new(process, nint.Zero);
        }
    }

    /// <summary>Kills the group, and then the tree as well - whichever finds more.</summary>
    public void Terminate()
    {
        if (_job != nint.Zero)
        {
            try { TerminateJobObject(_job, 1); } catch { /* already gone */ }
        }

        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process exited between the check and the kill, or we lost the right to signal it.
        }
    }

    public void Dispose()
    {
        if (_job != nint.Zero) CloseHandle(_job);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(nint job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
