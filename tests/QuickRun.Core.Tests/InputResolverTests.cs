using QuickRun.Core.Config;
using QuickRun.Core.Inputs;

namespace QuickRun.Core.Tests;

public class InputResolverTests
{
    private static InputDef Def(string id, InputType type = InputType.Text, bool required = false,
        string? def = null, string? pattern = null, double? min = null, double? max = null,
        string[]? options = null, string? env = null) =>
        new(id, null, type, null, def, required, pattern, min, max,
            (options ?? Array.Empty<string>()).Select(o => new InputOption(o, null)).ToList(), env, false);

    private static Dictionary<string, string?> Values(params (string, string?)[] v) =>
        v.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void ApplyDefaults_fills_missing_values()
        => Assert.Equal("dev", InputResolver.ApplyDefaults(new[] { Def("mode", def: "dev") }, Values())["mode"]);

    [Fact]
    public void ApplyDefaults_does_not_overwrite_a_provided_value()
        => Assert.Equal("prod",
            InputResolver.ApplyDefaults(new[] { Def("mode", def: "dev") }, Values(("mode", "prod")))["mode"]);

    [Fact]
    public void A_missing_required_value_is_an_error()
    {
        var errors = InputResolver.Validate(new[] { Def("apiKey", required: true) }, Values());
        Assert.Equal("apiKey", Assert.Single(errors).Id);
    }

    [Fact]
    public void An_empty_string_does_not_satisfy_required()
        => Assert.Single(InputResolver.Validate(new[] { Def("apiKey", required: true) }, Values(("apiKey", "  "))));

    [Fact]
    public void A_missing_optional_value_is_fine()
        => Assert.Empty(InputResolver.Validate(new[] { Def("note") }, Values()));

    [Fact]
    public void A_value_failing_the_pattern_is_an_error()
        => Assert.Single(InputResolver.Validate(new[] { Def("k", pattern: "^sk-") }, Values(("k", "nope"))));

    [Fact]
    public void A_value_matching_the_pattern_is_accepted()
        => Assert.Empty(InputResolver.Validate(new[] { Def("k", pattern: "^sk-") }, Values(("k", "sk-1"))));

    [Fact]
    public void A_non_numeric_value_for_a_number_input_is_an_error()
        => Assert.Single(InputResolver.Validate(new[] { Def("port", InputType.Number) }, Values(("port", "abc"))));

    [Fact]
    public void A_number_outside_min_max_is_an_error()
        => Assert.Single(InputResolver.Validate(
            new[] { Def("port", InputType.Number, min: 1, max: 65535) }, Values(("port", "70000"))));

    [Fact]
    public void A_number_inside_min_max_is_accepted()
        => Assert.Empty(InputResolver.Validate(
            new[] { Def("port", InputType.Number, min: 1, max: 65535) }, Values(("port", "3000"))));

    [Fact]
    public void A_non_boolean_value_for_a_bool_input_is_an_error()
        => Assert.Single(InputResolver.Validate(new[] { Def("flag", InputType.Bool) }, Values(("flag", "maybe"))));

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("True")]
    public void Boolean_values_are_accepted_case_insensitively(string value)
        => Assert.Empty(InputResolver.Validate(new[] { Def("flag", InputType.Bool) }, Values(("flag", value))));

    [Fact]
    public void A_select_value_outside_its_options_is_an_error()
        => Assert.Single(InputResolver.Validate(
            new[] { Def("mode", InputType.Select, options: new[] { "dev", "prod" }) }, Values(("mode", "staging"))));

    [Fact]
    public void ToEnv_maps_only_inputs_that_declare_env()
    {
        var env = InputResolver.ToEnv(
            new[] { Def("apiKey", env: "OPENAI_API_KEY"), Def("note") },
            Values(("apiKey", "sk-1"), ("note", "hello")));
        Assert.Equal("sk-1", env["OPENAI_API_KEY"]);
        Assert.Single(env);
    }

    [Fact]
    public void ToEnv_skips_null_values()
        => Assert.Empty(InputResolver.ToEnv(new[] { Def("k", env: "K") }, Values(("k", null))));

    [Fact]
    public void SecretIds_lists_password_inputs()
        => Assert.Equal(new[] { "pw" },
            InputResolver.SecretIds(new[] { Def("pw", InputType.Password), Def("plain") }));

    [Fact]
    public void ParseAssignments_splits_on_the_first_equals_sign()
    {
        var parsed = InputResolver.ParseAssignments(new[] { "apiKey=sk-a=b", "mode=dev" });
        Assert.Equal("sk-a=b", parsed["apiKey"]);
        Assert.Equal("dev", parsed["mode"]);
    }

    [Fact]
    public void ParseAssignments_rejects_a_value_without_an_equals_sign()
        => Assert.Throws<ArgumentException>(() => InputResolver.ParseAssignments(new[] { "apiKey" }));

    [Fact]
    public void ParseAssignments_accepts_an_empty_value()
        => Assert.Equal("", InputResolver.ParseAssignments(new[] { "note=" })["note"]);
}
