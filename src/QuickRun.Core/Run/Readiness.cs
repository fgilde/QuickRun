using System.Net.Sockets;
using System.Text.RegularExpressions;
using QuickRun.Core.Config;

namespace QuickRun.Core.Run;

/// <summary>Waits for a task's declared readiness condition, or gives up at the timeout.</summary>
public static class Readiness
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    /// <summary>
    /// Readiness probes are local, so this never goes through a proxy: HttpClient would otherwise
    /// resolve the system one, and on Windows that means WPAD - seconds of waiting on a machine that
    /// has no proxy, spent on the first probe of every run.
    /// </summary>
    private static readonly HttpClient Http =
        new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(3),
        };

    /// <param name="logSoFar">Reads the task's output so far, for log-pattern conditions.</param>
    /// <param name="windowProbe">
    /// Whether the task's process has a window yet. Injectable, and absent means "no window will
    /// ever appear" - which is the right answer everywhere a window is not a thing.
    /// </param>
    /// <param name="portProbe">Injectable so tests need not bind ports.</param>
    /// <param name="httpProbe">Injectable so tests need not start a server.</param>
    public static async Task<bool> WaitAsync(
        ReadyWhen? readyWhen,
        Func<string> logSoFar,
        TimeSpan timeout,
        CancellationToken ct,
        Func<int, Task<bool>>? portProbe = null,
        Func<string, Task<bool>>? httpProbe = null,
        Func<bool>? windowProbe = null)
    {
        if (readyWhen is null) return true;

        if (readyWhen.Delay is { } delay)
        {
            try { await Task.Delay(delay, ct); return true; }
            catch (OperationCanceledException) { return false; }
        }

        portProbe ??= PortOpenAsync;
        httpProbe ??= HttpAnsweringAsync;

        Regex? logPattern = null;
        if (readyWhen.Log is { } pattern)
        {
            try { logPattern = new Regex(pattern, RegexOptions.Compiled); }
            catch (ArgumentException) { return false; }
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var ready = readyWhen switch
            {
                { Port: { } port } => await Safe(() => portProbe(port)),
                { Http: { } url } => await Safe(() => httpProbe(url)),
                { Window: true } => windowProbe?.Invoke() ?? false,
                _ when logPattern is not null => logPattern.IsMatch(logSoFar()),
                _ => true,
            };
            if (ready) return true;

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { return false; }
        }

        return false;
    }

    private static async Task<bool> Safe(Func<Task<bool>> probe)
    {
        try { return await probe(); } catch { return false; }
    }

    /// <summary>Whether a loopback port is answering. Public so a caller can ask before starting.</summary>
    public static async Task<bool> PortOpenAsync(int port)
    {
        using var client = new TcpClient();
        var connect = client.ConnectAsync("127.0.0.1", port);
        var finished = await Task.WhenAny(connect, Task.Delay(500));
        return finished == connect && client.Connected;
    }

    /// <summary>
    /// Anything below 500 counts as up: a dev server answering 404 on / is running, and waiting
    /// for a 200 would hang on apps whose root path is not routed.
    /// </summary>
    public static async Task<bool> HttpAnsweringAsync(string url)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        return (int)response.StatusCode < 500;
    }

    /// <summary>
    /// What the address actually answered, or null when it could not be asked.
    /// <para>
    /// Ready and useful are not the same thing, and the difference is worth a line in the log: a web
    /// project built in the wrong configuration answers 404 for its entire front end, which counts
    /// as up by the rule above and is not what anyone was waiting for.
    /// </para>
    /// </summary>
    public static async Task<int?> HttpStatusAsync(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return (int)response.StatusCode;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
