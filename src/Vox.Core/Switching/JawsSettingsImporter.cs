using System.Globalization;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Speech;

namespace Vox.Core.Switching;

/// <summary>A JAWS dictionary entry and the voice it is for (null: every voice).</summary>
public sealed record JawsDictionaryEntry(ImportedEntry Entry, string? Voice);

/// <summary>
/// Brings JAWS's user settings over from the newest JAWS version's settings folder
/// (<c>%APPDATA%\Freedom Scientific\JAWS\&lt;version&gt;\Settings\&lt;language&gt;</c>):
/// key assignments in <c>default.jkm</c> and the browsers' <c>.jkm</c> files for scripts Vox has a
/// command for (into the user keymap), dictionary entries from <c>.jdf</c> files (into Vox's
/// dictionaries), and the voice rate, punctuation and typing echo from <c>default.jcf</c>. Only key
/// assignments are taken, never script logic. Nothing is saved here: settings are returned and
/// Vox's files planned, as <see cref="NvdaSettingsImporter"/> does.
/// </summary>
public sealed class JawsSettingsImporter
{
    public const string ConfigFileName = "default.jcf";
    public const string KeyMapFileName = "default.jkm";
    public const string DictionaryFileName = "default.jdf";

    // Applications whose .jkm keys are for web pages, which Vox's browse-mode keys cover
    private static readonly string[] BrowserKeyMaps = ["chrome.jkm", "msedge.jkm", "firefox.jkm"];

