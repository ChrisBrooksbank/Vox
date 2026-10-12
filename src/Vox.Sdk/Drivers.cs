namespace Vox.Sdk;

/// <summary>A voice a speech engine has.</summary>
/// <param name="Language">BCP 47 tag, or empty when unknown.</param>
public sealed record VoiceInfo(string Name, string Language);

/// <summary>How one utterance is said.</summary>
/// <param name="Voice">A voice to use for this utterance only (one of <see cref="ISpeechEngine.GetVoices"/>), else null.</param>
/// <param name="PitchOffset">Added to the pitch for this utterance (0–100 scale).</param>
public sealed record SpeakOptions(string? Voice = null, int PitchOffset = 0);

/// <summary>
/// A speech engine. <see cref="SpeakAsync"/> completes when the text has been spoken (or was
/// cancelled); Vox calls it for one utterance at a time and calls <see cref="Cancel"/> from another
/// thread to stop it. Vox disposes the engine when it switches to another one.
/// </summary>
public interface ISpeechEngine : IDisposable
{
    bool IsSpeaking { get; }
    Task SpeakAsync(string text, SpeakOptions options, CancellationToken cancellationToken);
    void Cancel();

    /// <summary>Words per minute.</summary>
    void SetRate(int wordsPerMinute);

    /// <summary>0 (lowest) to 100 (highest), 50 normal.</summary>
    void SetPitch(int pitch);

    /// <summary>0 to 100.</summary>
    void SetVolume(int volume);

    /// <summary>Selects a voice by name; empty: the engine's default voice.</summary>
    void SetVoice(string voiceName);

    IReadOnlyList<VoiceInfo> GetVoices();

    /// <summary>The fastest rate the engine speaks, in words per minute.</summary>
    int MaxRateWpm => 450;
}

/// <summary>A speech engine an add-on offers: started only when chosen in the speech settings.</summary>
/// <param name="Id">The SpeechEngine setting's value for it; unique among engines.</param>
/// <param name="Name">As shown in the settings.</param>
public sealed record SpeechEngineRegistration(string Id, string Name, Func<ISpeechEngine> Create);

/// <summary>Keys pressed together on a braille display, reported once all are released.</summary>
/// <param name="Keys">The driver's names for the keys ("dot1", "space", "panLeft", "routing").</param>
/// <param name="RoutingCell">The routing key's cell (0-based), or null.</param>
public sealed record BrailleKeysEventArgs(IReadOnlyList<string> Keys, int? RoutingCell = null);

/// <summary>
/// A braille display driver. Vox asks drivers in turn to <see cref="TryConnect"/> while no display is
/// connected (every few seconds), and again after <see cref="Disconnected"/>. Cells are bytes with
/// bit n-1 for dot n. Vox's common braille key map applies to its <see cref="KeysPressed"/>.
/// </summary>
public interface IBrailleDriver : IDisposable
{
    /// <summary>The BrailleDisplay setting's value for this driver; unique among drivers.</summary>
    string Id { get; }

    /// <summary>The display's name as shown in settings.</summary>
    string Name { get; }

    bool IsConnected { get; }

    /// <summary>The number of text cells; 0 while disconnected.</summary>
    int CellCount { get; }

    /// <summary>Looks for a display and opens it, without throwing and within about a second; true when one is connected.</summary>
    bool TryConnect();

    /// <summary>Closes the display without raising <see cref="Disconnected"/>.</summary>
    void Disconnect();

    /// <summary>Shows <paramref name="cells"/>; the cursor is already drawn into them, <paramref name="cursorPosition"/> is its cell or null.</summary>
    void Write(ReadOnlyMemory<byte> cells, int? cursorPosition);

    event EventHandler<BrailleKeysEventArgs>? KeysPressed;

    /// <summary>A connected display was lost.</summary>
    event EventHandler? Disconnected;
}
