using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace App.IntegrationTests;

// The real host, in process, pointed at one database, with every log entry it writes kept in Logs.
public sealed class IntegrationHost(string connectionString) : WebApplicationFactory<Program>
{
    public const string ConnectionSetting = "ConnectionStrings:AppDb";

    public LogCapture Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { [ConnectionSetting] = connectionString }));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }
}

// Every entry's category, message, exception and structured properties, as text a value can be searched for in.
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Select(p => $"{p.Key}={p.Value}")
                : [];
            owner._entries.Enqueue(string.Join("\n",
                new[] { category, formatter(state, exception), exception?.ToString() ?? "" }.Concat(properties)));
        }
    }
}
