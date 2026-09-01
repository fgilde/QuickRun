using QuickRun.Core.Config;
using QuickRun.Core.Detect;

namespace QuickRun.Core.Tests;

/// <summary>
/// The fields a detected compose run asks for.
/// <para>
/// The point is not to collect every variable a compose file mentions - it is to collect the ones
/// that would otherwise arrive empty. A placeholder with a default is answered by compose itself,
/// and asking about it turns a two-field form into a wall of prefilled boxes nobody reads.
/// </para>
/// </summary>
public class ComposeVariablesTests
{
    [Fact]
    public void A_placeholder_without_a_default_is_asked_for()
    {
        var asked = ComposeVariables.In("""
            services:
              db:
                image: postgres:16
                environment:
                  POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
            """);

        var one = Assert.Single(asked);
        Assert.Equal("POSTGRES_PASSWORD", one.Name);
        Assert.Null(one.Default);

        // And it is not typed into a log or a terminal in the clear.
        Assert.True(one.Secret);
    }

    /// <summary>
    /// Compose's own defaults are left to compose, in both spellings.
    /// </summary>
    [Theory]
    [InlineData("${TAG:-latest}")]
    [InlineData("${TAG-latest}")]
    public void A_placeholder_with_a_default_is_not_asked_for(string placeholder)
    {
        Assert.Empty(ComposeVariables.In($"services:\n  app:\n    image: app:{placeholder}\n"));
    }

    /// <summary>
    /// <c>:?</c> is compose refusing to start without a value, which is the strongest possible
    /// reason to ask - the text after it is a message, not a value.
    /// </summary>
    [Theory]
    [InlineData("${API_KEY:?set this first}")]
    [InlineData("${API_KEY?set this first}")]
    public void A_placeholder_compose_insists_on_is_asked_for(string placeholder)
    {
        var one = Assert.Single(ComposeVariables.In($"services:\n  app:\n    image: {placeholder}\n"));

        Assert.Equal("API_KEY", one.Name);
        Assert.Null(one.Default);
    }

    /// <summary>
    /// An escaped dollar never reaches compose's interpolation, so it is not a question.
    /// <para>
    /// This is how a compose file passes a variable through to the container's own shell. Asking
    /// about it would be asking the user to fill in something that is not theirs to fill in.
    /// </para>
    /// </summary>
    [Fact]
    public void An_escaped_dollar_is_not_a_question()
    {
        Assert.Empty(ComposeVariables.In("""
            services:
              app:
                command: sh -c 'echo $${HOSTNAME}'
            """));
    }

    [Fact]
    public void The_repositorys_own_example_fills_the_field_in()
    {
        var asked = ComposeVariables.In(
            compose: "services:\n  db:\n    environment:\n      PGPASS: ${POSTGRES_PASSWORD}\n",
            example: "# the one to copy\nPOSTGRES_PASSWORD=change-me   # not for production\n");

        var one = Assert.Single(asked);

        // The value from the file, without the comment that followed it on the line.
        Assert.Equal("change-me", one.Default);
    }

    [Fact]
    public void A_variable_an_env_file_already_sets_is_not_asked_for()
    {
        var asked = ComposeVariables.In(
            compose: "services:\n  db:\n    environment:\n      A: ${SET_ALREADY}\n      B: ${NOT_SET}\n",
            example: null,
            present: new HashSet<string> { "SET_ALREADY" });

        Assert.Equal("NOT_SET", Assert.Single(asked).Name);
    }

    [Fact]
    public void The_empty_fields_come_first()
    {
        var asked = ComposeVariables.In(
            compose: "x: ${SUGGESTED} ${NOTHING_KNOWN}\n",
            example: "SUGGESTED=here\n");

        // Ordered by what the person has to do, not alphabetically: the field with nothing in it is
        // the one the run is waiting on.
        Assert.Equal(new[] { "NOTHING_KNOWN", "SUGGESTED" }, asked.Select(v => v.Name));
    }

