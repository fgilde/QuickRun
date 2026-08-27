using System.Diagnostics;
using System.Text.Json;
using QuickRun.Core;

namespace QuickRun.App.Daemon;

/// <summary>
/// Keeps one QuickRun per machine, and makes the second start useful instead of fatal.
/// <para>
/// Two instances is not a theoretical problem: every <c>quickrun://</c> link, every double-click on
/// the binary and every autostart entry is another attempt to start one. The port is what decides -
/// whoever holds it is QuickRun, and anyone else hands their reason for existing over to it and
/// leaves. That way a second start raises the window that already exists rather than failing on a
/// bind error nobody can act on, and no repository is ever run twice by accident.
/// </para>
/// </summary>
public static class SingleInstance
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMilliseconds(1500) };

    /// <summary>Whether QuickRun already answers on this port.</summary>
    public static async Task<bool> RunningAsync(int port, CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{port}/api/ping", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks the instance that is already running to show itself, and says whether it did. False
    /// means it is an older build without the endpoint - then the browser is the way in.
    /// </summary>
    public static async Task<bool> ShowAsync(int port, string? target = null, CancellationToken ct = default)
    {
        var url = $"http://127.0.0.1:{port}/api/show";
        if (!string.IsNullOrEmpty(target)) url += $"?{target}";

        try
        {
            using var response = await Http.PostAsync(url, content: null, ct);
            if (!response.IsSuccessStatusCode) return false;

            // 200 is not the answer - "shown" is. A headless QuickRun accepts the request and
            // truthfully reports that there was no window to raise, and treating that as success
            // would leave the caller waiting for a window instead of opening the page.
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return body.RootElement.TryGetProperty("shown", out var shown)
                   && shown.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for QuickRun to answer, for a start that has just been triggered.
    /// </summary>
    public static async Task<bool> WaitAsync(int port, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await RunningAsync(port, ct)) return true;
            try { await Task.Delay(250, ct); } catch (OperationCanceledException) { return false; }
        }

        return false;
    }

    /// <summary>
    /// Starts QuickRun's own executable with the interface, detached from this process. Used by the
    /// URL handler, which must not become the long-lived instance itself: it was started by the
    /// operating system for one link and its console window would hang around for the whole session.
    /// </summary>
    public static bool Start(int port)
    {
        if (Environment.ProcessPath is not { } executable) return false;

        try
        {
            // UseShellExecute, so the new process inherits none of this one's handles. Inheriting
            // them keeps whatever started the link - a terminal, a browser's launcher - waiting on a
            // pipe that only closes when QuickRun does, which can be days.
            //
            // And therefore Arguments rather than ArgumentList, which .NET refuses to combine with
            // it - by throwing, which this used to catch and report as "could not start QuickRun"
            // with no reason attached. Nothing here needs quoting: a port is digits.
            Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = $"ui --port {port} --no-window",
                UseShellExecute = true,
                CreateNoWindow = true,
            });

            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Output.Warn($"could not start QuickRun: {e.Message}");
            return false;
        }
    }
}
