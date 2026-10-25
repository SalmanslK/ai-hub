using System.Collections;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace App.Api.Hosting;

// First in the pipeline: takes the caller's X-Correlation-Id or makes one, echoes it on the
// response, and opens a log scope so every entry written for the request carries it.
// The request log names the method, path and status only — never the query or headers.
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string Header = "X-Correlation-Id";
    private const string ItemKey = "CorrelationId";

    public static string? Of(HttpContext context) => context.Items[ItemKey] as string;

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[Header].ToString();
        // A caller's value is accepted only if it cannot forge or bloat a log line.
        var id = Acceptable().IsMatch(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = id;
        // Set when the response starts, so the exception handler clearing the response keeps it.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Header] = id;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new CorrelationScope(id)))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                logger.LogInformation("HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0} ms",
                    context.Request.Method, context.Request.Path.Value, context.Response.StatusCode,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,128}$")]
    private static partial Regex Acceptable();

    private sealed class CorrelationScope(string id) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => 1;

        public KeyValuePair<string, object?> this[int index] =>
            index == 0 ? new(ItemKey, id) : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{ItemKey}:{id}";
    }
}
