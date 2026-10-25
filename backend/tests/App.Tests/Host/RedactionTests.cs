using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace App.Tests.Host;

public sealed class RedactionTests
{
    private const string ProbeCategory = "R004.RedactionProbe";

    [Theory]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-7")]
    [InlineData("Password", "pw-r004-hunter2")]
    [InlineData("AccessToken", "tok-r004-abc123")]
    [InlineData("ClientSecret", "sec-r004-xyz789")]
    [InlineData("ConnectionString", "Server=db-r004;User Id=sa;Password=cs-r004-pw")]
    [InlineData("Email", "jane.doe.r004@example.com")]
    [InlineData("Requester", "john.r004@example.org")]
    public void A_sensitive_structured_property_is_replaced_before_any_sink_receives_it(string key, string value)
    {
        using var host = new TestHost();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(ProbeCategory);

        logger.LogWarning(
            "Sign-in {Password} {AccessToken} {ClientSecret} {ConnectionString} {Email} {Requester}",
            "pw-r004-hunter2", "tok-r004-abc123", "sec-r004-xyz789",
            "Server=db-r004;User Id=sa;Password=cs-r004-pw", "jane.doe.r004@example.com", "john.r004@example.org");

        var entry = Assert.Single(host.Logs.Entries, e => e.Category == ProbeCategory);
        var written = entry.Property(key);
        Assert.False(string.IsNullOrEmpty(written), $"`{key}` is missing from the entry; it should carry a redaction marker.");
        Assert.NotEqual(value, written);
        Assert.DoesNotContain(value, entry.Text);
        if (key == "ConnectionString")
            Assert.DoesNotContain("cs-r004-pw", entry.Text);
    }

    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-8")]
    public async Task A_bearer_token_and_a_password_in_the_query_never_reach_a_log()
    {
        using var host = new TestHost();
        var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health?password=pw-r004-query");
        request.Headers.Add("Authorization", "Bearer bearer-r004-token");

        await client.SendAsync(request);

        var entries = host.Logs.Entries;
        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.DoesNotContain("pw-r004-query", e.Text);
            Assert.DoesNotContain("bearer-r004-token", e.Text);
        });
    }
}
