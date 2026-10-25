using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using App.IntegrationTests.Checks;
using App.IntegrationTests.Image;
using Xunit;

namespace App.IntegrationTests.Deploy;

// The deploy script (devtools/deploy.py) run as a process against a Compose project, port and environment
// file made for one test alone (`--project`), so the real `ai-hub-demo` is never touched.
public sealed class DemoProject : IAsyncDisposable
{
    public const string Script = "devtools/deploy.py";
    public const string TestDatabase = "aihub_deploy_test";
    private const string ProjectLabel = "com.docker.compose.project";
    private const string ServiceLabel = "com.docker.compose.service";

    public string Name { get; } = "r014-" + Guid.NewGuid().ToString("N")[..12];
    public string EnvFile { get; } = Path.Combine(Path.GetTempPath(), "r014-" + Guid.NewGuid().ToString("N") + ".env");
    public int Port { get; } = ComposeProject.FreePort();
    public IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();

    public static async Task<DemoProject> CreateAsync()
    {
        var project = new DemoProject();
        var values = ComposeProject.FilledExample(new Dictionary<string, string>
        {
            ["AI_HUB_PORT"] = project.Port.ToString(),
            ["POSTGRES_DB"] = TestDatabase,
        });
        project.Values = new Dictionary<string, string>(values);
        await File.WriteAllLinesAsync(project.EnvFile, values.Select(pair => $"{pair.Key}={pair.Value}"));
        return project;
    }

    public Task<Output> DeployAsync(string commit, TimeSpan? wait = null) =>
        RunScriptAsync([commit, EnvFile, .. WaitArguments(wait)]);

    public async Task<Output> RunScriptAsync(IEnumerable<string> arguments)
    {
        Assert.True(File.Exists(Path.Combine(Checkout.Root, Script)), $"There is no deploy script at {Script}.");
        var result = await CommandLine.RunAsync(Python.Path, [Script, "--project", Name, .. arguments], Checkout.Root);
        return new Output(result);
    }

    // The container Compose runs for a service in this project: "<id>|<image>", or "" when none runs.
    public async Task<string> RunningAsync(string service)
    {
        var ps = await CommandLine.RunAsync("docker",
            ["ps", "--filter", $"label={ProjectLabel}={Name}", "--filter", $"label={ServiceLabel}={service}", "--format", "{{.ID}}|{{.Image}}"],
            Checkout.Root);
        Assert.True(ps.ExitCode == 0, $"docker ps failed: {ps.Stderr}");
        return ps.Stdout.Trim();
    }

    public async Task<string> AppImageAsync()
    {
        var running = await RunningAsync("app");
        return running.Length == 0 ? "" : running[(running.IndexOf('|') + 1)..];
    }

    public async Task<string[]> ContainersAsync()
    {
        var ps = await CommandLine.RunAsync("docker", ["ps", "-a", "-q", "--filter", $"label={ProjectLabel}={Name}"], Checkout.Root);
        return ps.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<string[]> VolumesAsync()
    {
        var ls = await CommandLine.RunAsync("docker", ["volume", "ls", "-q", "--filter", $"label={ProjectLabel}={Name}"], Checkout.Root);
        return ls.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<HttpStatusCode> HealthAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{Port}"), Timeout = TimeSpan.FromSeconds(10) };
        var health = await Http.UntilAnsweredAsync(client, "/health", TimeSpan.FromSeconds(30));
        return health.StatusCode;
    }

    public async Task<CommandLine.Result> PsqlAsync(string sql)
    {
        var db = await RunningAsync("db");
        Assert.True(db.Length > 0, "No database container runs in the project.");
        return await CommandLine.RunAsync("docker",
            ["exec", db[..db.IndexOf('|')], "psql", "-U", Values["POSTGRES_USER"], "-d", Values["POSTGRES_DB"], "-v", "ON_ERROR_STOP=1", "-tAc", sql],
            Checkout.Root);
    }

    public async ValueTask DisposeAsync()
    {
        await CommandLine.RunAsync("docker",
            ["compose", "-p", Name, "-f", ComposeProject.ComposeFile, "--env-file", EnvFile, "down", "-v", "--remove-orphans"],
            Checkout.Root);
        File.Delete(EnvFile);
    }

    private static string[] WaitArguments(TimeSpan? wait) =>
        wait is null ? [] : ["--wait", ((int)wait.Value.TotalSeconds).ToString()];

    public sealed record Output(CommandLine.Result Result)
    {
        public string Text => Result.Stdout + "\n" + Result.Stderr;

        // The last line the script wrote, on either stream (a failure may be reported on stderr).
        public string[] LastLines { get; } =
        [
            LastLine(Result.Stdout),
            LastLine(Result.Stderr),
        ];

        public string Describe() => $"exit {Result.ExitCode}\nstdout:\n{Result.Stdout}\nstderr:\n{Result.Stderr}";

        private static string LastLine(string text) =>
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
    }
}

// Commits made for a test: HEAD's tree with its Dockerfile changed, written as an unreferenced commit
// object (no branch moves, the working tree and index are not touched).
public static class ProbeCommit
{
    public static async Task<string> HeadAsync() => await GitAsync(["rev-parse", "--short", "HEAD"]);

