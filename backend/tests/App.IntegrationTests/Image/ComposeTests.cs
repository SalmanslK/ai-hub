using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace App.IntegrationTests.Image;

// The local environment: compose.yaml at the repository root, its values from an environment file.
// Each test runs its own Compose project, port and environment file, so a developer's own `.env`
// and running environment are never touched.
public sealed partial class ComposeProject : IAsyncDisposable
{
    public const string ComposeFile = "compose.yaml";
    public const string ExampleFile = ".env.example";
    public const string AppPort = "8080";
    public const string TestDatabase = "aihub_compose_test";

    private readonly string _envFile = Path.Combine(Path.GetTempPath(), "r012-" + Guid.NewGuid().ToString("N") + ".env");

    public string Name { get; } = "r012-" + Guid.NewGuid().ToString("N")[..12];
    public IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();

    // `.env.example` with every placeholder replaced by a made-up value, and the overrides on top.
    public static IDictionary<string, string> FilledExample(IReadOnlyDictionary<string, string> overrides)
    {
        var values = ReadEnvFile(Path.Combine(Checkout.Root, ExampleFile))
            .ToDictionary(pair => pair.Key, pair => IsPlaceholder(pair.Value) ? "test_" + Guid.NewGuid().ToString("N")[..16] : pair.Value);
        foreach (var (key, value) in overrides)
            values[key] = value;
        return values;
    }

    public async Task WriteEnvAsync(IDictionary<string, string> values)
    {
        Values = new Dictionary<string, string>(values);
        await File.WriteAllLinesAsync(_envFile, values.Select(pair => $"{pair.Key}={pair.Value}"));
    }

    public Task<CommandLine.Result> RunAsync(params string[] arguments) =>
        CommandLine.RunAsync("docker",
            ["compose", "-p", Name, "-f", ComposeFile, "--env-file", _envFile, .. arguments],
            Checkout.Root);

    // The host port Compose published for the app's 8080.
    public async Task<Uri> AppAddressAsync()
    {
        var port = await RunAsync("port", "app", AppPort);
        Assert.True(port.ExitCode == 0, $"docker compose port app {AppPort} failed: {port.Stderr}");
        var published = port.Stdout.Trim();
        return new Uri($"http://localhost:{published[(published.LastIndexOf(':') + 1)..]}");
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_envFile))
            await RunAsync("down", "-v", "--remove-orphans");
        File.Delete(_envFile);
    }

    public static Dictionary<string, string> ReadEnvFile(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('='))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim().Trim('"', '\''));

    public static bool IsPlaceholder(string value) => Placeholder().IsMatch(value);

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // The placeholder forms of architecture.md "Settings": CHANGE_ME, <…>, __…__, REPLACE_….
    [GeneratedRegex(@"^(CHANGE_ME\w*|<[^>]+>|__\w+__|REPLACE_\w+)$")]
    private static partial Regex Placeholder();
}

[Collection(ImageCollection.Name)]
public sealed class ComposeTests(ImageBuild image)
{
    private const string PostgresImage = "postgres:17-alpine";
    private const string DataVolumeTarget = "/var/lib/postgresql/data";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(120);

    [Fact]
    [Trait("AC", "R-012.AC-1")]
    public async Task Compose_up_runs_the_image_beside_postgres_and_answers_health_and_the_front_end()
    {
        Assert.True(image.Build.ExitCode == 0, $"docker build exited {image.Build.ExitCode}: {image.Build.Stderr}");
        await using var compose = await StartAsync();

        var services = await compose.RunAsync("ps", "--format", "{{.Service}}|{{.Image}}|{{.State}}");
        var running = services.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, running.Length);
        Assert.Contains(running, line => line.Contains($"|{image.Tag}|") && line.EndsWith("|running"));
        Assert.Contains(running, line => line.Contains($"|{PostgresImage}|") && line.EndsWith("|running"));

