using System.Globalization;
using Microsoft.Extensions.Logging;

namespace LifeLedger.Api.Services;

/// <summary>Small dependency-free local log sink for self-hosted installations.</summary>
public sealed class LocalFileLoggerProvider(string directory) : ILoggerProvider
{
    /// <summary>Serialises writes from concurrent requests to the same daily log file.</summary>
    private readonly object _gate = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new LocalFileLogger(directory, categoryName, _gate);

    /// <inheritdoc />
    public void Dispose() { }

    /// <summary>Writes application events to a local daily text file without an external logging dependency.</summary>
    private sealed class LocalFileLogger(string directory, string categoryName, object gate) : ILogger
    {
        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                Directory.CreateDirectory(directory);
                // One file per UTC day keeps local diagnostics easy to inspect and rotate.
                var path = Path.Combine(directory, $"lifeledger-{DateTime.UtcNow:yyyy-MM-dd}.log");
                var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {categoryName}: {formatter(state, exception)}";
                if (exception is not null) line += $"{Environment.NewLine}{exception}";
                lock (gate) File.AppendAllText(path, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch { /* Logging must never interrupt the financial application. */ }
        }
    }
}