    // An image that builds and starts but never answers /health.
    public static Task<string> NeverHealthyAsync() => WithDockerfileAsync(dockerfile => dockerfile + "\nENTRYPOINT [\"sleep\", \"infinity\"]\n");

    // An image that fails to build.
    public static Task<string> BrokenBuildAsync() => WithDockerfileAsync(dockerfile => dockerfile + "\nRUN exit 1\n");

    // The same image as HEAD under a tag of its own (only a comment differs).
    public static Task<string> HealthyAsync() => WithDockerfileAsync(dockerfile => dockerfile + $"\n# r014 probe {Guid.NewGuid():N}\n");

    public static Task RemoveImageAsync(string commit) =>
        CommandLine.RunAsync("docker", ["image", "rm", "-f", $"{ImageBuild.Repository}:{commit}"], Checkout.Root);

    private static async Task<string> WithDockerfileAsync(Func<string, string> change)
    {
        var dockerfile = (await GitAsync(["show", "HEAD:Dockerfile"])).Replace("\r\n", "\n");
        var blobFile = Path.Combine(Path.GetTempPath(), "r014-" + Guid.NewGuid().ToString("N") + ".Dockerfile");
        var index = Path.Combine(Path.GetTempPath(), "r014-" + Guid.NewGuid().ToString("N") + ".index");
        await File.WriteAllTextAsync(blobFile, change(dockerfile));
        try
        {
            var blob = await GitAsync(["hash-object", "-w", "--no-filters", blobFile]);
            var withIndex = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = index };
            await GitAsync(["read-tree", "HEAD"], withIndex);
            await GitAsync(["update-index", "--cacheinfo", $"100644,{blob},Dockerfile"], withIndex);
            var tree = await GitAsync(["write-tree"], withIndex);
            var commit = await GitAsync(["commit-tree", tree, "-p", "HEAD", "-m", "r014 probe"], new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = "r014 probe", ["GIT_AUTHOR_EMAIL"] = "probe@example.invalid",
                ["GIT_COMMITTER_NAME"] = "r014 probe", ["GIT_COMMITTER_EMAIL"] = "probe@example.invalid",
            });
            return await GitAsync(["rev-parse", "--short", commit]);
        }
        finally
        {
            File.Delete(blobFile);
            File.Delete(index);
        }
    }

    private static async Task<string> GitAsync(IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Checkout.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', start.ArgumentList)} failed: {await stderr}");
        return (await stdout).Trim();
    }
}

