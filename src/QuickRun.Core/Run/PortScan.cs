using System.Net.Sockets;
using QuickRun.Core.Config;

namespace QuickRun.Core.Run;

public sealed record PortConflict(string Task, int Port);

/// <summary>
/// Finds declared readiness ports that something else is already listening on. Without this a
/// `readyWhen: {port: 5000}` task reports ready instantly against a stranger's server, and the
/// user sees the wrong application.
/// </summary>
public static class PortScan
{
    /// <summary>
    /// How long a single loopback connect may take before the port counts as free. Generous on
    /// purpose: a busy machine can take well over a hundred milliseconds to accept a loopback
    /// connection, and a check that gives up early reports occupied ports as free - which is the
    /// one answer that makes the warning useless.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public static IReadOnlyList<PortConflict> Occupied(
        RunConfig config, Func<int, bool>? isInUse = null, TimeSpan? probeTimeout = null)
    {
        var timeout = probeTimeout ?? ProbeTimeout;
        isInUse ??= port => InUse(port, timeout);

        return config.Tasks
            .Where(task => task.ReadyWhen?.Port is not null)
            .Select(task => new PortConflict(task.Name, task.ReadyWhen!.Port!.Value))
            .Where(conflict => isInUse(conflict.Port))
            .ToList();
    }

    private static bool InUse(int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", port).Wait(timeout) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
