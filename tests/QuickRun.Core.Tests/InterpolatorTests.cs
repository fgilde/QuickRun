using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

public class InterpolatorTests
{
    private static InterpolationContext Ctx(params (string Key, string? Value)[] inputs) =>
        new(inputs.ToDictionary(i => i.Key, i => i.Value),
            Workspace: "/w/acme__app__main",
            RepoName: "app",
            RepoRef: "main",
            EnvLookup: name => name == "HOME" ? "/home/tester" : null);

    [Fact]
    public void Expands_an_input_reference()
        => Assert.Equal("./app --key sk-1",
            Interpolator.Expand("./app --key ${inputs.apiKey}", Ctx(("apiKey", "sk-1"))));

    [Fact]
    public void Expands_workspace_repo_name_and_ref()
        => Assert.Equal("/w/acme__app__main app main",
            Interpolator.Expand("${workspace} ${repo.name} ${repo.ref}", Ctx()));

    [Fact]
    public void Expands_an_environment_reference()
        => Assert.Equal("/home/tester/x", Interpolator.Expand("${env.HOME}/x", Ctx()));

    [Fact]
    public void A_missing_environment_variable_expands_to_empty()
        => Assert.Equal("[]", Interpolator.Expand("[${env.NOT_SET_ANYWHERE}]", Ctx()));

    [Fact]
    public void A_null_input_expands_to_empty()
        => Assert.Equal("[]", Interpolator.Expand("[${inputs.optional}]", Ctx(("optional", null))));

    [Fact]
    public void An_unknown_input_throws_and_names_the_key()
    {
        var ex = Assert.Throws<InterpolationException>(() => Interpolator.Expand("${inputs.nope}", Ctx()));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void An_unknown_namespace_throws()
        => Assert.Throws<InterpolationException>(() => Interpolator.Expand("${secrets.k}", Ctx()));

    [Fact]
    public void Text_without_placeholders_is_returned_unchanged()
        => Assert.Equal("npm run dev", Interpolator.Expand("npm run dev", Ctx()));

    [Fact]
    public void Placeholders_lists_every_reference()
        => Assert.Equal(new[] { "inputs.a", "env.B", "workspace" },
            Interpolator.Placeholders("${inputs.a} ${env.B} ${workspace}").ToArray());

    [Fact]
    public void Redact_replaces_every_secret_occurrence()
    {
        var secrets = Interpolator.Secrets(
            new Dictionary<string, string?> { ["apiKey"] = "sk-abc", ["mode"] = "dev" },
            new[] { "apiKey" });
        Assert.Equal("using *** twice: ***", Interpolator.Redact("using sk-abc twice: sk-abc", secrets));
    }

    [Fact]
    public void Redact_ignores_empty_and_very_short_secrets()
    {
        var secrets = Interpolator.Secrets(
            new Dictionary<string, string?> { ["a"] = "", ["b"] = "x" }, new[] { "a", "b" });
        Assert.Equal("keeps x intact", Interpolator.Redact("keeps x intact", secrets));
    }
}