public sealed partial class DeployTests
{
    // Long enough for a never-healthy image to fail and the previous one to come back healthy.
    private static readonly TimeSpan FailingWait = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("AC", "R-014.AC-1")]
    [Trait("AC", "R-014.AC-7")]
    public async Task A_healthy_commit_is_built_started_as_its_tag_and_exits_zero_once_health_answers()
    {
        var commit = await ProbeCommit.HeadAsync();
        await using var demo = await DemoProject.CreateAsync();

        var output = await demo.DeployAsync(commit);

        Assert.True(output.Result.ExitCode == 0, output.Describe());
        var inspect = await CommandLine.RunAsync("docker", ["image", "inspect", $"{ImageBuild.Repository}:{commit}"], Checkout.Root);
        Assert.True(inspect.ExitCode == 0, $"No image tagged {ImageBuild.Repository}:{commit}: {inspect.Stderr}");
        Assert.Equal($"{ImageBuild.Repository}:{commit}", await demo.AppImageAsync());
        Assert.Equal(HttpStatusCode.OK, await demo.HealthAsync());
        AssertNoPathOrSecret(output, demo);
    }

    [Fact]
    [Trait("AC", "R-014.AC-2")]
    [Trait("AC", "R-014.AC-4")]
    [Trait("AC", "R-014.AC-7")]
    public async Task A_never_healthy_commit_fails_restores_the_previous_image_and_keeps_the_data()
    {
        var head = await ProbeCommit.HeadAsync();
        var previous = await ProbeCommit.HealthyAsync();
        var broken = await ProbeCommit.NeverHealthyAsync();
        await using var demo = await DemoProject.CreateAsync();
        try
        {
            var first = await demo.DeployAsync(head);
            Assert.True(first.Result.ExitCode == 0, first.Describe());
            var marker = "r014-" + Guid.NewGuid().ToString("N");
            var write = await demo.PsqlAsync($"CREATE TABLE r014_probe (note text); INSERT INTO r014_probe VALUES ('{marker}');");
            Assert.True(write.ExitCode == 0, $"Writing the row failed: {write.Stderr}");

            var second = await demo.DeployAsync(previous);
            Assert.True(second.Result.ExitCode == 0, second.Describe());
            Assert.Equal($"{ImageBuild.Repository}:{previous}", await demo.AppImageAsync());
            await AssertRowSurvivesAsync(demo, marker);

            var failed = await demo.DeployAsync(broken, FailingWait);

            Assert.True(failed.Result.ExitCode != 0, failed.Describe());
            Assert.True(failed.LastLines.Any(line => line.Contains("/health")), "The last line does not name the /health failure.\n" + failed.Describe());
            Assert.Equal($"{ImageBuild.Repository}:{previous}", await demo.AppImageAsync());
            Assert.Equal(HttpStatusCode.OK, await demo.HealthAsync());
            await AssertRowSurvivesAsync(demo, marker);
            AssertNoPathOrSecret(first, demo);
            AssertNoPathOrSecret(failed, demo);
        }
        finally
        {
            await ProbeCommit.RemoveImageAsync(previous);
            await ProbeCommit.RemoveImageAsync(broken);
        }
    }

    [Fact]
    [Trait("AC", "R-014.AC-3")]
    [Trait("AC", "R-014.AC-7")]
    public async Task A_never_healthy_first_deploy_fails_and_says_there_is_no_previous_image()
    {
        var broken = await ProbeCommit.NeverHealthyAsync();
        await using var demo = await DemoProject.CreateAsync();
        try
        {
            var output = await demo.DeployAsync(broken, FailingWait);

            Assert.True(output.Result.ExitCode != 0, output.Describe());
            Assert.Contains("/health", output.Text);
            Assert.Contains("no previous image", output.Text, StringComparison.OrdinalIgnoreCase);
            AssertNoPathOrSecret(output, demo);
        }
        finally
        {
            await ProbeCommit.RemoveImageAsync(broken);
        }
    }

    [Fact]
    [Trait("AC", "R-014.AC-5")]
    public async Task A_commit_that_cannot_be_built_fails_at_the_build_and_leaves_the_running_deploy_alone()
    {
        var head = await ProbeCommit.HeadAsync();
        var broken = await ProbeCommit.BrokenBuildAsync();
        await using var demo = await DemoProject.CreateAsync();
        try
        {
            var first = await demo.DeployAsync(head);
            Assert.True(first.Result.ExitCode == 0, first.Describe());
            var app = await demo.RunningAsync("app");
            var db = await demo.RunningAsync("db");

            foreach (var commit in new[] { broken, "0000000deadbeef" })
            {
                var output = await demo.DeployAsync(commit);

                Assert.True(output.Result.ExitCode != 0, output.Describe());
                Assert.True(output.LastLines.Any(line => line.Contains("build", StringComparison.OrdinalIgnoreCase)),
                    $"The last line does not name the build for {commit}.\n" + output.Describe());
                Assert.Equal(app, await demo.RunningAsync("app"));
                Assert.Equal(db, await demo.RunningAsync("db"));
            }
            Assert.Equal(HttpStatusCode.OK, await demo.HealthAsync());
        }
        finally
        {
            await ProbeCommit.RemoveImageAsync(broken);
        }
    }

    [Fact]
    [Trait("AC", "R-014.AC-6")]
    public async Task No_commit_is_refused_naming_the_commit()
    {
        await using var demo = await DemoProject.CreateAsync();

        var output = await demo.RunScriptAsync([]);

        Assert.True(output.Result.ExitCode != 0, output.Describe());
        Assert.Contains("commit", output.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await demo.ContainersAsync());
    }

    [Fact]
    [Trait("AC", "R-014.AC-6")]
    public async Task No_environment_file_is_refused_naming_it_before_any_build()
    {
        var commit = await ProbeCommit.NeverHealthyAsync();
        await using var demo = await DemoProject.CreateAsync();
        try
        {
            var output = await demo.RunScriptAsync([commit]);

            Assert.True(output.Result.ExitCode != 0, output.Describe());
            Assert.Matches(EnvironmentFile(), output.Text);
            await AssertNothingBuiltOrStartedAsync(demo, commit);
        }
        finally
        {
            await ProbeCommit.RemoveImageAsync(commit);
        }
    }

    [Fact]
    [Trait("AC", "R-014.AC-6")]
    [Trait("AC", "R-014.AC-7")]
    public async Task An_environment_file_that_does_not_exist_is_refused_without_printing_its_path()
    {
        var commit = await ProbeCommit.NeverHealthyAsync();
        await using var demo = await DemoProject.CreateAsync();
        var missing = Path.Combine(Path.GetTempPath(), "r014-missing-" + Guid.NewGuid().ToString("N") + ".env");
        try
        {
            var output = await demo.RunScriptAsync([commit, missing]);

            Assert.True(output.Result.ExitCode != 0, output.Describe());
            Assert.Matches(EnvironmentFile(), output.Text);
            Assert.DoesNotContain(Path.GetFileName(missing), output.Text);
            await AssertNothingBuiltOrStartedAsync(demo, commit);
        }
        finally
        {
            await ProbeCommit.RemoveImageAsync(commit);
        }
    }

    private static async Task AssertRowSurvivesAsync(DemoProject demo, string marker)
    {
        Assert.Contains(await demo.VolumesAsync(), volume => volume.EndsWith("ai-hub-data", StringComparison.Ordinal));
        var read = await demo.PsqlAsync("SELECT note FROM r014_probe;");
        Assert.True(read.ExitCode == 0, $"Reading the row failed: {read.Stderr}");
        Assert.Equal(marker, read.Stdout.Trim());
    }

    private static async Task AssertNothingBuiltOrStartedAsync(DemoProject demo, string commit)
    {
        var inspect = await CommandLine.RunAsync("docker", ["image", "inspect", $"{ImageBuild.Repository}:{commit}"], Checkout.Root);
        Assert.True(inspect.ExitCode != 0, $"The image {ImageBuild.Repository}:{commit} was built.");
        Assert.Empty(await demo.ContainersAsync());
    }

    // FSD §7: neither the environment file's path nor its password reaches the output.
    private static void AssertNoPathOrSecret(DemoProject.Output output, DemoProject demo)
    {
        Assert.DoesNotContain(Path.GetFileName(demo.EnvFile), output.Text);
        Assert.DoesNotContain(demo.Values["POSTGRES_PASSWORD"], output.Text);
    }

    [GeneratedRegex(@"env(ironment)?[ _-]?file", RegexOptions.IgnoreCase)]
    private static partial Regex EnvironmentFile();
}

