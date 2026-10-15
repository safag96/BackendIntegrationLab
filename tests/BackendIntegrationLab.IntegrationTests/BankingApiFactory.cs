using System.Collections.Concurrent;
using BackendIntegrationLab.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BackendIntegrationLab.IntegrationTests;

public sealed class BankingApiFactory : WebApplicationFactory<Program>
{
    // One physical SQLite file per TEST, never the application's development file.
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(), $"BackendIntegrationLab-test-{Guid.NewGuid():N}.db");

    public TestErrorLogProvider Errors { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(Errors));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<BankingDbContext>();
            services.RemoveAll<DbContextOptions<BankingDbContext>>();
            // Pooling=False releases the file when connections are disposed,
            // so teardown can delete it without touching other tests' databases.
            services.AddDbContext<BankingDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath};Pooling=False"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing); // Stop the host and dispose its services first.
        if (disposing)
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                File.Delete(databasePath + suffix);
        }
    }
}

// Observes the actual database error without changing application behavior.
// This lets the rollback test prove that its intended SQLite trigger fired.
public sealed class TestErrorLogProvider : ILoggerProvider
{
    public ConcurrentQueue<Exception> Exceptions { get; } = new();
    public ILogger CreateLogger(string categoryName) => new ErrorLogger(Exceptions);
    public void Dispose() { }

    private sealed class ErrorLogger(ConcurrentQueue<Exception> exceptions) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null)
                exceptions.Enqueue(exception);
        }
    }
}
