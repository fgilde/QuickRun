using QuickRun.Core.Foreign;

namespace QuickRun.Core.Tests;

public class JsLiteralTests
{
    [Fact]
    public void Plain_json_is_read()
    {
        var value = JsLiteral.Parse("""{"run": [{"method": "shell.run"}]}""");
        Assert.Equal("shell.run", value.Field("run")!.Items[0].Field("method")!.Text);
    }

    [Fact]
    public void A_javascript_module_is_read_with_unquoted_keys_comments_and_a_trailing_comma()
    {
        var value = JsLiteral.Parse("""
            const path = require('path')
            module.exports = {
              daemon: true,
              // the array below is what matters
              run: [
                { method: 'shell.run', params: { message: "python main.py" } },
              ],
            }
            """);

        Assert.True(value.Field("daemon")!.Truthy);
        var step = value.Field("run")!.Items[0];
        Assert.Equal("python main.py", step.Field("params")!.Field("message")!.Text);
    }

    [Fact]
    public void A_function_becomes_unknown_without_losing_the_fields_around_it()
    {
        var value = JsLiteral.Parse("""
            module.exports = {
              title: "Comfyui",
              menu: async (kernel, info) => {
                let installed = info.exists("app/env")
                return [{ html: "<i>x</i>", href: "start.js" }]
              },
              version: "3.7"
            }
            """);

        Assert.Equal("Comfyui", value.Field("title")!.Text);
        Assert.IsType<JsValue.Unknown>(value.Field("menu"));
        Assert.Equal("3.7", value.Field("version")!.Text);
    }

    [Fact]
    public void A_message_is_read_whether_it_is_one_string_or_a_list()
    {
        var one = JsLiteral.Parse("""{"message": "a"}""").Field("message")!;
        var many = JsLiteral.Parse("""{"message": ["a", "b"]}""").Field("message")!;

        Assert.Equal(new[] { "a" }, one.Strings);
        Assert.Equal(new[] { "a", "b" }, many.Strings);
    }

    [Fact]
    public void An_escaped_regex_survives_the_string_reader()
    {
        var value = JsLiteral.Parse("""{"event": "/go to: +(http:\/\/[a-z]+:[0-9]+)/i"}""");
        Assert.Equal("/go to: +(http://[a-z]+:[0-9]+)/i", value.Field("event")!.Text);
    }

    [Fact]
    public void Nonsense_is_reported_rather_than_guessed_at()
    {
        Assert.Null(JsLiteral.TryParse("{ \"unterminated\": "));
        Assert.Throws<JsParseException>(() => JsLiteral.Parse("   "));
    }
}
