using System.Globalization;
using Microsoft.Extensions.Logging;

namespace LifeLedger.Api.Services;

/// <summary>Small dependency-free local log sink for self-hosted installations.</summary>
public sealed class LocalFileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly object _gate = new();

    public ILogger CreateLogger(string categoryName) => new LocalFileLogger(directory, categoryName, _gate);
    public void Dispose() { }

    private sealed class LocalFileLogger(string directory, string categoryName, object gate) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"lifeledger-{DateTime.UtcNow:yyyy-MM-dd}.log");
                var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {categoryName}: {formatter(state, exception)}";
                if (exception is not null) line += $"{Environment.NewLine}{exception}";
                lock (gate) File.AppendAllText(path, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch { /* Logging must never interrupt the financial application. */ }
        }
    }
}
