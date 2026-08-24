using System.Text.Json;
using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

/// <summary>
/// The published schema is what every sample's <c>yaml-language-server</c> comment points at, and
/// what gives repository owners completion in their editor. If it drifts from the parser, editors
/// start reporting valid configs as wrong - so the accepted key sets are compared directly.
/// </summary>
public class SchemaTests
{
    private static JsonDocument Schema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "schema", "quickrun.schema.json")))
            dir = dir.Parent;

        var path = Path.Combine(dir?.FullName ?? throw new FileNotFoundException("schema/ not found"),
            "schema", "quickrun.schema.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string[] Properties(JsonElement node) =>
        node.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

    private static JsonElement Definition(JsonDocument schema, string name) =>
        schema.RootElement.GetProperty("$defs").GetProperty(name);

    /// <summary>A def written as oneOf [string, object] - the object half is what carries keys.</summary>
    private static JsonElement ObjectVariant(JsonElement node) =>
        node.TryGetProperty("oneOf", out var variants)
            ? variants.EnumerateArray().First(v =>
                v.TryGetProperty("type", out var type) && type.GetString() == "object")
            : node;

    [Fact]
    public void The_schema_is_valid_json_and_declares_its_id()
    {
        using var schema = Schema();
        Assert.Equal("https://fgilde.github.io/QuickRun/quickrun.schema.json",
            schema.RootElement.GetProperty("$id").GetString());
    }

    [Fact]
    public void Top_level_keys_match_the_parser()
    {
        using var schema = Schema();
        Assert.Equal(
            ConfigParser.TopLevelKeys.OrderBy(k => k, StringComparer.Ordinal),
            Properties(schema.RootElement).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Step_keys_match_the_parser()
    {
        using var schema = Schema();
        Assert.Equal(
            ConfigParser.StepKeys.OrderBy(k => k, StringComparer.Ordinal),
            Properties(ObjectVariant(Definition(schema, "step"))).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Task_keys_match_the_parser()
    {
        using var schema = Schema();
        Assert.Equal(
            ConfigParser.TaskKeys.OrderBy(k => k, StringComparer.Ordinal),
            Properties(ObjectVariant(Definition(schema, "task"))).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Requirement_keys_match_the_parser()
    {
        using var schema = Schema();
        Assert.Equal(
            ConfigParser.RequireKeys.OrderBy(k => k, StringComparer.Ordinal),
            Properties(ObjectVariant(Definition(schema, "requirement"))).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Input_keys_match_the_parser()
    {
        using var schema = Schema();
        Assert.Equal(
            ConfigParser.InputKeys.OrderBy(k => k, StringComparer.Ordinal),
            Properties(Definition(schema, "input")).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void ReadyWhen_keys_match_the_parser()
    {
        using var schema = Schema();
        Assert.Equal(
            ConfigParser.ReadyWhenKeys.OrderBy(k => k, StringComparer.Ordinal),
            Properties(Definition(schema, "readyWhen")).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void The_input_types_match_the_enum()
    {
        using var schema = Schema();
        var declared = Definition(schema, "input")
            .GetProperty("properties").GetProperty("type").GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()!).OrderBy(v => v, StringComparer.Ordinal);

        Assert.Equal(
            Enum.GetNames<InputType>().Select(n => n.ToLowerInvariant()).OrderBy(v => v, StringComparer.Ordinal),
            declared);
    }

    [Fact]
    public void The_restart_policies_match_the_enum()
    {
        using var schema = Schema();
        var declared = ObjectVariant(Definition(schema, "task"))
            .GetProperty("properties").GetProperty("restart").GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()!).ToArray();

        Assert.Equal(new[] { "never", "onFailure" }, declared);
        Assert.Equal(Enum.GetNames<RestartPolicy>().Length, declared.Length);
    }

    [Fact]
    public void The_platform_keys_match_the_ones_the_parser_accepts()
    {
        using var schema = Schema();
        var declared = Definition(schema, "platform").GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()!).OrderBy(v => v, StringComparer.Ordinal);

        Assert.Equal(
            Enum.GetValues<OSKind>().Select(os => os.Key()).OrderBy(v => v, StringComparer.Ordinal),
            declared);
    }
}