// What the documentation says about the deploy: read as text, no Docker needed.
public sealed class DeployDocsTests
{
    [Fact]
    [Trait("AC", "R-014.AC-8")]
    public async Task Architecture_describes_the_demo_deploy_in_place_of_not_built_yet()
    {
        var section = await ArchitectureSectionAsync("## Environments and deployment");

        Assert.DoesNotContain("not built yet", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(DemoProject.Script, section);
        Assert.Contains("ai-hub-demo", section);
        Assert.Contains("/health", section);
        Assert.Contains("previous image", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("AC", "R-014.AC-8")]
    public async Task Known_trade_offs_gives_the_production_path_as_the_scalability_considerations()
    {
        var section = await ArchitectureSectionAsync("## Known trade-offs");

        var row = section.Split('\n').FirstOrDefault(line =>
            line.StartsWith('|') && line.Contains("production", StringComparison.OrdinalIgnoreCase)
            && line.Contains("managed database", StringComparison.OrdinalIgnoreCase));
        Assert.True(row is not null, "Known trade-offs has no row giving the production path with a managed database.");
        Assert.Contains("provider", row, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scal", row, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("§8", row);
    }

    [Fact]
    [Trait("AC", "R-014.AC-8")]
    public async Task Readme_says_how_to_deploy_a_commit_to_the_demo_server()
    {
        var readme = await File.ReadAllTextAsync(Path.Combine(Checkout.Root, "README.md"));

        Assert.Contains(DemoProject.Script, readme);
        Assert.Contains("demo", readme, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ArchitectureSectionAsync(string heading)
    {
        var architecture = await File.ReadAllTextAsync(Path.Combine(Checkout.Root, "docs", "architecture.md"));
        var start = architecture.IndexOf("\n" + heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"architecture.md has no {heading} section.");
        var end = architecture.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return architecture[start..(end < 0 ? architecture.Length : end)];
    }
}
