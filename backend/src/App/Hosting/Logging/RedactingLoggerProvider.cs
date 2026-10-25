using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace App.Api.Hosting.Logging;

// Wraps a logger provider so it only ever receives redacted entries and scopes.
public sealed class RedactingLoggerProvider(ILoggerProvider inner) : ILoggerProvider, ISupportExternalScope
{
    public ILogger CreateLogger(string categoryName) => new RedactingLogger(inner.CreateLogger(categoryName));

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        if (inner is ISupportExternalScope scoped)
            scoped.SetScopeProvider(new RedactingScopeProvider(scopeProvider));
    }

    // The inner provider belongs to the container, which disposes it.
    public void Dispose()
    {
    }

    private sealed class RedactingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!inner.IsEnabled(logLevel))
                return;
            var redacted = LogRedactor.Redact(state, formatter(state, exception));
            inner.Log(logLevel, eventId, redacted, LogRedactor.Redact(exception), static (s, _) => s.ToString());
        }
    }

    private sealed class RedactingScopeProvider(IExternalScopeProvider inner) : IExternalScopeProvider
    {
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) =>
            inner.ForEachScope((scope, s) => callback(LogRedactor.RedactScope(scope), s), state);

        public IDisposable Push(object? state) => inner.Push(state);
    }
}

public static class RedactingLoggingExtensions
{
    // Every logger provider — including one registered after this call — sees entries only
    // through redaction: the factory wraps whichever providers the container holds.
    // Provider-specific filter rules (Logging:Console:LogLevel) no longer match; use Logging:LogLevel.
    public static IServiceCollection AddLogRedaction(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(provider => new LoggerFactory(
            provider.GetServices<ILoggerProvider>().Select(p => new RedactingLoggerProvider(p)),
            provider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>(),
            provider.GetService<IOptions<LoggerFactoryOptions>>())));
        return services;
    }
}
