using System.Collections.Concurrent;
using App.Processing.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace App.IntegrationTests.EventPipeline;

// The real host, in process, on one database. With `worker: false` Capture's background worker is
// taken out, so nothing hands events to Processing; `processor` wraps Processing's contract as the
// worker sees it. Every log entry is kept in Logs, with its level.
public sealed class PipelineHost(
    string connectionString,
    bool worker = true,
    Func<IServiceProvider, IUsageEventProcessor, IUsageEventProcessor>? processor = null)
    : WebApplicationFactory<Program>
{
    private const string CaptureAssembly = "App.Capture";

    public LevelledLogs Logs { get; } = new();

    public int WorkersRemoved { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { [IntegrationHost.ConnectionSetting] = connectionString }));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            if (!worker)
                WorkersRemoved = RemoveCaptureWorkers(services);
            if (processor is not null)
                Wrap(services, processor);
        });
    }

    // Starts the host and returns it, so a test reads as "start, then act".
    public PipelineHost Started()
    {
        _ = Services;
        if (!worker)
            Assert.True(WorkersRemoved > 0, "Capture registers no hosted worker to take out.");
        return this;
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private static int RemoveCaptureWorkers(IServiceCollection services)
    {
        var workers = services
            .Where(d => d.ServiceType == typeof(IHostedService)
                        && (d.ImplementationType ?? d.ImplementationInstance?.GetType())?.Assembly.GetName().Name == CaptureAssembly)
            .ToList();
        foreach (var descriptor in workers)
            services.Remove(descriptor);
        return workers.Count;
    }

    // Replaces Processing's registration with `wrap` around it, keeping its lifetime.
    private static void Wrap(IServiceCollection services,
        Func<IServiceProvider, IUsageEventProcessor, IUsageEventProcessor> wrap)
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(IUsageEventProcessor))
                       ?? throw new InvalidOperationException("Processing registers no IUsageEventProcessor.");
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(IUsageEventProcessor),
            provider => wrap(provider, Inner(provider, original)), original.Lifetime));
    }

    private static IUsageEventProcessor Inner(IServiceProvider provider, ServiceDescriptor descriptor) =>
        (IUsageEventProcessor)(descriptor.ImplementationInstance
                               ?? descriptor.ImplementationFactory?.Invoke(provider)
                               ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
}

// Every log entry with its level: category, message, exception and structured properties as one text.
public sealed class LevelledLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Text)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Text)> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LevelledLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Select(p => $"{p.Key}={p.Value}")
                : [];
            owner._entries.Enqueue((logLevel, string.Join("\n",
                new[] { category, formatter(state, exception), exception?.ToString() ?? "" }.Concat(properties))));
        }
    }
}

// Processing's contract as the worker calls it: each call recorded, then handed to the real one.
// `failFirst` makes that many calls for `failing` throw before they reach it.
public sealed class RecordingProcessor(IUsageEventProcessor inner, Calls calls, Guid? failing = null, int failFirst = 0)
    : IUsageEventProcessor
{
    public async Task ProcessAsync(CapturedEvent capturedEvent, CancellationToken cancellationToken)
    {
        if (capturedEvent.Id == failing && calls.Failures(capturedEvent.Id) < failFirst)
        {
            calls.Add(capturedEvent.Id, failed: true);
            throw new InvalidOperationException("Processing is unavailable.");
        }
        await inner.ProcessAsync(capturedEvent, cancellationToken);
        calls.Add(capturedEvent.Id, failed: false);
    }
}

// The calls the worker made, in the order it made them, shared by every scope's processor.
public sealed class Calls
{
    private readonly ConcurrentQueue<(Guid Id, bool Failed)> _calls = new();

    public IReadOnlyList<(Guid Id, bool Failed)> All => _calls.ToArray();

    public IReadOnlyList<Guid> Handled => All.Where(c => !c.Failed).Select(c => c.Id).ToList();

    public int Failures(Guid id) => All.Count(c => c.Id == id && c.Failed);

    public void Add(Guid id, bool failed) => _calls.Enqueue((id, failed));
}
