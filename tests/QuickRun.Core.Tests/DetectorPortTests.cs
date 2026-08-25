using QuickRun.Core.Config;
using QuickRun.Core.Detect;

namespace QuickRun.Core.Tests;

/// <summary>
/// What the detector works out about an address, and the formats a repository uses to say how it
/// starts. Without a port a detected run offers no link and nothing to wait for, which is the
/// difference between "it started something" and "it is running here".
/// </summary>
public class DetectorPortTests
{
    private static Candidate First(FakeRepo repo, OSKind os = OSKind.Linux) =>
        Detector.Detect(repo.Path, os).First();

    [Fact]
    public void A_vite_project_gets_vites_port()
    {
        using var repo = new FakeRepo().With("package.json",
            """{"scripts": {"dev": "vite"}, "devDependencies": {"vite": "^5"}}""");
        Assert.Equal(5173, First(repo).Port);
    }

    [Fact]
    public void A_port_in_the_script_beats_the_framework_default()
    {
        using var repo = new FakeRepo().With("package.json",
            """{"scripts": {"dev": "vite --port 8080"}, "devDependencies": {"vite": "^5"}}""");
        Assert.Equal(8080, First(repo).Port);
    }

    [Fact]
    public void A_next_project_gets_three_thousand()
    {
        using var repo = new FakeRepo().With("package.json",
            """{"scripts": {"dev": "next dev"}, "dependencies": {"next": "^14"}}""");
        Assert.Equal(3000, First(repo).Port);
    }

    [Fact]
    public void A_plain_node_script_has_no_port_to_guess()
    {
        using var repo = new FakeRepo().With("package.json", """{"scripts": {"start": "node index.js"}}""");
        Assert.Null(First(repo).Port);
    }

    [Fact]
    public void Compose_takes_the_first_published_port()
    {
        using var repo = new FakeRepo().With("docker-compose.yml", """
            services:
              db:
                image: postgres
              web:
                ports:
                  - "7861:7860"
            """);
        Assert.Equal(7861, First(repo).Port);
    }

    [Fact]
    public void A_gradio_app_gets_the_port_gradio_uses()
    {
        using var repo = new FakeRepo().With("app.py", "").With("requirements.txt", "gradio==4.44.0\ntorch\n");
        var candidate = First(repo);
        Assert.Equal(7860, candidate.Port);
        Assert.Equal(new[] { ".venv/bin/python app.py" }, candidate.Run);
    }

    [Fact]
    public void A_streamlit_app_is_started_through_streamlit()
    {
        using var repo = new FakeRepo().With("app.py", "").With("requirements.txt", "streamlit\n");
        var candidate = First(repo);
        Assert.Equal(8501, candidate.Port);
        Assert.Equal(new[] { ".venv/bin/python -m streamlit run app.py" }, candidate.Run);
    }

    /// <summary>manage.py on its own prints Django's help; runserver is what serves.</summary>
    [Fact]
    public void A_django_project_actually_serves()
    {
        using var repo = new FakeRepo().With("manage.py", "").With("requirements.txt", "django\n");
        var candidate = First(repo);
        Assert.Equal(8000, candidate.Port);
        Assert.Equal(new[] { ".venv/bin/python manage.py runserver" }, candidate.Run);
    }

    [Fact]
    public void A_web_project_gets_the_port_from_its_launch_settings()
    {
        using var repo = new FakeRepo()
            .With("Web/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>")
            .With("Web/Properties/launchSettings.json", """
                {"profiles": {"http": {"applicationUrl": "http://localhost:5241"}}}
                """);

        var candidate = First(repo);
        Assert.Equal(5241, candidate.Port);
        Assert.Equal(new[] { "dotnet run --project Web/Web.csproj" }, candidate.Run);
    }

    [Fact]
    public void A_web_project_without_launch_settings_is_pinned_to_a_known_port()
    {
        using var repo = new FakeRepo().With("Web/Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");

        var candidate = First(repo);
        Assert.Equal(5000, candidate.Port);
        Assert.Equal(new[] { "dotnet run --project Web/Web.csproj --urls http://localhost:5000" }, candidate.Run);
    }

    [Fact]
    public void A_test_project_is_not_something_to_start()
    {
        using var repo = new FakeRepo()
            .With("App.Tests/App.Tests.csproj", "<Project><OutputType>Exe</OutputType></Project>");
        Assert.Empty(Detector.Detect(repo.Path, OSKind.Linux));
    }

    [Fact]
    public void A_procfile_becomes_its_processes_with_the_web_one_first()
    {
        using var repo = new FakeRepo().With("Procfile", """
            release: bundle exec rake db:migrate
            worker: bundle exec sidekiq
            web: bundle exec puma -p $PORT
            """);

        var candidate = First(repo);
        Assert.Equal("procfile", candidate.Kind);
        Assert.Equal(new[] { "bundle exec puma -p 8080", "bundle exec sidekiq" }, candidate.Run);
        Assert.Equal(8080, candidate.Port);
    }

    [Fact]
    public void A_replit_run_line_is_taken_as_written()
    {
        using var repo = new FakeRepo().With(".replit", """
            language = "python3"
            run = "python main.py --port 4000"
            """);

        var candidate = First(repo);
        Assert.Equal("replit", candidate.Kind);
        Assert.Equal(new[] { "python main.py --port 4000" }, candidate.Run);
        Assert.Equal(4000, candidate.Port);
    }

    [Fact]
    public void A_taskfile_target_is_detected()
    {
        using var repo = new FakeRepo().With("Taskfile.yml", """
            version: '3'
            tasks:
              build:
                cmds: [go build ./...]
              dev:
                cmds: [go run .]
            """);

        Assert.Equal(new[] { "task dev" }, First(repo).Run);
    }

    [Fact]
    public void A_justfile_target_is_detected()
    {
        using var repo = new FakeRepo().With("justfile", """
            build:
                cargo build

            run port="3000":
                cargo run
            """);

        Assert.Equal(new[] { "just run" }, First(repo).Run);
    }

    /// <summary>The generated config has to be the config a person would have written.</summary>
    [Fact]
    public void A_detected_port_becomes_a_readiness_check_and_an_open()
    {
        using var repo = new FakeRepo().With("package.json",
            """{"scripts": {"dev": "vite"}, "devDependencies": {"vite": "^5"}}""");

        var yaml = Detector.ToYaml(First(repo), "demo");
        Assert.Contains("readyWhen: {http: \"http://localhost:5173\"}", yaml);
        Assert.Contains("open: true", yaml);

        var config = ConfigParser.Parse(yaml, OSKind.Linux);
        Assert.Equal("http://localhost:5173", Assert.Single(config.Tasks).ReadyWhen!.Http);
        Assert.True(Assert.Single(config.Tasks).OpenReady);
        Assert.DoesNotContain(ConfigValidator.Validate(config), i => i.IsError);
    }
}
