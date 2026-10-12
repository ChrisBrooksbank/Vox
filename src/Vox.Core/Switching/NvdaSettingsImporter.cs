using Vox.Core.Braille;
using Vox.Core.Configuration;
using Vox.Core.Speech;

namespace Vox.Core.Switching;

/// <summary>Settings with what an import changed in them, and the report of what it did.</summary>
public sealed record SettingsImportResult(VoxSettings Settings, ImportReport Report);

/// <summary>
/// Brings NVDA's settings (<c>%APPDATA%\nvda\nvda.ini</c>) over into <see cref="VoxSettings"/>:
/// synthesizer and voice where Vox has a match, rate, pitch, volume, punctuation level, capitals,
/// typing echo, keyboard layout and screen reader key, browse-mode line length and layout, and
/// braille (display driver, table, tether, cursor, messages). Only what the file sets is brought
/// over (NVDA writes only what differs from its defaults); everything read is in the report, either
/// imported (with the value it replaced) or skipped with the reason. Nothing is saved here.
/// </summary>
public sealed class NvdaSettingsImporter
{
    public const string ConfigFileName = "nvda.ini";

    private readonly Func<string, IReadOnlyList<string>?> _voicesForEngine;

    /// <param name="voicesForEngine">
    /// The voices of a Vox speech engine by id (<see cref="SpeechEngineRegistry.OneCoreId"/>, ...),
    /// or null when that engine isn't installed.
    /// </param>
    public NvdaSettingsImporter(Func<string, IReadOnlyList<string>?> voicesForEngine)
    {
        _voicesForEngine = voicesForEngine;
    }

    /// <summary>NVDA's settings file for the current user (installed copy), whether or not it exists.</summary>
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvda", ConfigFileName);

    /// <summary>The user's NVDA settings file, or null when NVDA has never saved settings here.</summary>
    public static string? FindConfiguration() => File.Exists(DefaultConfigPath) ? DefaultConfigPath : null;

