using Microsoft.Extensions.Options;
using Vox.Core.Input;
using Vox.Core.Speech;

namespace Vox.Core.Configuration;

/// <summary>
/// The settings ring: speech settings changed from anywhere without a dialog. Previous/next
/// picks a setting (voice, rate, pitch, volume, punctuation, engine) and says it with its value;
/// increase/decrease changes it, saves it (so it is applied at once) and says the new value.
/// Settings the engine doesn't support (pitch, volume) are left out.
/// </summary>
public sealed class SettingsRing
{
    /// <summary>A setting in the ring.</summary>
    public enum Setting
    {
        Voice,
        Rate,
        Pitch,
        Volume,
        Punctuation,
        Engine,
    }

    private const int RateStep = 10;
    private const int FastRateStep = 25;
    private const int FastRateFromWpm = 400;
    private const int PercentStep = 5;
    // Lower than this and the user couldn't hear Vox to turn it back up
    private const int MinVolume = 10;

    private readonly ISpeechEngine _engine;
    private readonly IReadOnlyList<SpeechEngineDescriptor> _engines;
    private readonly IOptionsMonitor<VoxSettings> _settings;
    private readonly Action<VoxSettings> _updateSettings;
    private readonly SpeechQueue _speechQueue;
    private Setting _current = Setting.Rate;

    public SettingsRing(ISpeechEngine engine, IReadOnlyList<SpeechEngineDescriptor> engines, IOptionsMonitor<VoxSettings> settings,
        Action<VoxSettings> updateSettings, SpeechQueue speechQueue)
    {
        _engine = engine;
        _engines = engines;
        _settings = settings;
        _updateSettings = updateSettings;
        _speechQueue = speechQueue;
    }

    /// <summary>The setting picked now.</summary>
    public Setting Current => _current;

    /// <summary>Runs <paramref name="command"/> if it is a settings ring command; returns whether it was.</summary>
    public bool TryHandle(NavigationCommand command)
    {
        string? text = command switch
        {
            NavigationCommand.SettingsRingPrevious => Move(-1),
            NavigationCommand.SettingsRingNext => Move(+1),
            NavigationCommand.SettingsRingIncrease => Change(+1),
            NavigationCommand.SettingsRingDecrease => Change(-1),
            _ => null,
        };
        if (text is null)
            return false;
        _speechQueue.Enqueue(new Utterance(text, SpeechPriority.Interrupt));
        return true;
    }

    /// <summary>The settings in the ring, in order (those the engine supports).</summary>
    public IReadOnlyList<Setting> Settings
    {
        get
        {
            var capabilities = _engine.Capabilities;
            var settings = new List<Setting> { Setting.Voice, Setting.Rate };
            if (capabilities.HasFlag(SpeechCapabilities.Pitch))
                settings.Add(Setting.Pitch);
            if (capabilities.HasFlag(SpeechCapabilities.Volume))
                settings.Add(Setting.Volume);
            settings.Add(Setting.Punctuation);
            if (_engines.Count > 1)
                settings.Add(Setting.Engine);
            return settings;
        }
    }

    /// <summary>Picks the previous or next setting (wrapping around); returns what to say.</summary>
    public string Move(int direction)
    {
        var settings = Settings;
        int index = settings.ToList().IndexOf(_current);
        index = index < 0 ? 0 : (index + Math.Sign(direction) + settings.Count) % settings.Count;
        _current = settings[index];
        return Describe(_current, _settings.CurrentValue);
    }

    /// <summary>Changes the picked setting one step; returns what to say (the value, unchanged at either end).</summary>
    public string Change(int direction)
    {
        if (!Settings.Contains(_current))
            _current = Setting.Rate;
        var settings = _settings.CurrentValue;
        var changed = Step(_current, settings, Math.Sign(direction));
        if (changed != settings)
            _updateSettings(changed);
        return Value(_current, changed);
    }

    private VoxSettings Step(Setting setting, VoxSettings settings, int direction)
    {
        switch (setting)
        {
            case Setting.Voice:
            {
                var voices = _engine.GetAvailableVoices();
                if (voices.Count == 0)
                    return settings;
                var voice = settings.VoiceName ?? _engine.CurrentVoice;
                int index = voices.ToList().IndexOf(voice ?? string.Empty);
                index = Math.Clamp(index < 0 ? 0 : index + direction, 0, voices.Count - 1);
                return settings with { VoiceName = voices[index] };
            }
            case Setting.Rate:
            {
                int rate = settings.SpeechRateWpm;
                int step = direction > 0
                    ? (rate >= FastRateFromWpm ? FastRateStep : RateStep)
                    : (rate > FastRateFromWpm ? FastRateStep : RateStep);
                int max = Math.Clamp(_engine.MaxRateWpm, ISpeechEngine.MinSupportedWpm, ISpeechEngine.MaxSupportedWpm);
                return settings with { SpeechRateWpm = Math.Clamp(rate + direction * step, ISpeechEngine.MinSupportedWpm, max) };
            }
            case Setting.Pitch:
                return settings with { SpeechPitch = Math.Clamp(settings.SpeechPitch + direction * PercentStep, 0, 100) };
            case Setting.Volume:
                return settings with { SpeechVolume = Math.Clamp(settings.SpeechVolume + direction * PercentStep, MinVolume, 100) };
            case Setting.Punctuation:
            {
                int level = Math.Clamp((int)settings.PunctuationLevel + direction, (int)PunctuationLevel.None, (int)PunctuationLevel.All);
                return settings with { PunctuationLevel = (PunctuationLevel)level };
            }
            case Setting.Engine:
            {
                int index = IndexOfEngine(settings.SpeechEngine);
                index = Math.Clamp(index + direction, 0, _engines.Count - 1);
                var id = _engines[index].Id;
                // Another engine has other voices: start with its default one
                return string.Equals(id, EngineId(settings.SpeechEngine), StringComparison.OrdinalIgnoreCase)
                    ? settings
                    : settings with { SpeechEngine = id, VoiceName = null };
            }
            default:
                return settings;
        }
    }

    private int IndexOfEngine(string? id)
    {
        var current = EngineId(id);
        for (int i = 0; i < _engines.Count; i++)
        {
            if (string.Equals(_engines[i].Id, current, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    /// <summary>The engine a setting means (null or unknown: the preferred one).</summary>
    private string EngineId(string? id) =>
        _engines.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))?.Id ?? _engines[0].Id;

    private string Describe(Setting setting, VoxSettings settings) => $"{Name(setting)} {Value(setting, settings)}";

    private static string Name(Setting setting) => setting switch
    {
        Setting.Voice => "Voice",
        Setting.Rate => "Rate",
        Setting.Pitch => "Pitch",
        Setting.Volume => "Volume",
        Setting.Punctuation => "Punctuation",
        Setting.Engine => "Synthesizer",
        _ => setting.ToString(),
    };

    private string Value(Setting setting, VoxSettings settings) => setting switch
    {
        Setting.Voice => settings.VoiceName ?? _engine.CurrentVoice ?? "default",
        Setting.Rate => settings.SpeechRateWpm.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Setting.Pitch => settings.SpeechPitch.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Setting.Volume => settings.SpeechVolume.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Setting.Punctuation => settings.PunctuationLevel.ToString().ToLowerInvariant(),
        Setting.Engine => _engines[IndexOfEngine(settings.SpeechEngine)].DisplayName,
        _ => string.Empty,
    };
}
