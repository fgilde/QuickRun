using QuickRun.Core.Config;
using QuickRun.Core.Detect;

namespace QuickRun.Core.Tests;

public class DetectorTests
{
    private static IReadOnlyList<Candidate> Detect(FakeRepo repo) => Detector.Detect(repo.Path, OSKind.Linux);

    [Fact]
    public void An_empty_repository_yields_no_candidates()
    {
        using var repo = new FakeRepo();
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Docker_compose_is_detected()
    {
        using var repo = new FakeRepo().With("docker-compose.yml", "services: {}");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("compose", c.Kind);
        Assert.Equal(new[] { "docker compose up" }, c.Run);
        Assert.Empty(c.Setup);
    }

    [Fact]
    public void Compose_yaml_and_compose_yml_are_both_recognised()
    {
        using var repo = new FakeRepo().With("compose.yaml", "services: {}");
        Assert.Equal("compose", Assert.Single(Detect(repo)).Kind);
    }

    [Fact]
    public void An_npm_dev_script_is_detected_with_its_install_step()
    {
        using var repo = new FakeRepo().With("package.json", "{\"scripts\":{\"dev\":\"vite\"}}");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("npm", c.Kind);
        Assert.Equal(new[] { "npm install" }, c.Setup);
        Assert.Equal(new[] { "npm run dev" }, c.Run);
    }

    [Fact]
    public void A_lockfile_upgrades_npm_install_to_npm_ci()
    {
        using var repo = new FakeRepo()
            .With("package.json", "{\"scripts\":{\"dev\":\"vite\"}}")
            .With("package-lock.json", "{}");
        Assert.Equal(new[] { "npm ci" }, Assert.Single(Detect(repo)).Setup);
    }

    [Fact]
    public void A_pnpm_lockfile_switches_the_package_manager()
    {
        using var repo = new FakeRepo()
            .With("package.json", "{\"scripts\":{\"dev\":\"vite\"}}")
            .With("pnpm-lock.yaml", "");
        var c = Assert.Single(Detect(repo));
        Assert.Equal(new[] { "pnpm install" }, c.Setup);
        Assert.Equal(new[] { "pnpm run dev" }, c.Run);
    }

    [Fact]
    public void Start_is_used_when_there_is_no_dev_script()
    {
        using var repo = new FakeRepo().With("package.json", "{\"scripts\":{\"start\":\"node .\"}}");
        Assert.Equal(new[] { "npm run start" }, Assert.Single(Detect(repo)).Run);
    }

    [Fact]
    public void A_package_json_without_dev_or_start_yields_nothing()
    {
        using var repo = new FakeRepo().With("package.json", "{\"scripts\":{\"build\":\"tsc\"}}");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Malformed_package_json_does_not_throw()
    {
        using var repo = new FakeRepo().With("package.json", "{not json");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void An_aspire_apphost_outranks_a_plain_csproj()
    {
        using var repo = new FakeRepo()
            .With("src/AppHost/AppHost.csproj", "<Project Sdk=\"Aspire.AppHost.Sdk\"></Project>")
            .With("src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        var first = Detect(repo)[0];
        Assert.Equal("aspire", first.Kind);
        Assert.Contains("AppHost.csproj", first.Run[0]);
    }

    [Fact]
    public void A_web_csproj_is_detected_as_a_dotnet_candidate()
    {
        using var repo = new FakeRepo().With("src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("dotnet", c.Kind);
        Assert.Contains("dotnet run --project", c.Run[0]);
    }

    [Fact]
    public void An_exe_csproj_is_detected()
    {
        using var repo = new FakeRepo().With("src/Tool/Tool.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
        Assert.Equal("dotnet", Assert.Single(Detect(repo)).Kind);
    }

    [Fact]
    public void A_library_csproj_is_not_a_candidate()
    {
        using var repo = new FakeRepo().With("src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Requirements_txt_with_main_py_produces_a_venv_flow()
    {
        using var repo = new FakeRepo().With("requirements.txt", "flask").With("main.py", "");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("python", c.Kind);
        Assert.Contains("venv", c.Setup[0]);
        Assert.Contains("requirements.txt", string.Join(" ", c.Setup));
        Assert.Contains("main.py", c.Run[0]);
    }

    [Fact]
    public void A_makefile_run_target_is_detected()
    {
        using var repo = new FakeRepo().With("Makefile", "build:\n\tgcc x.c\nrun:\n\t./a.out\n");
        var c = Assert.Single(Detect(repo));
        Assert.Equal("make", c.Kind);
        Assert.Equal(new[] { "make run" }, c.Run);
    }

    [Fact]
    public void A_makefile_without_run_or_dev_targets_yields_nothing()
    {
        using var repo = new FakeRepo().With("Makefile", "build:\n\tgcc x.c\n");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Cargo_and_go_projects_are_detected()
    {
        using (var repo = new FakeRepo().With("Cargo.toml", "[package]"))
            Assert.Equal(new[] { "cargo run" }, Assert.Single(Detect(repo)).Run);

        using (var repo = new FakeRepo().With("go.mod", "module x"))
            Assert.Equal("go", Assert.Single(Detect(repo)).Kind);
    }

    [Fact]
    public void A_root_run_script_is_the_highest_ranked_candidate()
    {
        using var repo = new FakeRepo()
            .With("run.sh", "#!/bin/sh\necho hi")
            .With("docker-compose.yml", "services: {}");
        var candidates = Detect(repo);
        Assert.Equal("script", candidates[0].Kind);
        Assert.Equal(new[] { "./run.sh" }, candidates[0].Run);
    }

    [Fact]
    public void A_monorepo_yields_one_candidate_per_directory()
    {
        using var repo = new FakeRepo()
            .With("web/package.json", "{\"scripts\":{\"dev\":\"vite\"}}")
            .With("api/package.json", "{\"scripts\":{\"dev\":\"nest start\"}}");
        var candidates = Detect(repo);
        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.RelativeDir == "web");
        Assert.Contains(candidates, c => c.RelativeDir == "api");
    }

    [Fact]
    public void Ignored_directories_are_not_scanned()
    {
        using var repo = new FakeRepo().With("node_modules/pkg/package.json", "{\"scripts\":{\"dev\":\"x\"}}");
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void Candidates_are_ordered_by_descending_confidence()
    {
        using var repo = new FakeRepo()
            .With("docker-compose.yml", "services: {}")
            .With("package.json", "{\"scripts\":{\"dev\":\"vite\"}}");
        var candidates = Detect(repo);
        Assert.True(candidates[0].Confidence >= candidates[1].Confidence);
    }

    [Fact]
    public void ToYaml_produces_a_config_that_the_parser_accepts()
    {
        using var repo = new FakeRepo()
            .With("package.json", "{\"scripts\":{\"dev\":\"vite\"}}")
            .With("package-lock.json", "{}");
        var yaml = Detector.ToYaml(Assert.Single(Detect(repo)), "web-app");

        var parsed = ConfigParser.Parse(yaml, OSKind.Linux);
        Assert.Equal("web-app", parsed.Name);
        Assert.Equal(new[] { "npm ci" }, parsed.Setup.Select(s => s.Run));
        Assert.Equal("npm run dev", Assert.Single(parsed.Tasks).Run);
    }

    [Fact]
    public void ToYaml_emits_cwd_for_a_candidate_in_a_subdirectory()
    {
        using var repo = new FakeRepo().With("web/package.json", "{\"scripts\":{\"dev\":\"vite\"}}");
        var parsed = ConfigParser.Parse(Detector.ToYaml(Assert.Single(Detect(repo)), null), OSKind.Linux);
        Assert.Equal("web", parsed.Tasks[0].Cwd);
        Assert.Equal("web", parsed.Setup[0].Cwd);
    }

    [Fact]
    public void ToYaml_output_passes_validation()
    {
        using var repo = new FakeRepo().With("docker-compose.yml", "services: {}");
        var yaml = Detector.ToYaml(Assert.Single(Detect(repo)), "stack");
        Assert.DoesNotContain(ConfigValidator.Validate(ConfigParser.Parse(yaml, OSKind.Linux)), i => i.IsError);
    }
}
