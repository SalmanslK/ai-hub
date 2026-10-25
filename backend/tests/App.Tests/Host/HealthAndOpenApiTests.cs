using System.Net;
using System.Text.Json;
using Xunit;

namespace App.Tests.Host;

public sealed class HealthAndOpenApiTests
{
    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-1")]
    public async Task Health_answers_200_with_a_healthy_status_without_authentication()
    {
        using var host = new TestHost();
        var client = host.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = body.RootElement.EnumerateObject()
            .FirstOrDefault(p => string.Equals(p.Name, "status", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(JsonValueKind.String, status.Value.ValueKind);
        Assert.Equal("Healthy", status.Value.GetString(), ignoreCase: true);
    }

    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-2")]
    public async Task The_openapi_document_is_served_and_lists_health()
    {
        using var host = new TestHost();
        var client = host.CreateClient();

        var response = await client.GetAsync("/api/openapi.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.TryGetProperty("openapi", out var version), "No `openapi` member.");
        Assert.StartsWith("3.", version.GetString());
        Assert.True(document.RootElement.TryGetProperty("paths", out var paths), "No `paths` member.");
        Assert.True(paths.TryGetProperty("/health", out _), "`paths` does not include /health.");
    }
}
