using App.Api.Hosting.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace App.Api.Hosting;

// The cross-cutting plumbing every request passes through: settings, logging, correlation id,
// problem-details errors, the database and its migrations, the health check, the OpenAPI document and the
// built front end.
public static class HostPlumbing
{
    public const string OpenApiPath = "/api/openapi.json";
    public const string HealthPath = "/health";
    public const string DatabaseCheck = "database";
    // Any path that is not a file and not under /api: the front end's router owns it.
    private const string FrontEndRoute = "{*path:nonfile:regex(^(?!api(/|$)))}";
    private const string FrontEndEntry = "index.html";

    public static WebApplicationBuilder AddHostPlumbing(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<ApiOptions>()
            .BindConfiguration(ApiOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
        });
        builder.Services.AddLogRedaction();

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Of(context.HttpContext);
            // A fault's detail can hold a message, a stack trace or data; the caller gets none of it.
            if (context.ProblemDetails.Status >= StatusCodes.Status500InternalServerError)
            {
                context.ProblemDetails.Detail = null;
                context.ProblemDetails.Extensions.Remove("exception");
            }
        });

        builder.Services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.Section)
            .Configure<IConfiguration>((options, configuration) =>
                options.AppDb = configuration.GetConnectionString(DatabaseOptions.ConnectionName) ?? "")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddHostedService<DatabaseMigrator>();

        // The database check reports only its status: its failure never carries the connection string.
        builder.Services.AddHealthChecks()
            .AddCheck("process", () => HealthCheckResult.Healthy())
            .AddNpgSql(DatabaseOptions.ConnectionString, name: DatabaseCheck);

        builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, _) =>
        {
            var api = context.ApplicationServices.GetRequiredService<IOptions<ApiOptions>>().Value;
            document.Info.Title = api.Title;
            document.Info.Version = api.Version;
            return Task.CompletedTask;
        }));
        return builder;
    }

    public static WebApplication UseHostPlumbing(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"),
            api => api.UseStatusCodePages());
        // The built front end, from the web root, with / as its index.html; with none there, it serves nothing.
        app.UseDefaultFiles();
        app.UseStaticFiles();
        // After the correlation id, so routing's own log entries carry it.
        app.UseRouting();

        app.MapOpenApi(OpenApiPath);
        app.MapGet(HealthPath, async (HealthCheckService health, CancellationToken cancellation) =>
            {
                var report = await health.CheckHealthAsync(cancellation);
                var body = new HealthBody(
                    report.Status.ToString(),
                    report.Entries.Select(e => new HealthEntry(e.Key, e.Value.Status.ToString())).ToList());
                return Results.Json(body, statusCode: report.Status == HealthStatus.Unhealthy
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status200OK);
            })
            .AllowAnonymous()
            .Produces<HealthBody>()
            .Produces<HealthBody>(StatusCodes.Status503ServiceUnavailable);
        // A fallback has the lowest priority, so every mapped endpoint wins; an unknown /api path stays a problem.
        app.MapFallbackToFile(FrontEndRoute, FrontEndEntry);
        return app;
    }

    public sealed record HealthBody(string Status, IReadOnlyList<HealthEntry> Checks);

    public sealed record HealthEntry(string Name, string Status);
}
