using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace App.Tests.Host;

public sealed class SettingsTests
{
    private static readonly string HostFolder = Path.Combine(FindBackendRoot(), "src", "App");
    private static readonly Regex SecretKey = new(@"(?i)(password|pwd|secret|token|api[_-]?key|connectionstring|credential)");
    private static readonly Regex Placeholder = new(@"(?i)(change[_-]?me|placeholder|<[^<>]+>|__[A-Z0-9_]+__)");

    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-9")]
    public void Start_up_fails_naming_a_required_setting_that_is_missing()
    {
        var settings = Path.Combine(HostFolder, "appsettings.json");
        Assert.True(File.Exists(settings), "backend/src/App/appsettings.json is missing.");
        var appSettings = Leaves(settings)
            .Where(l => !l.Key.StartsWith("Logging:", StringComparison.OrdinalIgnoreCase) && l.Key != "AllowedHosts")
            .ToList();
        Assert.True(appSettings.Count > 0, "appsettings.json binds no application setting.");

        // Every application setting blanked: at least one is required, so the host must refuse to start.
        using var host = new TestHost(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(appSettings.Select(l => new KeyValuePair<string, string?>(l.Key, "")))));
        var error = Record.Exception(() => host.CreateClient());

        Assert.True(error is not null, "The host started with every application setting blank.");
        var messages = Messages(error);
        Assert.True(
            appSettings.Any(l => messages.Contains(l.Key.Split(':').Last(), StringComparison.Ordinal)),
            "The start-up error names none of the settings: " + messages);
    }

    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-9")]
    public void Every_committed_secret_setting_holds_a_placeholder()
    {
        var files = Directory.GetFiles(HostFolder, "appsettings*.json");
        Assert.Contains(files, f => Path.GetFileName(f) == "appsettings.json");

        var offenders = files
            .SelectMany(f => Leaves(f).Select(l => (File: Path.GetFileName(f), l.Key, l.Value)))
            .Where(l => SecretKey.IsMatch(l.Key) && !Placeholder.IsMatch(l.Value))
            .Select(l => $"{l.File}: {l.Key}")
            .ToList();

        Assert.True(offenders.Count == 0, "Not a recognisable placeholder: " + string.Join(", ", offenders));
    }

    private static List<KeyValuePair<string, string>> Leaves(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var leaves = new List<KeyValuePair<string, string>>();
        Walk(document.RootElement, "", leaves);
        return leaves;
    }

    private static void Walk(JsonElement element, string path, List<KeyValuePair<string, string>> leaves)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, path.Length == 0 ? property.Name : $"{path}:{property.Name}", leaves);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Walk(item, $"{path}:{index++}", leaves);
                break;
            default:
                leaves.Add(new(path, element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText()));
                break;
        }
    }

    private static string Messages(Exception error)
    {
        var messages = new List<string>();
        var pending = new Stack<Exception>([error]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions)
                    pending.Push(inner);
            else if (current.InnerException is not null)
                pending.Push(current.InnerException);
        }
        return string.Join(" | ", messages);
    }

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "App.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("App.slnx not found above the test output.");
    }
}
