using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace App.Tests.Host;

// A logger provider that keeps every entry it receives: message, exception, the entry's
// structured properties and the properties of every scope it was written in.
public sealed class LogCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new List<KeyValuePair<string, string?>>();
            Add(state, properties);
            owner._scopes.ForEachScope((scope, list) => Add(scope, list), properties);
            owner._entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception?.ToString(), properties));
        }

        private static void Add(object? state, List<KeyValuePair<string, string?>> properties)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var (key, value) in pairs)
                    properties.Add(new(key, value?.ToString()));
            }
            else if (state is not null)
            {
                properties.Add(new("Scope", state.ToString()));
            }
        }
    }
}

public sealed record LogEntry(
    string Category,
    LogLevel Level,
    string Message,
    string? Exception,
    IReadOnlyList<KeyValuePair<string, string?>> Properties)
{
    // Everything the entry carries, as one string a value can be searched for in.
    public string Text =>
        string.Join("\n", new[] { Category, Message, Exception ?? "" }
            .Concat(Properties.Select(p => $"{p.Key}={p.Value}")));

    public string? Property(string name) =>
        Properties.LastOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}