    [Fact]
    public void A_compose_file_that_asks_for_everything_is_capped()
    {
        var many = string.Join(" ", Enumerable.Range(0, ComposeVariables.Most + 10).Select(i => $"${{VAR_{i:D3}}}"));

        Assert.Equal(ComposeVariables.Most, ComposeVariables.In($"x: {many}\n").Count);
    }

    [Fact]
    public void A_repeated_placeholder_is_one_field()
    {
        var asked = ComposeVariables.In("""
            services:
              db:
                environment:
                  POSTGRES_PASSWORD: ${DB_PASS}
              app:
                environment:
                  DATABASE_URL: postgres://user:${DB_PASS}@db/app
            """);

        Assert.Equal("DB_PASS", Assert.Single(asked).Name);
    }

    [Theory]
    [InlineData("A=plain", "plain")]
    [InlineData("A=\"quoted value\"", "quoted value")]
    [InlineData("A='single'", "single")]
    [InlineData("export A=exported", "exported")]
    [InlineData("A=", "")]
    [InlineData("A=\"\"", "")]
    public void An_env_file_is_read_the_way_a_shell_reads_it(string line, string expected)
    {
        Assert.Equal(expected, ComposeVariables.Values(line)["A"]);
    }

    /// <summary>
    /// End to end: a directory with a compose file and an example becomes a config that asks.
    /// <para>
    /// Parsed back through the real parser, because the detector writes text and a generated config
    /// that does not parse is worse than none - it fails after the user has filled the form in.
    /// </para>
    /// </summary>
    [Fact]
    public void A_detected_compose_run_becomes_a_config_that_asks()
    {
        var dir = Directory.CreateTempSubdirectory("quickrun-compose-vars").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
                services:
                  db:
                    image: postgres:16
                    environment:
                      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
                      POSTGRES_DB: ${POSTGRES_DB}
                  app:
                    image: app:${TAG:-latest}
                    ports:
                      - "8080:8080"
                """);

            File.WriteAllText(Path.Combine(dir, ".env.example"), "POSTGRES_DB=app\n");

            var compose = Detector.Detect(dir, OSKinds.Current).Single(c => c.Kind == "compose");
            var config = ConfigParser.Parse(Detector.ToYaml(compose, "demo"), OSKinds.Current);

            Assert.DoesNotContain(ConfigValidator.Validate(config), i => i.IsError);

            var password = config.Inputs.Single(i => i.Id == "POSTGRES_PASSWORD");
            var database = config.Inputs.Single(i => i.Id == "POSTGRES_DB");

            // TAG has a default in the compose file, so compose answers it and the form does not.
            Assert.Equal(2, config.Inputs.Count);

            Assert.Equal(InputType.Password, password.Type);
            Assert.True(password.Required);
            Assert.Equal("app", database.Default);
            Assert.False(database.Required);

            // Each one exported under its own name - which is the only reason compose ever sees it.
            Assert.Equal("POSTGRES_PASSWORD", password.Env);
            Assert.Equal("POSTGRES_DB", database.Env);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// A compose file with nothing to ask about generates what it always did.
    /// <para>
    /// The case that must not have changed: the detector's output for every repository whose compose
    /// file interpolates nothing, which is most of them.
    /// </para>
    /// </summary>
    [Fact]
    public void A_compose_file_with_no_placeholders_asks_nothing()
    {
        var dir = Directory.CreateTempSubdirectory("quickrun-compose-plain").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "compose.yml"),
                "services:\n  app:\n    image: app:1\n    ports:\n      - \"3000:3000\"\n");

            var compose = Detector.Detect(dir, OSKinds.Current).Single(c => c.Kind == "compose");
            var yaml = Detector.ToYaml(compose, "demo");

            Assert.DoesNotContain("inputs:", yaml);
            Assert.Empty(ConfigParser.Parse(yaml, OSKinds.Current).Inputs);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
