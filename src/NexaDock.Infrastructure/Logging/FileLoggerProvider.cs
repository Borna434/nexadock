using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace NexaDock.Infrastructure.Logging;

/// <summary>Minimal append-only file logger with a single 1 MB roll-over, for field diagnostics.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 1024 * 1024;
    private readonly string _path;
    private readonly LogLevel _minLevel;
    private readonly object _gate = new();

    public FileLoggerProvider(string path, LogLevel minLevel = LogLevel.Information)
    {
        _path = path;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(LogLevel level, string category, string message, Exception? ex)
    {
        var sb = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(level.ToString().ToUpperInvariant()[..4]).Append("] ")
            .Append(category).Append(": ").Append(message);
        if (ex is not null)
        {
            sb.AppendLine().Append(ex);
        }

        sb.AppendLine();

        lock (_gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                File.AppendAllText(_path, sb.ToString());
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._minLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
