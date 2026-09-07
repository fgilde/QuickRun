using System.Net;
using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// Reading a config from an address.
/// <para>
/// The fetch is injected, so nothing here touches the network. What is being pinned down is the
/// shape of the answer: a config, or a reason there is none - and never an exception reaching the
/// window, because a page pressed a button and something has to be shown either way.
/// </para>
/// </summary>
public class RemoteConfigTests
{
    private const string Url = "https://example.com/configs/app.yml";

    private static Func<string, Task<HttpResponseMessage>> Answers(
        HttpStatusCode code, string body, long? declaredLength = null)
        => _ =>
        {
            var response = new HttpResponseMessage(code) { Content = new StringContent(body) };

            if (declaredLength is { } length) response.Content.Headers.ContentLength = length;

            return Task.FromResult(response);
        };

    [Fact]
    public async Task A_published_config_comes_back_as_text()
    {
        var (text, error) = await RemoteConfig.ReadAsync(Url,
            Answers(HttpStatusCode.OK, "tasks:\n  - run: echo hi\n"));

        Assert.Null(error);
        Assert.Contains("echo hi", text);
    }

    [Fact]
    public async Task An_address_a_config_may_not_come_from_is_refused_before_asking()
    {
        var asked = false;

        var (text, error) = await RemoteConfig.ReadAsync("https://192.168.0.5/app.yml", _ =>
        {
            asked = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        Assert.Null(text);
        Assert.Contains("not an address", error);

        // The point of checking first: the request itself is what must not happen.
        Assert.False(asked, "it asked an address it had already decided against");
    }

    [Fact]
    public async Task A_redirect_is_not_followed()
    {
        // The client is built with redirects off, so one arrives here as what it is: not a success.
        // A public address answering "now ask 192.168.0.1" would otherwise walk around the check
        // above.
        var (text, error) = await RemoteConfig.ReadAsync(Url, Answers(HttpStatusCode.Found, ""));

        Assert.Null(text);
        Assert.Contains("302", error);
    }

    [Fact]
    public async Task Something_that_is_not_there_says_so_with_its_status()
    {
        var (_, error) = await RemoteConfig.ReadAsync(Url, Answers(HttpStatusCode.NotFound, "nope"));

        Assert.Contains("404", error);
        Assert.Contains(Url, error);
    }

    [Fact]
    public async Task A_body_too_large_to_be_a_config_is_refused()
    {
        var huge = new string('x', RemoteConfig.MostBytes + 10);

        // Declared, which is the polite case.
        var (declared, declaredError) = await RemoteConfig.ReadAsync(Url,
            Answers(HttpStatusCode.OK, huge, huge.Length));

        Assert.Null(declared);
        Assert.Contains("larger than", declaredError);

        // And undeclared, which is the one that matters: nothing said how much was coming.
        var (undeclared, undeclaredError) = await RemoteConfig.ReadAsync(Url, _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(huge))),
            };

            response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        });

        Assert.Null(undeclared);
        Assert.Contains("larger than", undeclaredError);
    }

    [Fact]
    public async Task An_empty_answer_is_not_a_config()
    {
        var (text, error) = await RemoteConfig.ReadAsync(Url, Answers(HttpStatusCode.OK, "   \n"));

        Assert.Null(text);
        Assert.Contains("empty", error);
    }

    [Fact]
    public async Task A_network_that_is_not_there_is_reported_rather_than_thrown()
    {
        var (text, error) = await RemoteConfig.ReadAsync(Url,
            _ => throw new HttpRequestException("no route to host"));

        Assert.Null(text);
        Assert.Contains("no route to host", error);
    }
}
