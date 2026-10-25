using System.Net;
using DotNet.Testcontainers.Builders;
using Xunit;

namespace App.IntegrationTests.Image;

[CollectionDefinition(Name)]
public sealed class ImageCollection : ICollectionFixture<ImageBuild>
{
    public const string Name = "Image";
}

// The one image of both halves, built once for the collection with the command README.md gives:
// `docker build -t ai-hub:$(git rev-parse --short HEAD) .` at the repository root.
public sealed class ImageBuild : IAsyncLifetime
{
    public const string Repository = "ai-hub";

    public string RepositoryRoot => Checkout.Root;
    public string Tag { get; private set; } = "";
    public CommandLine.Result Build { get; private set; } = new(-1, "", "");

    public async Task InitializeAsync()
    {
        var commit = await CommandLine.RunAsync("git", ["rev-parse", "--short", "HEAD"], RepositoryRoot);
        Tag = $"{Repository}:{commit.Stdout.Trim()}";
        Build = await CommandLine.RunAsync("docker", ["build", "-t", Tag, "."], RepositoryRoot);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[Collection(ImageCollection.Name)]
public sealed class ImageTests(ImageBuild image)
{
    private const int HttpPort = 8080;
    private const string DatabaseAlias = "db";
    private const string Database = "aihub_image_test";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);
    private static readonly string[] Excluded = [".git", ".factory", "node_modules", "bin", "obj"];

    [Fact]
    [Trait("AC", "R-011.AC-7")]
    public async Task The_readme_build_command_builds_an_image_tagged_with_the_commit()
    {
        Assert.True(File.Exists(Path.Combine(image.RepositoryRoot, "Dockerfile")), "There is no Dockerfile at the repository root.");
        Assert.True(image.Build.ExitCode == 0, $"docker build exited {image.Build.ExitCode}: {image.Build.Stderr}");

        var inspect = await CommandLine.RunAsync("docker", ["image", "inspect", image.Tag], image.RepositoryRoot);
        Assert.True(inspect.ExitCode == 0, $"No image tagged {image.Tag}: {inspect.Stderr}");

        var readme = await File.ReadAllTextAsync(Path.Combine(image.RepositoryRoot, "README.md"));
        Assert.Contains("docker build -t ai-hub:$(git rev-parse --short HEAD) .", readme);
    }

    [Fact]
    [Trait("AC", "R-011.AC-8")]
    public async Task The_running_image_answers_health_and_serves_the_front_end()
    {
        Assert.True(image.Build.ExitCode == 0, $"docker build exited {image.Build.ExitCode}: {image.Build.Stderr}");

        await using var network = new NetworkBuilder().Build();
        await network.CreateAsync();
        await using var postgres = await ContainerRuntime.StartPostgresAsync(PostgresFixture.NewPostgres()
            .WithDatabase(Database)
            .WithNetwork(network)
            .WithNetworkAliases(DatabaseAlias));
        await using var app = new ContainerBuilder(image.Tag)
            .WithNetwork(network)
            .WithEnvironment("ConnectionStrings__AppDb",
                $"Host={DatabaseAlias};Port=5432;Database={Database};Username={PostgresFixture.User};Password={PostgresFixture.Password}")
            .WithPortBinding(HttpPort, assignRandomHostPort: true)
            .Build();
        await app.StartAsync();
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{app.Hostname}:{app.GetMappedPublicPort(HttpPort)}"),
        };

        var health = await Http.UntilAnsweredAsync(client, "/health", StartTimeout);
        var root = await client.GetAsync("/");
        var rootText = await root.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Equal("text/html", root.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<div id=\"root\">", rootText);
    }

    [Fact]
    [Trait("AC", "R-011.AC-9")]
    public async Task The_dockerignore_keeps_local_files_out_of_the_build_context()
    {
        var dockerignore = Path.Combine(image.RepositoryRoot, ".dockerignore");
        Assert.True(File.Exists(dockerignore), "There is no .dockerignore at the repository root.");

        // A throwaway image of the build context itself: what the root's .dockerignore lets through.
        var probeTag = "ai-hub-context-probe:" + Guid.NewGuid().ToString("N");
        var probe = Path.Combine(Path.GetTempPath(), "r011-probe-" + Guid.NewGuid().ToString("N") + ".Dockerfile");
        await File.WriteAllTextAsync(probe, "FROM busybox:1.37\nCOPY . /context\n");
        try
        {
            var build = await CommandLine.RunAsync("docker", ["build", "-q", "-f", probe, "-t", probeTag, "."], image.RepositoryRoot);
            Assert.True(build.ExitCode == 0, $"The context probe did not build: {build.Stderr}");
            var found = await FindExcludedAsync(probeTag, "/context");
            Assert.True(found.Count == 0, "The build context holds: " + string.Join(", ", found));
        }
        finally
        {
            File.Delete(probe);
            await CommandLine.RunAsync("docker", ["image", "rm", "-f", probeTag], image.RepositoryRoot);
        }
    }

    [Fact]
    [Trait("AC", "R-011.AC-9")]
    public async Task The_runtime_image_holds_no_local_files_from_the_checkout()
    {
        Assert.True(image.Build.ExitCode == 0, $"docker build exited {image.Build.ExitCode}: {image.Build.Stderr}");

        var workingDir = await CommandLine.RunAsync("docker", ["image", "inspect", "-f", "{{.Config.WorkingDir}}", image.Tag], image.RepositoryRoot);
        var appDirectory = workingDir.Stdout.Trim();
        Assert.False(string.IsNullOrEmpty(appDirectory) || appDirectory == "/", "The image sets no working directory of its own.");

        var found = await FindExcludedAsync(image.Tag, appDirectory);
        Assert.True(found.Count == 0, "The runtime image holds: " + string.Join(", ", found));
    }

    // Every path under `directory` in the image named .git, .factory, node_modules, bin, obj or .env*.
    private async Task<IReadOnlyList<string>> FindExcludedAsync(string tag, string directory)
    {
        List<string> names = ["-name", ".env*"];
        foreach (var name in Excluded)
            names.AddRange(["-o", "-name", name]);
        var find = await CommandLine.RunAsync("docker",
            ["run", "--rm", "--user", "0", "--entrypoint", "find", tag, directory, "(", .. names, ")", "-print"],
            image.RepositoryRoot);
        Assert.True(find.ExitCode == 0, $"find failed in {tag}: {find.Stderr}");
        return find.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
