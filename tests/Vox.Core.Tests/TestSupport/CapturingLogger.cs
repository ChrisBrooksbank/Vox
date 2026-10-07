using Microsoft.Extensions.Logging;

namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps every entry at every level (Trace included), with the
/// formatted message and each structured value, so tests can check what would reach a log file.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly object _lock = new();
    private readonly List<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> Entries
    {
        get { lock (_lock) return _entries.ToList(); }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.Select(p => p.Value?.ToString() ?? string.Empty).ToList()
            : new List<string>();
        lock (_lock)
            _entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), values, exception?.Message));
    }

    /// <summary>Everything that would be written for this logger: messages, values and exception messages.</summary>
    public string AllText
    {
        get
        {
            lock (_lock)
                return string.Join("\n", _entries.Select(e =>
                    string.Join(" ", new[] { e.Message, e.ExceptionMessage ?? string.Empty }.Concat(e.Values))));
        }
    }
}

public sealed record CapturedLogEntry(LogLevel Level, string Message, IReadOnlyList<string> Values, string? ExceptionMessage);