    /// <summary>Imports the settings file at <paramref name="path"/>; a file that can't be read is reported, nothing changed.</summary>
    public SettingsImportResult ImportFile(string path, VoxSettings current)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var report = new ImportReport(ImportSource.Nvda);
            report.AddFile(path);
            report.AddSkipped(Path.GetFileName(path), null, ImportSkipReason.Unreadable, ex.Message);
            return new SettingsImportResult(current, report);
        }
        return Import(text, current, path);
    }

    /// <summary>Imports settings from the text of an nvda.ini.</summary>
    public SettingsImportResult Import(string iniText, VoxSettings current, string fileName = ConfigFileName)
    {
        var file = ConfigObjFile.Parse(iniText);
        var import = new ImportState(current, new ImportReport(ImportSource.Nvda));
        import.Report.AddFile(fileName);
        import.Report.AddMalformed(fileName, file.Malformed);

        ImportSpeech(file.Root, import);
        ImportKeyboard(file.Root, import);
        ImportBrowseMode(file.Root, import);
        ImportBraille(file.Root, import);
        return new SettingsImportResult(import.Settings, import.Report);
    }

    // NVDA's synthesizer ids → Vox's engines
    private static string? EngineFor(string synth) => synth.ToLowerInvariant() switch
    {
        "onecore" => SpeechEngineRegistry.OneCoreId,
        "sapi5" => SpeechEngineRegistry.SapiId,
        "espeak" => SpeechEngineRegistry.EspeakId,
        _ => null,
    };

    private void ImportSpeech(ConfigObjSection root, ImportState import)
    {
        var speech = root.Section("speech");
        if (speech is null)
            return;

        // NVDA's "auto" (or no choice) is OneCore on Windows 10 and later
        string? synthValue = speech.Value("synth");
        string synth = string.IsNullOrWhiteSpace(synthValue) || synthValue.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "oneCore" : synthValue;
        string? engine = EngineFor(synth);
        IReadOnlyList<string>? voices = engine is null ? null : _voicesForEngine(engine);
        bool engineAvailable = false;
        if (synthValue is not null)
        {
            if (engine is null)
                import.Report.AddSkipped("speech/synth", synthValue, ImportSkipReason.NoCounterpart);
            else if (voices is null)
                import.Report.AddSkipped("speech/synth", synthValue, ImportSkipReason.NotInstalled, engine);
            else
            {
                engineAvailable = true;
                import.Set("speech/synth", nameof(VoxSettings.SpeechEngine), engine, s => s.SpeechEngine,
                    (s, v) => Equals(s.SpeechEngine, v) ? s : s with { SpeechEngine = v, VoiceName = null });
            }
        }
        else
            engineAvailable = voices is not null;

        if (speech.Value("symbolLevel") is { } symbolLevel)
        {
            if (ConfigObjFile.ParseInt(symbolLevel) is { } level)
                import.Set("speech/symbolLevel", nameof(VoxSettings.PunctuationLevel), PunctuationFor(level),
                    s => s.PunctuationLevel, (s, v) => s with { PunctuationLevel = v });
            else
                import.Report.AddSkipped("speech/symbolLevel", symbolLevel, ImportSkipReason.InvalidValue);
        }

        // Rate, pitch, volume, voice and capitals are kept per synthesizer
        var synthSection = speech.Section(synth);
        if (synthSection is null)
            return;
        string prefix = $"speech/{synthSection.Name}/";

        if (synthSection.Value("voice") is { } voice)
        {
            string? match = engineAvailable ? VoiceMatcher.Match(voice, voices!) : null;
            if (match is not null)
                import.Set(prefix + "voice", nameof(VoxSettings.VoiceName), match, s => s.VoiceName, (s, v) => s with { VoiceName = v });
            else
                import.Report.AddSkipped(prefix + "voice", voice, engine is null ? ImportSkipReason.NoCounterpart : ImportSkipReason.NotInstalled);
        }

        bool rateBoost = ConfigObjFile.ParseBool(synthSection.Value("rateBoost")) ?? false;
        ImportPercent(synthSection, "rate", prefix, import, rate =>
            import.Set(prefix + "rate", nameof(VoxSettings.SpeechRateWpm), RateToWpm(synth, rate, rateBoost),
                s => s.SpeechRateWpm, (s, v) => s with { SpeechRateWpm = v }));
        ImportPercent(synthSection, "pitch", prefix, import, pitch =>
            import.Set(prefix + "pitch", nameof(VoxSettings.SpeechPitch), pitch, s => s.SpeechPitch, (s, v) => s with { SpeechPitch = v }));
        ImportPercent(synthSection, "volume", prefix, import, volume =>
            import.Set(prefix + "volume", nameof(VoxSettings.SpeechVolume), volume, s => s.SpeechVolume, (s, v) => s with { SpeechVolume = v }));

        ImportCapitals(synthSection, prefix, import);
    }

    private static void ImportPercent(ConfigObjSection section, string key, string prefix, ImportState import, Action<int> set)
    {
        if (section.Value(key) is not { } text)
            return;
        if (ConfigObjFile.ParseInt(text) is { } value)
            set(Math.Clamp(value, 0, 100));
        else
            import.Report.AddSkipped(prefix + key, text, ImportSkipReason.InvalidValue);
    }

    /// <summary>NVDA's symbol level (0 none, 100 some, 200 most, 300 all, 1000 character) as Vox's punctuation level.</summary>
    public static PunctuationLevel PunctuationFor(int symbolLevel) => symbolLevel switch
    {
        < 100 => PunctuationLevel.None,
        < 200 => PunctuationLevel.Some,
        < 300 => PunctuationLevel.Most,
        _ => PunctuationLevel.All,
    };

    /// <summary>
    /// NVDA's rate (0–100 per synthesizer) as words per minute, approximately: eSpeak goes from
    /// 80 to 450 words a minute; SAPI and OneCore are at their normal speed (about 180) at 50 and
    /// three times faster or slower at the ends. Rate boost triples it. Kept within what Vox supports.
    /// </summary>
    public static int RateToWpm(string synth, int rate, bool rateBoost)
    {
        rate = Math.Clamp(rate, 0, 100);
        double wpm = synth.Equals("espeak", StringComparison.OrdinalIgnoreCase)
            ? 80 + rate * 3.7
            : 180 * Math.Pow(3, (rate - 50) / 50.0);
        if (rateBoost)
            wpm *= 3;
        return Math.Clamp((int)Math.Round(wpm / 5) * 5, ISpeechEngine.MinSupportedWpm, ISpeechEngine.MaxSupportedWpm);
    }

    private static void ImportCapitals(ConfigObjSection section, string prefix, ImportState import)
    {
        string? sayCap = section.Value("sayCapForCapitals");
        string? beep = section.Value("beepForCapitals");
        string? pitchChange = section.Value("capPitchChange");
        if (sayCap is null && beep is null && pitchChange is null)
            return;

        bool? say = sayCap is null ? false : ConfigObjFile.ParseBool(sayCap);
        bool? beeps = beep is null ? false : ConfigObjFile.ParseBool(beep);
        // NVDA raises the pitch by 30 % unless told otherwise
        int? pitch = pitchChange is null ? 30 : ConfigObjFile.ParseInt(pitchChange);
        if (say is null || beeps is null || pitch is null)
        {
            import.Report.AddSkipped(prefix + "capitals", string.Join(", ", new[] { sayCap, beep, pitchChange }.Where(v => v is not null)),
                ImportSkipReason.InvalidValue);
            return;
        }

        var indication = say.Value ? CapitalIndication.SayCap
            : beeps.Value ? CapitalIndication.Beep
            : pitch.Value != 0 ? CapitalIndication.Pitch
            : CapitalIndication.Off;
        import.Set(prefix + "capitals", nameof(VoxSettings.CapitalsForCharacters), indication,
            s => s.CapitalsForCharacters, (s, v) => s with { CapitalsForCharacters = v });
    }

    private static void ImportKeyboard(ConfigObjSection root, ImportState import)
    {
        var keyboard = root.Section("keyboard");
        if (keyboard is null)
            return;

        // Typing echo: True/False in older NVDA, 0 (off) / 1 (in edit controls) / 2 (always) in newer
        string? chars = keyboard.Value("speakTypedCharacters");
        string? words = keyboard.Value("speakTypedWords");
        if (chars is not null || words is not null)
        {
            bool? echoChars = chars is null ? true : EchoOn(chars);
            bool? echoWords = words is null ? false : EchoOn(words);
            if (echoChars is null || echoWords is null)
                import.Report.AddSkipped("keyboard/speakTypedCharacters", string.Join(", ", new[] { chars, words }.Where(v => v is not null)),
                    ImportSkipReason.InvalidValue);
            else
            {
                var mode = (echoChars.Value, echoWords.Value) switch
                {
                    (true, true) => TypingEchoMode.Both,
                    (true, false) => TypingEchoMode.Characters,
                    (false, true) => TypingEchoMode.Words,
                    _ => TypingEchoMode.None,
                };
                import.Set("keyboard/speakTyped", nameof(VoxSettings.TypingEchoMode), mode,
                    s => s.TypingEchoMode, (s, v) => s with { TypingEchoMode = v });
            }
        }

        if (keyboard.Value("keyboardLayout") is { } layoutText)
        {
            KeyboardLayout? layout = layoutText.Trim().ToLowerInvariant() switch
            {
                "desktop" => KeyboardLayout.Desktop,
                "laptop" => KeyboardLayout.Laptop,
                _ => null,
            };
            if (layout is { } l)
                import.Set("keyboard/keyboardLayout", nameof(VoxSettings.KeyboardLayout), l, s => s.KeyboardLayout, (s, v) => s with { KeyboardLayout = v });
            else
                import.Report.AddSkipped("keyboard/keyboardLayout", layoutText, ImportSkipReason.InvalidValue);
        }

        // Vox has one screen reader key: CapsLock only when NVDA uses it and not Insert
        if (keyboard.Value("useCapsLockAsNVDAModifierKey") is { } capsText)
        {
            bool? caps = ConfigObjFile.ParseBool(capsText);
            bool insert = (ConfigObjFile.ParseBool(keyboard.Value("useExtendedInsertAsNVDAModifierKey")) ?? true)
                || (ConfigObjFile.ParseBool(keyboard.Value("useNumpadInsertAsNVDAModifierKey")) ?? true);
            if (caps is null)
                import.Report.AddSkipped("keyboard/useCapsLockAsNVDAModifierKey", capsText, ImportSkipReason.InvalidValue);
            else if (caps.Value && insert)
                import.Report.AddSkipped("keyboard/useCapsLockAsNVDAModifierKey", capsText, ImportSkipReason.Unsupported,
                    nameof(ModifierKey.Insert));
            else
            {
                var key = caps.Value ? ModifierKey.CapsLock : ModifierKey.Insert;
                import.Set("keyboard/useCapsLockAsNVDAModifierKey", nameof(VoxSettings.ModifierKey), key,
                    s => s.ModifierKey, (s, v) => s with { ModifierKey = v });
            }
        }
    }

    private static bool? EchoOn(string value) =>
        ConfigObjFile.ParseInt(value) is { } n ? n != 0 : ConfigObjFile.ParseBool(value);

    private static void ImportBrowseMode(ConfigObjSection root, ImportState import)
    {
        var buffers = root.Section("virtualBuffers");
        if (buffers is null)
            return;
        if (buffers.Value("maxLineLength") is { } lengthText)
        {
            if (ConfigObjFile.ParseInt(lengthText) is { } length && length >= 0)
                import.Set("virtualBuffers/maxLineLength", nameof(VoxSettings.MaxLineLength), length,
                    s => s.MaxLineLength, (s, v) => s with { MaxLineLength = v });
            else
                import.Report.AddSkipped("virtualBuffers/maxLineLength", lengthText, ImportSkipReason.InvalidValue);
        }
        ImportBool(buffers, "useScreenLayout", "virtualBuffers/", nameof(VoxSettings.ScreenLayout), import,
            s => s.ScreenLayout, (s, v) => s with { ScreenLayout = v });
    }

    private static void ImportBraille(ConfigObjSection root, ImportState import)
    {
        var braille = root.Section("braille");
        if (braille is null)
            return;
        const string prefix = "braille/";

        if (braille.Value("display") is { } display)
        {
            string? driver = display.Trim().ToLowerInvariant() switch
            {
                "auto" => null,
                "nobraille" => BrailleDisplayManager.NoneId,
                "hid" or "hidbraillestandard" => "HID",
                "brltty" => "BRLTTY",
                _ => "?",
            };
            if (driver == "?")
                // Vox still finds HID and BRLTTY displays by itself
                import.Report.AddSkipped(prefix + "display", display, ImportSkipReason.NoCounterpart);
            else
                import.Set(prefix + "display", nameof(VoxSettings.BrailleDisplay), driver, s => s.BrailleDisplay, (s, v) => s with { BrailleDisplay = v });
        }

        if (braille.Value("translationTable") is { } tableName)
        {
            if (BrailleTables.Find(tableName) is { } table)
                import.Set(prefix + "translationTable", nameof(VoxSettings.BrailleTable), table.Id, s => s.BrailleTable, (s, v) => s with { BrailleTable = v });
            else
                import.Report.AddSkipped(prefix + "translationTable", tableName, ImportSkipReason.Unsupported);
        }

        ImportChoice(braille, "tetherTo", prefix, nameof(VoxSettings.BrailleTether), import,
            v => v switch
            {
                "auto" => BrailleTether.Automatic,
                "focus" => BrailleTether.Focus,
                "review" => BrailleTether.Review,
                _ => null,
            },
            s => s.BrailleTether, (s, v) => s with { BrailleTether = v });
        ImportChoice(braille, "focusContextPresentation", prefix, nameof(VoxSettings.BrailleFocusContext), import,
            v => v switch
            {
                "fill" => BrailleFocusContext.FillDisplay,
                "scroll" => BrailleFocusContext.ScrollBackOnly,
                "changedcontext" => BrailleFocusContext.ChangedContext,
                _ => null,
            },
            s => s.BrailleFocusContext, (s, v) => s with { BrailleFocusContext = v });
        // The focus cursor's dots as a bit mask: 0xC0 dots 7 and 8, 0x80 dot 8, 0xFF all
        ImportChoice(braille, "cursorShapeFocus", prefix, nameof(VoxSettings.BrailleCursorShape), import,
            v => ConfigObjFile.ParseInt(v) switch
            {
                0xC0 => BrailleCursorShape.Underline,
                0x80 => BrailleCursorShape.Dot8,
                0xFF => BrailleCursorShape.AllDots,
                _ => (BrailleCursorShape?)null,
            },
            s => s.BrailleCursorShape, (s, v) => s with { BrailleCursorShape = v });

        ImportBool(braille, "cursorBlink", prefix, nameof(VoxSettings.BrailleCursorBlink), import,
            s => s.BrailleCursorBlink, (s, v) => s with { BrailleCursorBlink = v });
        ImportBool(braille, "showSelection", prefix, nameof(VoxSettings.BrailleShowSelection), import,
            s => s.BrailleShowSelection, (s, v) => s with { BrailleShowSelection = v });
        ImportBool(braille, "expandAtCursor", prefix, nameof(VoxSettings.BrailleExpandWordAtCursor), import,
            s => s.BrailleExpandWordAtCursor, (s, v) => s with { BrailleExpandWordAtCursor = v });
        ImportBool(braille, "wordWrap", prefix, nameof(VoxSettings.BrailleWordWrap), import,
            s => s.BrailleWordWrap, (s, v) => s with { BrailleWordWrap = v });

        if (braille.Value("cursorBlinkRate") is { } rateText)
        {
            if (ConfigObjFile.ParseInt(rateText) is { } rate && rate > 0)
                import.Set(prefix + "cursorBlinkRate", nameof(VoxSettings.BrailleCursorBlinkRateMs), Math.Clamp(rate, 200, 2000),
                    s => s.BrailleCursorBlinkRateMs, (s, v) => s with { BrailleCursorBlinkRateMs = v });
            else
                import.Report.AddSkipped(prefix + "cursorBlinkRate", rateText, ImportSkipReason.InvalidValue);
        }

        // Messages: showMessages 0 (newer NVDA) turns them off; else messageTimeout seconds
        if (ConfigObjFile.ParseInt(braille.Value("showMessages")) == 0)
            import.Set(prefix + "showMessages", nameof(VoxSettings.BrailleMessageTimeoutSeconds), 0,
                s => s.BrailleMessageTimeoutSeconds, (s, v) => s with { BrailleMessageTimeoutSeconds = v });
        else if (braille.Value("messageTimeout") is { } timeoutText)
        {
            if (ConfigObjFile.ParseInt(timeoutText) is { } timeout && timeout >= 0)
                import.Set(prefix + "messageTimeout", nameof(VoxSettings.BrailleMessageTimeoutSeconds), Math.Min(timeout, 20),
                    s => s.BrailleMessageTimeoutSeconds, (s, v) => s with { BrailleMessageTimeoutSeconds = v });
            else
                import.Report.AddSkipped(prefix + "messageTimeout", timeoutText, ImportSkipReason.InvalidValue);
        }
    }

    private static void ImportBool(ConfigObjSection section, string key, string prefix, string target, ImportState import,
        Func<VoxSettings, bool> get, Func<VoxSettings, bool, VoxSettings> with)
    {
        if (section.Value(key) is not { } text)
            return;
        if (ConfigObjFile.ParseBool(text) is { } value)
            import.Set(prefix + key, target, value, get, with);
        else
            import.Report.AddSkipped(prefix + key, text, ImportSkipReason.InvalidValue);
    }

    private static void ImportChoice<T>(ConfigObjSection section, string key, string prefix, string target, ImportState import,
        Func<string, T?> parse, Func<VoxSettings, T> get, Func<VoxSettings, T, VoxSettings> with) where T : struct
    {
        if (section.Value(key) is not { } text)
            return;
        if (parse(text.Trim().ToLowerInvariant()) is { } value)
            import.Set(prefix + key, target, value, get, with);
        else
            import.Report.AddSkipped(prefix + key, text, ImportSkipReason.Unsupported);
    }

    // The settings being built and the report of each change
    private sealed class ImportState(VoxSettings settings, ImportReport report)
    {
        public VoxSettings Settings { get; private set; } = settings;

        public ImportReport Report { get; } = report;

        public void Set<T>(string source, string target, T value, Func<VoxSettings, T> get, Func<VoxSettings, T, VoxSettings> with)
        {
            Report.AddImported(source, target, value, get(Settings));
            Settings = with(Settings, value);
        }
    }
}
