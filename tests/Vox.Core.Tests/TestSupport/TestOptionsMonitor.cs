using Microsoft.Extensions.Options;

namespace Vox.Core.Tests.TestSupport;

/// <summary>An <see cref="IOptionsMonitor{T}"/> whose value a test sets, telling the listeners like a reload.</summary>
public sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];

    public TestOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; private set; }

    public T Get(string? name) => CurrentValue;

    /// <summary>Changes the value and calls every listener.</summary>
    public void Set(T value)
    {
        CurrentValue = value;
        foreach (var listener in _listeners.ToList())
            listener(value, null);
    }

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new Subscription(() => _listeners.Remove(listener));
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}
