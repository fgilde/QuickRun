using QuickRun.Core.Config;

namespace QuickRun.Core.Tests;

public class ConfigValidatorTests
{
    private static IReadOnlyList<ValidationIssue> Check(string yaml) =>
        ConfigValidator.Validate(ConfigParser.Parse(yaml, OSKind.Linux));

    private static void AssertError(IReadOnlyList<ValidationIssue> issues, string contains)
        => Assert.Contains(issues, i => i.IsError && i.Message.Contains(contains, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_minimal_config_is_valid()
        => Assert.Empty(Check("run: ./run.sh"));

    [Fact]
    public void A_config_with_nothing_to_execute_is_an_error()
        => AssertError(Check("name: Nothing"), "nothing to run");

    [Fact]
    public void Duplicate_task_names_are_an_error()
        => AssertError(Check("tasks:\n  - name: api\n    run: a\n  - name: api\n    run: b"), "duplicate");

    [Fact]
    public void Unknown_dependsOn_target_is_an_error()
        => AssertError(Check("tasks:\n  - name: api\n    run: a\n    dependsOn: [db]"), "dependsOn");

    [Fact]
    public void Dependency_cycles_are_an_error()
    {
        var yaml = string.Join("\n",
            "tasks:",
            "  - name: a",
            "    run: x",
            "    dependsOn: [b]",
            "  - name: b",
            "    run: y",
            "    dependsOn: [a]");
        AssertError(Check(yaml), "cycle");
    }

    [Fact]
    public void A_task_depending_on_itself_is_a_cycle()
        => AssertError(Check("tasks:\n  - name: a\n    run: x\n    dependsOn: [a]"), "cycle");

    [Fact]
    public void A_valid_dependency_chain_is_not_a_cycle()
    {
        var yaml = string.Join("\n",
            "tasks:",
            "  - name: db",
            "    run: x",
            "    readyWhen: {port: 5432}",
            "  - name: api",
            "    run: y",
            "    dependsOn: [db]",
            "    readyWhen: {port: 5000}",
            "  - name: web",
            "    run: z",
            "    dependsOn: [api]");
        Assert.DoesNotContain(Check(yaml), i => i.IsError);
    }

    [Fact]
    public void A_dependency_without_readyWhen_is_a_warning()
    {
        var yaml = string.Join("\n",
            "tasks:",
            "  - name: db",
            "    run: x",
            "  - name: api",
            "    run: y",
            "    dependsOn: [db]");
        var issues = Check(yaml);
        Assert.DoesNotContain(issues, i => i.IsError);
        Assert.Contains(issues, i => !i.IsError && i.Message.Contains("readyWhen"));
    }

    [Fact]
    public void Duplicate_input_ids_are_an_error()
        => AssertError(Check("inputs:\n  - id: k\n  - id: k\nrun: a"), "duplicate");

    [Fact]
    public void A_select_input_without_options_is_an_error()
        => AssertError(Check("inputs:\n  - id: mode\n    type: select\nrun: a"), "options");

    [Fact]
    public void A_select_default_outside_its_options_is_an_error()
        => AssertError(Check("inputs:\n  - id: mode\n    type: select\n    options: [a, b]\n    default: c\nrun: x"), "default");

    [Fact]
    public void An_invalid_regex_pattern_is_an_error()
        => AssertError(Check("inputs:\n  - id: k\n    pattern: \"[unclosed\"\nrun: a"), "pattern");

    [Fact]
    public void Pattern_on_a_non_text_input_is_a_warning_not_an_error()
    {
        var issues = Check("inputs:\n  - id: k\n    type: bool\n    pattern: \"^x\"\nrun: a");
        Assert.Contains(issues, i => !i.IsError && i.Message.Contains("pattern", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(issues, i => i.IsError);
    }

    [Fact]
    public void Min_greater_than_max_is_an_error()
        => AssertError(Check("inputs:\n  - id: p\n    type: number\n    min: 10\n    max: 1\nrun: a"), "greater");

    [Fact]
    public void An_unknown_interpolation_reference_is_an_error()
        => AssertError(Check("run: ./app --key ${inputs.missing}"), "missing");

    [Fact]
    public void A_known_interpolation_reference_is_accepted()
        => Assert.Empty(Check("inputs:\n  - id: apiKey\nrun: ./app --key ${inputs.apiKey}"));

    [Fact]
    public void An_interpolation_reference_in_open_is_checked()
        => AssertError(Check("tasks:\n  - run: a\n    open: \"http://localhost:${inputs.port}\""), "port");

    [Fact]
    public void An_environment_reference_needs_no_declaration()
        => Assert.Empty(Check("run: ./app --home ${env.HOME}"));

    [Fact]
    public void An_unsupported_version_is_an_error()
        => AssertError(Check("version: 2\nrun: a"), "version");

    [Fact]
    public void An_absolute_cwd_is_an_error()
        => AssertError(Check("tasks:\n  - run: a\n    cwd: /etc"), "relative");

    [Fact]
    public void A_cwd_escaping_the_workspace_is_an_error()
        => AssertError(Check("tasks:\n  - run: a\n    cwd: ../../etc"), "outside");

    [Fact]
    public void A_normal_subdirectory_cwd_is_fine()
        => Assert.Empty(Check("tasks:\n  - run: a\n    cwd: web/frontend"));
}