    // JAWS's script names → Vox's commands
    private static readonly Dictionary<string, NavigationCommand> Scripts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SayAll"] = NavigationCommand.SayAll,
        ["SayLine"] = NavigationCommand.ReadCurrentLine,
        ["SayWord"] = NavigationCommand.ReadCurrentWord,
        ["SayCharacter"] = NavigationCommand.ReadCurrentChar,
        ["SayNextLine"] = NavigationCommand.NextLine, ["SayPriorLine"] = NavigationCommand.PrevLine,
        ["SayNextWord"] = NavigationCommand.NextWord, ["SayPriorWord"] = NavigationCommand.PrevWord,
        ["SayNextCharacter"] = NavigationCommand.NextChar, ["SayPriorCharacter"] = NavigationCommand.PrevChar,
        ["NextParagraph"] = NavigationCommand.NextParagraph, ["PriorParagraph"] = NavigationCommand.PrevParagraph,
        ["JAWSHome"] = NavigationCommand.StartOfLine, ["JAWSEnd"] = NavigationCommand.EndOfLine,
        ["JAWSTopOfFile"] = NavigationCommand.TopOfDocument, ["JAWSBottomOfFile"] = NavigationCommand.BottomOfDocument,
        ["SaySelectedText"] = NavigationCommand.ReadSelection,
        ["SayFont"] = NavigationCommand.ReadFormatting,
        ["SayWindowTitle"] = NavigationCommand.SayTitle,
        ["SayWindowPromptAndText"] = NavigationCommand.SayFocus,
        ["SayBottomLineOfWindow"] = NavigationCommand.SayStatusBar,
        ["SayTime"] = NavigationCommand.SayTime,
        ["SayWindow"] = NavigationCommand.ReadWindow,
        ["KeyboardHelp"] = NavigationCommand.ToggleInputHelp,
        ["JAWSWindow"] = NavigationCommand.OpenVoxMenu,
        ["QuickSettings"] = NavigationCommand.OpenSettings,
        ["VirtualPCCursorToggle"] = NavigationCommand.ToggleMode,
        ["JAWSFind"] = NavigationCommand.Find, ["VirtualFind"] = NavigationCommand.Find,
        ["JAWSFindNext"] = NavigationCommand.FindNext, ["JAWSFindPrior"] = NavigationCommand.FindPrevious,
        ["SelectALink"] = NavigationCommand.ElementsList,
        ["SelectAHeading"] = NavigationCommand.ElementsList,
        ["SelectAFormControl"] = NavigationCommand.ElementsList,
        ["MoveToNextHeading"] = NavigationCommand.NextHeading, ["MoveToPriorHeading"] = NavigationCommand.PrevHeading,
        ["MoveToNextHeadingLevel1"] = NavigationCommand.HeadingLevel1, ["MoveToPriorHeadingLevel1"] = NavigationCommand.PrevHeadingLevel1,
        ["MoveToNextHeadingLevel2"] = NavigationCommand.HeadingLevel2, ["MoveToPriorHeadingLevel2"] = NavigationCommand.PrevHeadingLevel2,
        ["MoveToNextHeadingLevel3"] = NavigationCommand.HeadingLevel3, ["MoveToPriorHeadingLevel3"] = NavigationCommand.PrevHeadingLevel3,
        ["MoveToNextHeadingLevel4"] = NavigationCommand.HeadingLevel4, ["MoveToPriorHeadingLevel4"] = NavigationCommand.PrevHeadingLevel4,
        ["MoveToNextHeadingLevel5"] = NavigationCommand.HeadingLevel5, ["MoveToPriorHeadingLevel5"] = NavigationCommand.PrevHeadingLevel5,
        ["MoveToNextHeadingLevel6"] = NavigationCommand.HeadingLevel6, ["MoveToPriorHeadingLevel6"] = NavigationCommand.PrevHeadingLevel6,
        ["MoveToNextVisitedLink"] = NavigationCommand.NextVisitedLink, ["MoveToPriorVisitedLink"] = NavigationCommand.PrevVisitedLink,
        ["MoveToNextUnvisitedLink"] = NavigationCommand.NextUnvisitedLink, ["MoveToPriorUnvisitedLink"] = NavigationCommand.PrevUnvisitedLink,
        ["MoveToNextRegion"] = NavigationCommand.NextLandmark, ["MoveToPriorRegion"] = NavigationCommand.PrevLandmark,
        ["MoveToMainRegion"] = NavigationCommand.MoveToMainContent,
        ["MoveToNextFormField"] = NavigationCommand.NextFormField, ["MoveToPriorFormField"] = NavigationCommand.PrevFormField,
        ["MoveToNextTable"] = NavigationCommand.NextTable, ["MoveToPriorTable"] = NavigationCommand.PrevTable,
        ["MoveToNextButton"] = NavigationCommand.NextButton, ["MoveToPriorButton"] = NavigationCommand.PrevButton,
        ["MoveToNextEdit"] = NavigationCommand.NextEdit, ["MoveToPriorEdit"] = NavigationCommand.PrevEdit,
        ["MoveToNextComboBox"] = NavigationCommand.NextComboBox, ["MoveToPriorComboBox"] = NavigationCommand.PrevComboBox,
        ["MoveToNextCheckBox"] = NavigationCommand.NextCheckBox, ["MoveToPriorCheckBox"] = NavigationCommand.PrevCheckBox,
        ["MoveToNextRadioButton"] = NavigationCommand.NextRadioButton, ["MoveToPriorRadioButton"] = NavigationCommand.PrevRadioButton,
        ["MoveToNextList"] = NavigationCommand.NextList, ["MoveToPriorList"] = NavigationCommand.PrevList,
        ["MoveToNextListItem"] = NavigationCommand.NextListItem, ["MoveToPriorListItem"] = NavigationCommand.PrevListItem,
        ["MoveToNextGraphic"] = NavigationCommand.NextGraphic, ["MoveToPriorGraphic"] = NavigationCommand.PrevGraphic,
        ["MoveToNextBlockQuote"] = NavigationCommand.NextBlockQuote, ["MoveToPriorBlockQuote"] = NavigationCommand.PrevBlockQuote,
        ["MoveToNextFrame"] = NavigationCommand.NextFrame, ["MoveToPriorFrame"] = NavigationCommand.PrevFrame,
        ["MoveToNextSeparator"] = NavigationCommand.NextSeparator, ["MoveToPriorSeparator"] = NavigationCommand.PrevSeparator,
        ["MoveToNextObject"] = NavigationCommand.NextEmbeddedObject, ["MoveToPriorObject"] = NavigationCommand.PrevEmbeddedObject,
        ["MoveToNextNonLinkText"] = NavigationCommand.NextNonLinkText, ["MoveToPriorNonLinkText"] = NavigationCommand.PrevNonLinkText,
        ["MoveToNextParagraph"] = NavigationCommand.NextTextParagraph, ["MoveToPriorParagraph"] = NavigationCommand.PrevTextParagraph,
        ["MoveToEndOfElement"] = NavigationCommand.EndOfContainer, ["MoveToStartOfElement"] = NavigationCommand.StartOfContainer,
        ["NextCell"] = NavigationCommand.TableNextColumn, ["PriorCell"] = NavigationCommand.TablePrevColumn,
        ["DownCell"] = NavigationCommand.TableNextRow, ["UpCell"] = NavigationCommand.TablePrevRow,
        ["ReadCurrentRow"] = NavigationCommand.ReadTableRow, ["ReadCurrentColumn"] = NavigationCommand.ReadTableColumn,
    };

    private readonly Func<string, IReadOnlyList<string>?> _voicesForEngine;

    /// <param name="voicesForEngine">The voices of a Vox speech engine by id, or null when it isn't installed.</param>
    public JawsSettingsImporter(Func<string, IReadOnlyList<string>?> voicesForEngine)
    {
        _voicesForEngine = voicesForEngine;
    }

    /// <summary>Where JAWS keeps each version's user settings for the current user.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Freedom Scientific", "JAWS");

    /// <summary>
    /// The newest JAWS version's user settings folder under <paramref name="root"/> (the version
    /// folder with the highest number, "2025" over "2024" over "18.0"; English, "enu", when there
    /// are several languages), or null when there is none.
    /// </summary>
    public static string? FindSettingsFolder(string? root = null)
    {
        root ??= DefaultRoot;
        if (!Directory.Exists(root))
            return null;
        try
        {
            var versions = Directory.GetDirectories(root)
                .Select(d => (Path: d, Version: double.TryParse(Path.GetFileName(d), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : -1))
                .Where(v => v.Version >= 0)
                .OrderByDescending(v => v.Version);
            foreach (var (path, _) in versions)
            {
                string settings = Path.Combine(path, "Settings");
                if (!Directory.Exists(settings))
                    continue;
                var languages = Directory.GetDirectories(settings).Order(StringComparer.OrdinalIgnoreCase).ToList();
                var language = languages.FirstOrDefault(l => Path.GetFileName(l).Equals("enu", StringComparison.OrdinalIgnoreCase))
                    ?? languages.FirstOrDefault();
                if (language is not null)
                    return language;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>
    /// Imports a JAWS user settings folder: default.jcf into the settings, the key maps into the
    /// user keymap and the dictionaries into Vox's dictionaries (planned, not written).
    /// </summary>
    /// <param name="voxUserFolder">Vox's user data folder, where its keymap and dictionaries are.</param>
    /// <param name="layoutBindings">The bindings of a Vox keyboard layout (<see cref="KeyMap.LayoutBindings"/>).</param>
    public SettingsImportResult ImportFolder(string settingsFolder, VoxSettings current, string voxUserFolder,
        Func<KeyboardLayout, IReadOnlyList<KeyBinding>> layoutBindings)
    {
        var report = new ImportReport(ImportSource.Jaws);
        var settings = current;
        var files = new List<PlannedFile>();

        if (ImportFileReader.Read(Path.Combine(settingsFolder, ConfigFileName), report) is { } jcf)
            settings = ImportConfiguration(jcf, current, report);

        var keys = new List<ImportedKey>();
        var keyMaps = SafeFiles(settingsFolder, "*.jkm");
        foreach (var file in keyMaps)
        {
            string name = Path.GetFileName(file);
            if (name.Equals(KeyMapFileName, StringComparison.OrdinalIgnoreCase) || BrowserKeyMaps.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                if (ImportFileReader.Read(file, report) is { } text)
                    keys.AddRange(ParseKeyMap(text, settings.KeyboardLayout, report, name));
            }
            else
                // Keys for one application: Vox has no per-application keys of the user's
                report.AddSkipped(name, null, ImportSkipReason.NoCounterpart);
        }
        if (keys.Count > 0 && KeymapImport.PlanFile(voxUserFolder, layoutBindings(settings.KeyboardLayout), keys, report) is { } keymap)
            files.Add(keymap);

        string voxDictionaries = Path.Combine(voxUserFolder, PronunciationRule.FolderName);
        var allVoices = new[] { SpeechEngineRegistry.OneCoreId, SpeechEngineRegistry.SapiId, SpeechEngineRegistry.EspeakId }
            .SelectMany(engine => _voicesForEngine(engine) ?? []).ToList();
        foreach (var file in SafeFiles(settingsFolder, "*.jdf"))
        {
            if (ImportFileReader.Read(file, report) is not { } text)
                continue;
            var entries = ParseDictionary(text, Path.GetFileName(file), report);
            DictionaryImport.Plan(Path.Combine(voxDictionaries, PronunciationRule.UserFileName),
                entries.Where(e => e.Voice is null).Select(e => e.Entry).ToList(), voxUserFolder, report, files);
            foreach (var group in entries.Where(e => e.Voice is not null).GroupBy(e => e.Voice!, StringComparer.OrdinalIgnoreCase))
            {
                if (VoiceMatcher.Match(group.Key, allVoices) is { } voice)
                    DictionaryImport.Plan(PronunciationRule.VoiceFile(voxDictionaries, voice), group.Select(e => e.Entry).ToList(),
                        voxUserFolder, report, files);
                else
                {
                    foreach (var entry in group)
                        report.AddSkipped(entry.Entry.Source, entry.Entry.Entry.Pattern, ImportSkipReason.NotInstalled, group.Key);
                }
            }
        }

        return new SettingsImportResult(settings, report) { Files = files };
    }

    private static IEnumerable<string> SafeFiles(string folder, string pattern)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // JAWS's INI files have ";" comments, which configobj doesn't; as "#" the line numbers stay
    private static ConfigObjFile ParseIni(string text) =>
        ConfigObjFile.Parse(string.Join('\n', text.Split('\n').Select(l => l.TrimStart().StartsWith(';') ? "#" + l : l)));

    /// <summary>
    /// The speech settings of a default.jcf: <c>Rate</c> (percent), <c>Punctuation</c> (0 none,
    /// 1 some, 2 most, 3 all) and <c>TypingEcho</c> (0 off, 1 characters, 2 words, 3 both), from
    /// its <c>[Options]</c> section (else any section that has them).
    /// </summary>
    public static VoxSettings ImportConfiguration(string jcfText, VoxSettings current, ImportReport report, string fileName = ConfigFileName)
    {
        var file = ParseIni(jcfText);
        report.AddMalformed(fileName, file.Malformed);
        var import = new ImportState(current, report);

        var sections = file.Root.Sections.Values
            .OrderByDescending(s => s.Name.Equals("Options", StringComparison.OrdinalIgnoreCase)).ToList();
        string? Find(string key, out string source)
        {
            foreach (var section in sections)
            {
                if (section.Value(key) is { } value)
                {
                    source = $"{section.Name}/{key}";
                    return value;
                }
            }
            source = key;
            return null;
        }

        if (Find("Rate", out var rateSource) is { } rateText)
        {
            if (ConfigObjFile.ParseInt(rateText) is { } rate)
                import.Set(rateSource, nameof(VoxSettings.SpeechRateWpm), NvdaSettingsImporter.RateToWpm("sapi5", rate, rateBoost: false),
                    s => s.SpeechRateWpm, (s, v) => s with { SpeechRateWpm = v });
            else
                report.AddSkipped(rateSource, rateText, ImportSkipReason.InvalidValue);
        }

        if (Find("Punctuation", out var punctuationSource) is { } punctuationText)
        {
            PunctuationLevel? level = ConfigObjFile.ParseInt(punctuationText) switch
            {
                0 => PunctuationLevel.None,
                1 => PunctuationLevel.Some,
                2 => PunctuationLevel.Most,
                3 => PunctuationLevel.All,
                _ => null,
            };
            if (level is { } l)
                import.Set(punctuationSource, nameof(VoxSettings.PunctuationLevel), l, s => s.PunctuationLevel, (s, v) => s with { PunctuationLevel = v });
            else
                report.AddSkipped(punctuationSource, punctuationText, ImportSkipReason.InvalidValue);
        }

        if (Find("TypingEcho", out var echoSource) is { } echoText)
        {
            TypingEchoMode? mode = ConfigObjFile.ParseInt(echoText) switch
            {
                0 => TypingEchoMode.None,
                1 => TypingEchoMode.Characters,
                2 => TypingEchoMode.Words,
                3 => TypingEchoMode.Both,
                _ => null,
            };
            if (mode is { } m)
                import.Set(echoSource, nameof(VoxSettings.TypingEchoMode), m, s => s.TypingEchoMode, (s, v) => s with { TypingEchoMode = v });
            else
                report.AddSkipped(echoSource, echoText, ImportSkipReason.InvalidValue);
        }
        return import.Settings;
    }

    /// <summary>
    /// The keys of a .jkm file for scripts Vox has: <c>Insert+T=SayWindowTitle</c> lines in
    /// <c>[Common Keys]</c> and the section of the keyboard layout Vox uses (<c>[Desktop Keys]</c> or
    /// <c>[Laptop Keys]</c>). Other sections (braille, touch), keys Vox can't bind and scripts with
    /// no command are reported as skipped.
    /// </summary>
    public static IReadOnlyList<ImportedKey> ParseKeyMap(string text, KeyboardLayout layout, ImportReport report, string fileName = KeyMapFileName)
    {
        var file = ParseIni(text);
        report.AddMalformed(fileName, file.Malformed);
        var keys = new List<ImportedKey>();
        string ownLayout = layout.IsLaptop() ? "laptop keys" : "desktop keys";
        foreach (var section in file.Root.Sections.Values)
        {
            string name = section.Name.Trim().ToLowerInvariant();
            bool isOtherLayout = name is "desktop keys" or "laptop keys" && name != ownLayout;
            bool isKeys = name == "common keys" || name == ownLayout;
            foreach (var (key, script) in section.Values)
            {
                string source = $"{fileName}: [{section.Name}] {key}";
                if (!isKeys)
                {
                    report.AddSkipped(source, script, isOtherLayout ? ImportSkipReason.Unsupported : ImportSkipReason.NoCounterpart);
                    continue;
                }
                if (!Scripts.TryGetValue(script.Trim(), out var command))
                {
                    report.AddSkipped(source, script, ImportSkipReason.NoCounterpart);
                    continue;
                }
                if (!TryParseKey(key, out var modifiers, out int vkCode, out var reason))
                {
                    report.AddSkipped(source, script, reason);
                    continue;
                }
                string mode = script.StartsWith("MoveTo", StringComparison.OrdinalIgnoreCase) || script.StartsWith("SelectA", StringComparison.OrdinalIgnoreCase)
                    ? "Browse" : "Any";
                keys.Add(new ImportedKey(source, command, modifiers, vkCode, mode));
            }
        }
        return keys;
    }

    /// <summary>
    /// The keys of a JAWS key name ("Insert+Control+F", "JAWSKey+NumPad5", "Shift+Semicolon"), or why
    /// Vox can't bind it. The JAWS key (Insert, JAWSKey, CapsLock) is Vox's screen reader key.
    /// </summary>
    public static bool TryParseKey(string text, out KeyModifiers modifiers, out int vkCode, out ImportSkipReason reason)
    {
        modifiers = KeyModifiers.None;
        vkCode = 0;
        reason = ImportSkipReason.InvalidValue;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "insert" or "jawskey" or "capslock": modifiers |= KeyModifiers.Insert; break;
                case "control" or "ctrl" or "leftcontrol" or "rightcontrol": modifiers |= KeyModifiers.Ctrl; break;
                case "alt" or "leftalt" or "rightalt": modifiers |= KeyModifiers.Alt; break;
                case "shift" or "leftshift" or "rightshift": modifiers |= KeyModifiers.Shift; break;
                case "windows" or "leftwindows" or "rightwindows":
                    reason = ImportSkipReason.Unsupported;
                    return false;
                default:
                    return false;
            }
        }
        if (VkCodeOf(parts[^1]) is not { } code)
            return false;
        vkCode = code;
        return true;
    }

    private static int? VkCodeOf(string key)
    {
        string name = key.ToLowerInvariant();
        if (name.Length == 1 && (char.IsAsciiLetter(name[0]) || char.IsAsciiDigit(name[0])))
            return char.ToUpperInvariant(name[0]);
        if (name.Length is 2 or 3 && name[0] == 'f' && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24)
            return 0x6F + f;
        if (name.Length == 7 && name.StartsWith("numpad", StringComparison.Ordinal) && char.IsAsciiDigit(name[6]))
            return 0x60 + (name[6] - '0');
        return name switch
        {
            "uparrow" => 38, "downarrow" => 40, "leftarrow" => 37, "rightarrow" => 39,
            "home" => 36, "end" => 35, "pageup" => 33, "pagedown" => 34,
            "enter" => 13, "escape" or "esc" => 27, "space" or "spacebar" => 32, "tab" => 9, "backspace" => 8,
            "delete" => 46, "applications" or "applicationskey" => 93, "printscreen" => 44, "pause" => 19,
            "semicolon" => 186, "equals" => 187, "comma" => 188, "dash" or "hyphen" or "minus" => 189, "period" => 190,
            "slash" => 191, "grave" or "graveaccent" => 192, "leftbracket" => 219, "backslash" => 220,
            "rightbracket" => 221, "apostrophe" or "quote" => 222,
            "numpadenter" => NumpadKeys.NumpadEnter, "numpadplus" => 107, "numpadminus" => 109,
            "numpadstar" or "numpadmultiply" => 106, "numpadslash" or "numpaddivide" => 111,
            "numpaddelete" or "numpadperiod" => 110, "numpadinsert" => 96,
            _ => null,
        };
    }

    /// <summary>
    /// The entries of a .jdf dictionary: one per line, fields separated by the line's first
    /// character (usually "."): <c>.word.replacement.voice.</c>, the voice empty or "*" for every
    /// voice. A "*" at the start or end of the word matches the rest of a word ("*ing"); otherwise
    /// the word matches whole, ignoring case. Lines starting with ";" are comments.
    /// </summary>
    public static IReadOnlyList<JawsDictionaryEntry> ParseDictionary(string text, string fileName, ImportReport report)
    {
        var entries = new List<JawsDictionaryEntry>();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r').TrimStart('﻿').Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;
            string source = $"{fileName}:{i + 1}";
            char delimiter = line[0];
            var fields = line[1..].Split(delimiter);
            if (char.IsLetterOrDigit(delimiter) || fields.Length < 2 || fields[0].Trim('*').Length == 0)
            {
                report.AddSkipped(source, line, ImportSkipReason.Malformed);
                continue;
            }
            string word = fields[0];
            string? voice = fields.Length > 2 && fields[2].Trim() is { Length: > 0 } v && v != "*" ? v : null;
            PronunciationEntry entry;
            if (word.StartsWith('*') || word.EndsWith('*'))
            {
                string core = System.Text.RegularExpressions.Regex.Escape(word.Trim('*'));
                string pattern = (word.StartsWith('*') ? @"\w*" : @"(?<!\w)") + core + (word.EndsWith('*') ? @"\w*" : @"(?!\w)");
                entry = new PronunciationEntry(pattern, fields[1].Replace("$", "$$"), PronunciationMatch.Regex);
            }
            else
                entry = new PronunciationEntry(word, fields[1], PronunciationMatch.Word);
            entries.Add(new JawsDictionaryEntry(new ImportedEntry(source, entry), voice));
        }
        return entries;
    }
}