        using var client = new HttpClient { BaseAddress = await compose.AppAddressAsync() };
        var health = await Http.UntilAnsweredAsync(client, "/health", StartTimeout);
        var root = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Equal("text/html", root.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<div id=\"root\">", await root.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("AC", "R-012.AC-2")]
    public async Task A_row_survives_compose_down_and_up_on_the_named_volume()
    {
        Assert.True(image.Build.ExitCode == 0, $"docker build exited {image.Build.ExitCode}: {image.Build.Stderr}");
        await using var compose = await StartAsync();
        await AssertHealthyAsync(compose);

        var config = await compose.RunAsync("config", "--format", "json");
        Assert.True(config.ExitCode == 0, $"docker compose config failed: {config.Stderr}");
        using var document = JsonDocument.Parse(config.Stdout);
        var dbVolumes = document.RootElement.GetProperty("services").GetProperty("db").GetProperty("volumes").EnumerateArray();
        var data = dbVolumes.SingleOrDefault(volume => volume.GetProperty("target").GetString() == DataVolumeTarget);
        Assert.True(data.ValueKind == JsonValueKind.Object, $"The db service mounts nothing at {DataVolumeTarget}.");
        Assert.Equal("volume", data.GetProperty("type").GetString());
        var volumeName = data.GetProperty("source").GetString();
        Assert.True(document.RootElement.TryGetProperty("volumes", out var declared) && declared.TryGetProperty(volumeName!, out _),
            $"The volume {volumeName} is not declared under compose.yaml's volumes.");

        var marker = "r012-" + Guid.NewGuid().ToString("N");
        var write = await PsqlAsync(compose, $"CREATE TABLE r012_probe (note text); INSERT INTO r012_probe VALUES ('{marker}');");
        Assert.True(write.ExitCode == 0, $"Writing the row failed: {write.Stderr}");

        var down = await compose.RunAsync("down");
        Assert.True(down.ExitCode == 0, $"docker compose down failed: {down.Stderr}");
        var up = await compose.RunAsync("up", "-d");
        Assert.True(up.ExitCode == 0, $"docker compose up -d failed: {up.Stderr}");
        await AssertHealthyAsync(compose);

        var read = await PsqlAsync(compose, "SELECT note FROM r012_probe;");
        Assert.True(read.ExitCode == 0, $"Reading the row failed: {read.Stderr}");
        Assert.Equal(marker, read.Stdout.Trim());
    }

    [Fact]
    [Trait("AC", "R-012.AC-6")]
    public async Task The_image_holds_no_env_file()
    {
        Assert.True(image.Build.ExitCode == 0, $"docker build exited {image.Build.ExitCode}: {image.Build.Stderr}");

        var find = await CommandLine.RunAsync("docker",
            ["run", "--rm", "--user", "0", "--entrypoint", "find", image.Tag, "/", "-xdev", "(", "-name", ".env", "-o", "-name", ".env.*", ")", "-print"],
            Checkout.Root);

        Assert.True(find.ExitCode == 0, $"find failed in {image.Tag}: {find.Stderr}");
        Assert.True(find.Stdout.Trim().Length == 0, "The image holds: " + find.Stdout.Trim());
    }

    private async Task<ComposeProject> StartAsync()
    {
        Assert.True(File.Exists(Path.Combine(Checkout.Root, ComposeProject.ComposeFile)), "There is no compose.yaml at the repository root.");
        Assert.True(File.Exists(Path.Combine(Checkout.Root, ComposeProject.ExampleFile)), "There is no .env.example at the repository root.");

        var compose = new ComposeProject();
        await compose.WriteEnvAsync(ComposeProject.FilledExample(new Dictionary<string, string>
        {
            ["AI_HUB_TAG"] = image.Tag[(image.Tag.IndexOf(':') + 1)..],
            ["AI_HUB_PORT"] = ComposeProject.FreePort().ToString(),
            ["POSTGRES_DB"] = ComposeProject.TestDatabase,
        }));
        var up = await compose.RunAsync("up", "-d");
        Assert.True(up.ExitCode == 0, $"docker compose up -d failed: {up.Stderr}");
        return compose;
    }

    // The app answers /health only once its database is up and migrated.
    private static async Task AssertHealthyAsync(ComposeProject compose)
    {
        using var client = new HttpClient { BaseAddress = await compose.AppAddressAsync() };
        var health = await Http.UntilAnsweredAsync(client, "/health", StartTimeout);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    private static Task<CommandLine.Result> PsqlAsync(ComposeProject compose, string sql) =>
        compose.RunAsync("exec", "-T", "db", "psql",
            "-U", compose.Values.GetValueOrDefault("POSTGRES_USER", "postgres"),
            "-d", compose.Values["POSTGRES_DB"],
            "-v", "ON_ERROR_STOP=1", "-tAc", sql);
}

// What the repository holds: no Docker daemon is needed beyond Compose reading its file.
public sealed partial class ComposeFileTests
{
    private const string PasswordVariable = "POSTGRES_PASSWORD";

    [Fact]
    [Trait("AC", "R-012.AC-3")]
    public async Task Compose_refuses_to_start_without_an_environment_file()
    {
        AssertComposeFileExists();
        await using var compose = new ComposeProject();
        await compose.WriteEnvAsync(new Dictionary<string, string>());

        var up = await compose.RunAsync("up", "-d");

        Assert.NotEqual(0, up.ExitCode);
        Assert.Matches(MissingVariable(), up.Stderr);
        await AssertNoContainerAsync(compose);
    }

    [Fact]
    [Trait("AC", "R-012.AC-3")]
    public async Task Compose_refuses_to_start_without_the_database_password_and_names_it()
    {
        AssertComposeFileExists();
        await using var compose = new ComposeProject();
        var values = ComposeProject.FilledExample(new Dictionary<string, string>
        {
            ["AI_HUB_PORT"] = ComposeProject.FreePort().ToString(),
            ["POSTGRES_DB"] = ComposeProject.TestDatabase,
        });
        values.Remove(PasswordVariable);
        await compose.WriteEnvAsync(values);

        var up = await compose.RunAsync("up", "-d");

        Assert.NotEqual(0, up.ExitCode);
        Assert.Contains(PasswordVariable, up.Stderr);
        await AssertNoContainerAsync(compose);
    }

