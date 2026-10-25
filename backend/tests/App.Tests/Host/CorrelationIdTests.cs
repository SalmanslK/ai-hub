using Xunit;

namespace App.Tests.Host;

public sealed class CorrelationIdTests
{
    // Hosting diagnostics log "request starting/finished" outside the middleware pipeline,
    // before the correlation id is read; every entry written inside it must carry the id.
    private const string HostingDiagnostics = "Microsoft.AspNetCore.Hosting.Diagnostics";

    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-3")]
    public async Task A_supplied_correlation_id_is_echoed_and_carried_by_every_log_entry_of_the_request()
    {
        using var host = new TestHost();
        var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add(TestHost.CorrelationHeader, "corr-r004-ac3");

        var response = await client.SendAsync(request);

        Assert.Equal("corr-r004-ac3", TestHost.Header(response, TestHost.CorrelationHeader));
        var entries = host.Logs.Entries
            .Where(e => e.Property("RequestPath") == "/health")
            .Where(e => !e.Category.StartsWith(HostingDiagnostics, StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal("corr-r004-ac3", e.Property("CorrelationId")));
    }

    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-4")]
    public async Task A_request_without_a_correlation_id_gets_a_new_one_each_time()
    {
        using var host = new TestHost();
        var client = host.CreateClient();

        var first = TestHost.Header(await client.GetAsync("/health"), TestHost.CorrelationHeader);
        var second = TestHost.Header(await client.GetAsync("/health"), TestHost.CorrelationHeader);

        Assert.False(string.IsNullOrWhiteSpace(first), "The first response carries no correlation id.");
        Assert.False(string.IsNullOrWhiteSpace(second), "The second response carries no correlation id.");
        Assert.NotEqual(first, second);
    }
}
