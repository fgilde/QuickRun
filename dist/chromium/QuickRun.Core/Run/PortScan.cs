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
    public static IReadOnlyList<PortConflict> Occupied(RunConfig config, Func<int, bool>? isInUse = null)
    {
        isInUse ??= InUse;

        return config.Tasks
            .Where(task => task.ReadyWhen?.Port is not null)
            .Select(task => new PortConflict(task.Name, task.ReadyWhen!.Port!.Value))
            .Where(conflict => isInUse(conflict.Port))
            .ToList();
    }

    private static bool InUse(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", port).Wait(300) && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
