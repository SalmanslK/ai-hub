using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace App.Tests.Host;

public sealed class DatabaseSettingsTests
{
    private const string Setting = "ConnectionStrings:AppDb";
    private static readonly string HostFolder = Path.Combine(FindBackendRoot(), "src", "App");
    private static readonly Regex Placeholder = new(@"(?i)(change[_-]?me|placeholder|<[^<>]+>|__[A-Z0-9_]+__)");

    [Theory]
    [Trait("AC", "R-005.AC-6")]
    [InlineData("")]
    [InlineData(null)]
    public void Start_up_fails_naming_the_connection_setting_when_it_is_missing_or_empty(string? value)
    {
        using var host = new TestHost(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { [Setting] = value })));

        var error = Record.Exception(() => host.CreateClient());

        Assert.True(error is not null, $"The host started with {Setting} {(value is null ? "missing" : "empty")}.");
        Assert.Contains("AppDb", Messages(error), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("AC", "R-005.AC-6")]
    public void The_committed_connection_setting_holds_a_placeholder_password()
    {
        var settings = Directory.GetFiles(HostFolder, "appsettings*.json")
            .Select(f => (File: Path.GetFileName(f), Value: Value(f, Setting)))
            .ToList();
        var committed = settings.SingleOrDefault(s => s.File == "appsettings.json");
        Assert.False(string.IsNullOrWhiteSpace(committed.Value), $"appsettings.json has no {Setting}.");

        foreach (var (file, value) in settings.Where(s => s.Value is not null))
        {
            var connection = new DbConnectionStringBuilder { ConnectionString = value };
            var password = new[] { "Password", "Pwd" }
                .Select(key => connection.TryGetValue(key, out var v) ? v?.ToString() : null)
                .FirstOrDefault(v => v is not null);
            Assert.True(password is null || Placeholder.IsMatch(password),
                $"{file}: {Setting} carries a password that is not a recognisable placeholder.");
        }
    }

    private static string? Value(string file, string key)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var element = document.RootElement;
        foreach (var part in key.Split(':'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element))
                return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
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
