namespace QuickRun.Core.Config;

/// <summary>
/// Reads a config published at an address.
/// <para>
/// What arrives is text that becomes a plan on screen, and nothing runs until somebody approves it
/// in QuickRun's own window - so this is not a way to execute anything. It is still a request the
/// daemon makes on a page's behalf, which is why the address has been through
/// <see cref="ConfigReference"/> before it gets here and why nothing here follows a redirect: a
/// public address that answers with "now go and ask 192.168.0.1" would walk straight around that
/// check.
/// </para>
/// </summary>
public static class RemoteConfig
{
    /// <summary>The same ceiling a config file on disk gets. A config is a page of text.</summary>
    public const int MostBytes = 512 * 1024;

    /// <summary>Short, because a person pressed a button and is waiting for the window.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient Http = Create();

    /// <summary>
    /// The config's text, or the reason there is none.
    /// </summary>
    /// <param name="fetch">
    /// Injectable so tests never reach the network. Given the address, it returns the body or
    /// throws the way HttpClient would.
    /// </param>
    public static async Task<(string? Text, string? Error)> ReadAsync(
        string url, Func<string, Task<HttpResponseMessage>>? fetch = null)
    {
        var reference = ConfigReference.Read(url);

        if (reference.Kind != ConfigReferenceKind.Remote)
            return (null, $"'{url}' is not an address a config may be read from");

        try
        {
            using var response = await (fetch ?? Get)(reference.Value);

            if (!response.IsSuccessStatusCode)
                return (null, $"{reference.Value} answered {(int)response.StatusCode}");

            // Asked before reading, and checked again while reading: a length nobody declared is
            // the case that matters, and a body that keeps coming has to be stopped somewhere.
            if (response.Content.Headers.ContentLength is > MostBytes)
                return (null, $"{reference.Value} is larger than {MostBytes / 1024} KB");

            var text = await Read(response);

            if (text is null) return (null, $"{reference.Value} is larger than {MostBytes / 1024} KB");
            if (string.IsNullOrWhiteSpace(text)) return (null, $"{reference.Value} is empty");

            return (text, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return (null, $"could not read {reference.Value}: {e.Message}");
        }
    }

    private static async Task<string?> Read(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();

        var buffer = new byte[MostBytes + 1];
        var read = 0;

        while (read < buffer.Length)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(read));
            if (got == 0) break;
            read += got;
        }

        return read > MostBytes ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static Task<HttpResponseMessage> Get(string url) => Http.GetAsync(url);

    private static HttpClient Create()
    {
        // No redirects, for the reason in the class comment. No proxy, because WPAD resolution on a
        // Windows machine has already cost this project four red CI runs.
        var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };

        var client = new HttpClient(handler) { Timeout = Timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"QuickRun/{BuildInfo.Version}");

        return client;
    }
}
