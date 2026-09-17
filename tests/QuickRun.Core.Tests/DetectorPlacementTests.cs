using QuickRun.Core.Config;
using QuickRun.Core.Detect;

namespace QuickRun.Core.Tests;

/// <summary>
/// Which of several entry points a repository is actually run by.
/// <para>
/// Detection used to answer "what kinds of entry point are here" and pick the highest-ranked kind,
/// wherever it sat. In a real repository that is not the same question: fluxer has a compose file
/// under .devcontainer - the environment it is developed in, with its databases and its message bus
/// - and one under deploy/self-hosting that starts the application. Both are compose files, the
/// first one won, and running it failed. Where a file sits says what it is for.
/// </para>
/// </summary>
public class DetectorPlacementTests
{
    private static IReadOnlyList<Candidate> Detect(FakeRepo repo) => Detector.Detect(repo.Path, OSKind.Linux);

    private static Candidate First(FakeRepo repo) => Detect(repo).First();

    [Fact]
    public void The_environment_a_project_is_developed_in_is_not_the_project()
    {
        using var repo = new FakeRepo()
            .With(".devcontainer/docker-compose.yml", "services: {db: {image: postgres}}")
            .With("deploy/self-hosting/docker-compose.yml", "services: {app: {image: app}}");

        var chosen = First(repo);

        Assert.Equal("compose", chosen.Kind);
        Assert.Equal("deploy/self-hosting", chosen.RelativeDir);

        // Still offered, because a repository whose only entry point is there should offer it.
        Assert.Contains(Detect(repo), c => c.RelativeDir == ".devcontainer");
    }

    [Theory]
    [InlineData("tests")]
    [InlineData("examples")]
    [InlineData("docs")]
    [InlineData("tools")]
    [InlineData(".github")]
    public void A_compose_file_in_a_repositorys_workshop_loses_to_the_application(string aside)
    {
        using var repo = new FakeRepo()
            .With($"{aside}/docker-compose.yml", "services: {}")
            .With("package.json", "{\"scripts\":{\"dev\":\"vite\"}}");

        var chosen = First(repo);

        Assert.Equal("npm", chosen.Kind);
        Assert.Equal("", chosen.RelativeDir);
    }

    [Fact]
    public void The_nearer_of_two_of_the_same_kind_wins()
    {
        using var repo = new FakeRepo()
            .With("docker-compose.yml", "services: {}")
            .With("deploy/docker-compose.yml", "services: {}");

        Assert.Equal("", First(repo).RelativeDir);
    }

    /// <summary>
    /// A monorepo full of crates, none of which is the way in.
    /// </summary>
    [Fact]
    public void A_rust_library_is_not_an_entry_point()
    {
        using var repo = new FakeRepo()
            .With("Cargo.toml", "[workspace]\nmembers = [\"core\", \"cli\"]\n")
            .With("core/Cargo.toml", "[package]\nname = \"core\"\n")
            .With("core/src/lib.rs", "pub fn hello() {}\n");

        // The workspace root has no package, the member has no binary: cargo run has nothing to run
        // in either of them.
        Assert.Empty(Detect(repo));
    }

    [Fact]
    public void A_crate_with_a_binary_is_one()
    {
        using var repo = new FakeRepo()
            .With("Cargo.toml", "[workspace]\nmembers = [\"cli\"]\n")
            .With("cli/Cargo.toml", "[package]\nname = \"cli\"\n")
            .With("cli/src/main.rs", "fn main() {}\n")
            .With("core/Cargo.toml", "[package]\nname = \"core\"\n")
            .With("core/src/lib.rs", "pub fn hello() {}\n");

        var chosen = Assert.Single(Detect(repo));

        Assert.Equal("cargo", chosen.Kind);
        Assert.Equal("cli", chosen.RelativeDir);
    }

    /// <summary>
    /// A repository that ships a Dockerfile is saying how it is meant to be built and run, and that
    /// used to count for nothing unless a compose file said the same thing.
    /// </summary>
    [Fact]
    public void A_dockerfile_on_its_own_is_a_way_in()
    {
        using var repo = new FakeRepo()
            .With("Dockerfile", "FROM node:22\nEXPOSE 8080\nCMD [\"node\", \"server.js\"]\n");

        var chosen = Assert.Single(Detect(repo));

        Assert.Equal("docker", chosen.Kind);
        Assert.Equal(new[] { "docker build -t quickrun-app ." }, chosen.Setup);
        Assert.Contains("-p 8080:8080", chosen.Run[0]);
        Assert.Equal(8080, chosen.Port);
    }

    [Fact]
    public void A_compose_file_beside_it_says_more_than_the_dockerfile_does()
    {
        using var repo = new FakeRepo()
            .With("Dockerfile", "FROM node:22\nEXPOSE 8080\n")
            .With("compose.yaml", "services: {app: {build: .}}");

        Assert.Equal("compose", Assert.Single(Detect(repo)).Kind);
    }

    [Fact]
    public void A_dev_server_is_what_somebody_wants_to_see_first()
    {
        using var repo = new FakeRepo()
            .With("Dockerfile", "FROM node:22\nEXPOSE 3000\n")
            .With("package.json", "{\"scripts\":{\"dev\":\"next dev\"}}");

        Assert.Equal("npm", First(repo).Kind);
    }
}
