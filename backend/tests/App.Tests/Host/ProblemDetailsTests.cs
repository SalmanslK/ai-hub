using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.Tests.Host;

public sealed class ProblemDetailsTests
{
    private const string ThrowingPath = "/api/r004-throws";
    private const string Secret = "sec-r004-problem-9f2c";
    private const string StackMarker = "   at App.Secret.Leak() in C:\\src\\Leak.cs:line 42";
    private static readonly string ExceptionMessage = $"Boom r004 {StackMarker} password={Secret}";

    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-5")]
    public async Task An_unhandled_api_exception_becomes_a_500_problem_without_detail()
    {
        using var host = new TestHost(builder => builder.ConfigureServices(services =>
            services.AddTransient<IStartupFilter, ThrowingApiEndpoint>()));
        var client = host.CreateClient();

        var response = await client.GetAsync(ThrowingPath);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        AssertProblem(text, 500, TestHost.Header(response, TestHost.CorrelationHeader));
        Assert.DoesNotContain("Boom r004", text);
        Assert.DoesNotContain(Secret, text);
        Assert.DoesNotContain("Leak.cs", text);
        Assert.DoesNotContain("   at ", text);
        Assert.DoesNotContain(nameof(InvalidOperationException), text);
    }

    [Fact]
    [Trait("AC", "R-005.AC-8")]
    [Trait("AC", "R-004.AC-6")]
    public async Task An_unknown_api_path_becomes_a_404_problem()
    {
        using var host = new TestHost();
        var client = host.CreateClient();

        var response = await client.GetAsync("/api/r004-no-such-endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        AssertProblem(await response.Content.ReadAsStringAsync(), 404, TestHost.Header(response, TestHost.CorrelationHeader));
    }

    private static void AssertProblem(string text, int status, string? correlationId)
    {
        using var body = JsonDocument.Parse(text);
        var root = body.RootElement;
        Assert.True(root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String, "No `type`.");
        Assert.True(root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String, "No `title`.");
        Assert.True(root.TryGetProperty("status", out var actual), "No `status`.");
        Assert.Equal(status, actual.GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(correlationId), "The response carries no correlation id header.");
        Assert.True(root.TryGetProperty("correlationId", out var member), "No `correlationId`.");
        Assert.Equal(correlationId, member.GetString());
    }

    // Appended after the host's own pipeline: a request no endpoint matched reaches it, so
    // the exception passes through every middleware the host put in front of it.
    private sealed class ThrowingApiEndpoint : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (HttpContext context, RequestDelegate following) =>
            {
                if (context.Request.Path == ThrowingPath)
                    throw new InvalidOperationException(ExceptionMessage);
                await following(context);
            });
        };
    }
}
