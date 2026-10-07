using Vox.Core.Speech;

namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// An <see cref="ISpeechEngine"/> that records what would have been spoken, with priority and
/// time, so tests can assert on Vox's spoken output instead of on mock calls.
/// </summary>
public sealed class RecordingSpeechEngine : ISpeechEngine
{
    private readonly object _lock = new();
    private readonly List<SpokenUtterance> _spoken = new();
    private readonly Func<DateTimeOffset> _clock;
    private int _cancelCount;
    private string? _voice;

    public RecordingSpeechEngine(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool IsSpeaking => false;
    public int RateWpm { get; private set; } = 200;
    public string? CurrentVoice => _voice;

    /// <summary>Snapshot of everything spoken so far, oldest first.</summary>
    public IReadOnlyList<SpokenUtterance> Spoken
    {
        get { lock (_lock) return _spoken.ToList(); }
    }

    /// <summary>The text of everything spoken so far, oldest first.</summary>
    public IReadOnlyList<string> SpokenText => Spoken.Select(s => s.Text).ToList();

    /// <summary>How many times <see cref="Cancel"/> was called.</summary>
    public int CancelCount => Volatile.Read(ref _cancelCount);

    public Task SpeakAsync(Utterance utterance, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock) _spoken.Add(new SpokenUtterance(utterance.Text, utterance.Priority, _clock()));
        return Task.CompletedTask;
    }

    public void Cancel() => Interlocked.Increment(ref _cancelCount);

    public void SetRate(int wpm) => RateWpm = wpm;

    public void SetVoice(string voiceName) => _voice = voiceName;

    public SpeechCapabilities Capabilities => SpeechCapabilities.Pitch | SpeechCapabilities.Volume;

    public int Pitch { get; private set; } = ISpeechEngine.DefaultPitch;
    public void SetPitch(int pitch) => Pitch = pitch;

    public int Volume { get; private set; } = 100;
    public void SetVolume(int volume) => Volume = volume;

    public IReadOnlyList<string> GetAvailableVoices() => ["Test Voice"];

    public void Clear()
    {
        lock (_lock) _spoken.Clear();
    }

    /// <summary>
    /// Waits until an utterance matching <paramref name="predicate"/> has been spoken, and returns it.
    /// Throws <see cref="TimeoutException"/> if none arrives in time (default 2 s).
    /// </summary>
    public async Task<SpokenUtterance> WaitForAsync(Func<SpokenUtterance, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        while (true)
        {
            lock (_lock)
            {
                var match = _spoken.FirstOrDefault(predicate);
                if (match is not null) return match;
            }
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Nothing matching was spoken. Spoken: [{string.Join(" | ", SpokenText)}]");
            await Task.Delay(10);
        }
    }

    public Task<SpokenUtterance> WaitForTextAsync(string text, TimeSpan? timeout = null) =>
        WaitForAsync(s => s.Text == text, timeout);
}

public sealed record SpokenUtterance(string Text, SpeechPriority Priority, DateTimeOffset Timestamp);