    [Fact]
    [Trait("AC", "R-012.AC-4")]
    public async Task Git_ignores_the_env_file_and_tracks_the_example()
    {
        var ignored = await CommandLine.RunAsync("git", ["check-ignore", "-q", ".env"], Checkout.Root);
        Assert.True(ignored.ExitCode == 0, ".env is not ignored by git: a real environment file could be committed.");

        var exampleIgnored = await CommandLine.RunAsync("git", ["check-ignore", "-q", ComposeProject.ExampleFile], Checkout.Root);
        Assert.True(exampleIgnored.ExitCode == 1, ".env.example is ignored by git.");

        var tracked = await CommandLine.RunAsync("git", ["ls-files", "--error-unmatch", ComposeProject.ExampleFile], Checkout.Root);
        Assert.True(tracked.ExitCode == 0, $".env.example is not tracked: {tracked.Stderr}");
    }

    [Fact]
    [Trait("AC", "R-012.AC-5")]
    public void The_example_holds_only_placeholders_for_its_secrets()
    {
        var example = Path.Combine(Checkout.Root, ComposeProject.ExampleFile);
        Assert.True(File.Exists(example), "There is no .env.example at the repository root.");

        var secrets = ComposeProject.ReadEnvFile(example).Where(pair => SecretKey().IsMatch(pair.Key)).ToList();

        Assert.Contains(secrets, pair => pair.Key == PasswordVariable);
        Assert.All(secrets, pair => Assert.True(ComposeProject.IsPlaceholder(pair.Value),
            $"{pair.Key} in .env.example is not a recognisable placeholder."));
    }

    [Fact]
    [Trait("AC", "R-012.AC-5")]
    public async Task Compose_file_references_its_secrets_and_holds_none()
    {
        AssertComposeFileExists();
        var lines = (await File.ReadAllLinesAsync(Path.Combine(Checkout.Root, ComposeProject.ComposeFile)))
            .Where(line => !line.TrimStart().StartsWith('#'));
        var text = string.Join('\n', lines);

        Assert.Contains("${" + PasswordVariable, text);
        var values = SecretValue().Matches(text).Select(match => match.Groups["value"].Value).ToList();
        Assert.NotEmpty(values);
        Assert.All(values, value => Assert.True(value.StartsWith("${"),
            $"compose.yaml holds a secret value ({value.Length} characters) instead of a ${{…}} reference."));
    }

    [Fact]
    [Trait("AC", "R-012.AC-7")]
    public async Task Architecture_names_the_local_environment_and_the_demo_one_as_missing()
    {
        var architecture = await File.ReadAllTextAsync(Path.Combine(Checkout.Root, "docs", "architecture.md"));
        var start = architecture.IndexOf("\n## Environments and deployment", StringComparison.Ordinal);
        Assert.True(start >= 0, "architecture.md has no Environments and deployment section.");
        var end = architecture.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = architecture[start..(end < 0 ? architecture.Length : end)];

        Assert.DoesNotContain("FILL", section);
        Assert.Contains("compose.yaml", section);
        Assert.Contains("PostgreSQL 17", section);
        Assert.Contains("volume", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".env.example", section);
        Assert.Contains("demo", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("AC", "R-012.AC-7")]
    public async Task Readme_gives_the_commands_to_run_it_locally()
    {
        var readme = await File.ReadAllTextAsync(Path.Combine(Checkout.Root, "README.md"));

        Assert.Contains("cp .env.example .env", readme);
        Assert.Contains("docker compose up", readme);
    }

    private static void AssertComposeFileExists() =>
        Assert.True(File.Exists(Path.Combine(Checkout.Root, ComposeProject.ComposeFile)), "There is no compose.yaml at the repository root.");

    private static async Task AssertNoContainerAsync(ComposeProject compose)
    {
        var containers = await compose.RunAsync("ps", "-a", "-q");
        Assert.True(containers.Stdout.Trim().Length == 0, "Compose started containers: " + containers.Stdout.Trim());
    }

    [GeneratedRegex(@"required variable \w+ is missing a value")]
    private static partial Regex MissingVariable();

    [GeneratedRegex("PASSWORD|SECRET|TOKEN|CONNECTION", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();

    // A password, secret or token key in compose.yaml and the value it is given, in YAML (key: value) or a
    // connection string (Password=value) — not a ${VAR:?…} reference's own name.
    [GeneratedRegex(@"(?<!\$\{\w*)(?:password|secret|token)\w*\s*[:=]\s*[""']?(?<value>[^;""'\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretValue();
}
