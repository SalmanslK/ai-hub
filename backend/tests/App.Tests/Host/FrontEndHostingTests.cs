using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace App.Tests.Host;

// The host serves the built front end from its web root, on the same origin as /api: index.html for any
// path that is not /api/* or /health. Each test gets a web root of its own, deleted afterwards.
public sealed class FrontEndHostingTests : IDisposable
{
    private const string IndexHtml = "<!doctype html><html><body><div id=\"root\">r011-index</div></body></html>";
    private const string AppJs = "console.log('r011-app');";
    private const string AssetPath = "assets/app.js";

    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "r011-webroot-" + Guid.NewGuid().ToString("N"));

    public FrontEndHostingTests()
    {
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(_webRoot, AssetPath), AppJs);
    }

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    private TestHost HostWithFrontEnd() => new(builder => builder.UseWebRoot(_webRoot));

    [Fact]
    [Trait("AC", "R-011.AC-1")]
    public async Task The_root_path_returns_the_front_ends_index_html()
    {
        using var host = HostWithFrontEnd();
        var client = host.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(IndexHtml, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [Trait("AC", "R-011.AC-2")]
    [InlineData("/connections/abc")]
    [InlineData("/insights")]
    public async Task A_deep_link_outside_the_api_returns_index_html(string path)
    {
        using var host = HostWithFrontEnd();
        var client = host.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(IndexHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("AC", "R-011.AC-3")]
    public async Task A_built_asset_is_served_as_itself()
    {
        using var host = HostWithFrontEnd();
        var client = host.CreateClient();

        var response = await client.GetAsync("/" + AssetPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith("javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AppJs, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("AC", "R-011.AC-4")]
    public async Task An_unknown_api_path_is_still_a_404_problem_not_index_html()
    {
        using var host = HostWithFrontEnd();
        var client = host.CreateClient();

        var response = await client.GetAsync("/api/r011-no-such-endpoint");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("r011-index", text);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        var correlationId = TestHost.Header(response, TestHost.CorrelationHeader);
        Assert.False(string.IsNullOrWhiteSpace(correlationId), "The response carries no correlation id header.");
        Assert.Equal(correlationId, body.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    [Trait("AC", "R-011.AC-5")]
    public async Task Health_and_the_openapi_document_still_answer_json_not_index_html()
    {
        using var host = HostWithFrontEnd();
        var client = host.CreateClient();

        var health = await client.GetAsync("/health");
        var openApi = await client.GetAsync("/api/openapi.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("application/json", health.Content.Headers.ContentType?.MediaType);
        using (var body = JsonDocument.Parse(await health.Content.ReadAsStringAsync()))
        {
            var checks = body.RootElement.EnumerateObject()
                .First(p => string.Equals(p.Name, "checks", StringComparison.OrdinalIgnoreCase)).Value;
            Assert.NotEmpty(checks.EnumerateArray());
            Assert.All(checks.EnumerateArray(), check => Assert.True(
                check.EnumerateObject().Any(p => string.Equals(p.Name, "status", StringComparison.OrdinalIgnoreCase)),
                "A check carries no status."));
        }

        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Equal("application/json", openApi.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.TryGetProperty("openapi", out _), "No `openapi` member.");
    }

    [Fact]
    [Trait("AC", "R-011.AC-6")]
    public async Task Without_a_built_front_end_the_host_starts_and_health_answers()
    {
        var emptyRoot = Path.Combine(_webRoot, "empty");
        Directory.CreateDirectory(emptyRoot);
        using var host = new TestHost(builder => builder.UseWebRoot(emptyRoot));
        var client = host.CreateClient();

        var health = await client.GetAsync("/health");
        var openApi = await client.GetAsync("/api/openapi.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("application/json", health.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
    }

    [Fact]
    [Trait("AC", "R-011.AC-6")]
    public async Task With_the_default_web_root_the_host_starts_and_health_answers()
    {
        using var host = new TestHost();
        var client = host.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
