using Vox.Core.Configuration;
using Vox.Core.Input;

namespace Vox.Core.Switching;

/// <summary>
/// Reads NVDA's own key assignments (<c>%APPDATA%\nvda\gestures.ini</c>: a section per scriptable
/// class, <c>script = kb:NVDA+control+f, kb(laptop):NVDA+shift+f</c>) and turns those for commands
/// Vox has into <see cref="ImportedKey"/>s. Keys of another keyboard layout than the one Vox uses,
/// keys with the Windows key, braille and touch gestures, NVDA's removed keys ("None") and scripts
/// of applications or add-ons are reported as skipped.
/// </summary>
public static class NvdaGesturesImporter
{
    public const string FileName = "gestures.ini";

    // NVDA's script names → Vox's commands
    private static readonly Dictionary<string, NavigationCommand> Scripts = new(StringComparer.OrdinalIgnoreCase)
    {
        // Browse mode quick navigation
        ["nextHeading"] = NavigationCommand.NextHeading, ["previousHeading"] = NavigationCommand.PrevHeading,
        ["nextHeading1"] = NavigationCommand.HeadingLevel1, ["previousHeading1"] = NavigationCommand.PrevHeadingLevel1,
        ["nextHeading2"] = NavigationCommand.HeadingLevel2, ["previousHeading2"] = NavigationCommand.PrevHeadingLevel2,
        ["nextHeading3"] = NavigationCommand.HeadingLevel3, ["previousHeading3"] = NavigationCommand.PrevHeadingLevel3,
        ["nextHeading4"] = NavigationCommand.HeadingLevel4, ["previousHeading4"] = NavigationCommand.PrevHeadingLevel4,
        ["nextHeading5"] = NavigationCommand.HeadingLevel5, ["previousHeading5"] = NavigationCommand.PrevHeadingLevel5,
        ["nextHeading6"] = NavigationCommand.HeadingLevel6, ["previousHeading6"] = NavigationCommand.PrevHeadingLevel6,
        ["nextLink"] = NavigationCommand.NextLink, ["previousLink"] = NavigationCommand.PrevLink,
        ["nextVisitedLink"] = NavigationCommand.NextVisitedLink, ["previousVisitedLink"] = NavigationCommand.PrevVisitedLink,
        ["nextUnvisitedLink"] = NavigationCommand.NextUnvisitedLink, ["previousUnvisitedLink"] = NavigationCommand.PrevUnvisitedLink,
        ["nextLandmark"] = NavigationCommand.NextLandmark, ["previousLandmark"] = NavigationCommand.PrevLandmark,
        ["nextFormField"] = NavigationCommand.NextFormField, ["previousFormField"] = NavigationCommand.PrevFormField,
        ["nextTable"] = NavigationCommand.NextTable, ["previousTable"] = NavigationCommand.PrevTable,
        ["nextButton"] = NavigationCommand.NextButton, ["previousButton"] = NavigationCommand.PrevButton,
        ["nextEdit"] = NavigationCommand.NextEdit, ["previousEdit"] = NavigationCommand.PrevEdit,
        ["nextComboBox"] = NavigationCommand.NextComboBox, ["previousComboBox"] = NavigationCommand.PrevComboBox,
        ["nextCheckBox"] = NavigationCommand.NextCheckBox, ["previousCheckBox"] = NavigationCommand.PrevCheckBox,
        ["nextRadioButton"] = NavigationCommand.NextRadioButton, ["previousRadioButton"] = NavigationCommand.PrevRadioButton,
        ["nextList"] = NavigationCommand.NextList, ["previousList"] = NavigationCommand.PrevList,
        ["nextListItem"] = NavigationCommand.NextListItem, ["previousListItem"] = NavigationCommand.PrevListItem,
        ["nextGraphic"] = NavigationCommand.NextGraphic, ["previousGraphic"] = NavigationCommand.PrevGraphic,
        ["nextBlockQuote"] = NavigationCommand.NextBlockQuote, ["previousBlockQuote"] = NavigationCommand.PrevBlockQuote,
        ["nextFrame"] = NavigationCommand.NextFrame, ["previousFrame"] = NavigationCommand.PrevFrame,
        ["nextSeparator"] = NavigationCommand.NextSeparator, ["previousSeparator"] = NavigationCommand.PrevSeparator,
        ["nextEmbeddedObject"] = NavigationCommand.NextEmbeddedObject, ["previousEmbeddedObject"] = NavigationCommand.PrevEmbeddedObject,
        ["nextNotLinkBlock"] = NavigationCommand.NextNonLinkText, ["previousNotLinkBlock"] = NavigationCommand.PrevNonLinkText,
        ["nextTextParagraph"] = NavigationCommand.NextTextParagraph, ["previousTextParagraph"] = NavigationCommand.PrevTextParagraph,
        ["movePastEndOfContainer"] = NavigationCommand.EndOfContainer, ["moveToStartOfContainer"] = NavigationCommand.StartOfContainer,
        ["elementsList"] = NavigationCommand.ElementsList,
        ["activatePosition"] = NavigationCommand.ActivateElement,
        ["disablePassThrough"] = NavigationCommand.ExitFocusMode,
        ["toggleVirtualBufferPassThrough"] = NavigationCommand.ToggleMode,
        // Tables
        ["nextColumn"] = NavigationCommand.TableNextColumn, ["previousColumn"] = NavigationCommand.TablePrevColumn,
        ["nextRow"] = NavigationCommand.TableNextRow, ["previousRow"] = NavigationCommand.TablePrevRow,
        // Browse cursor and selection
        ["moveByCharacter_forward"] = NavigationCommand.NextChar, ["moveByCharacter_back"] = NavigationCommand.PrevChar,
        ["moveByWord_forward"] = NavigationCommand.NextWord, ["moveByWord_back"] = NavigationCommand.PrevWord,
        ["moveByLine_forward"] = NavigationCommand.NextLine, ["moveByLine_back"] = NavigationCommand.PrevLine,
        ["moveByParagraph_forward"] = NavigationCommand.NextParagraph, ["moveByParagraph_back"] = NavigationCommand.PrevParagraph,
        ["startOfLine"] = NavigationCommand.StartOfLine, ["endOfLine"] = NavigationCommand.EndOfLine,
        ["topOfDocument"] = NavigationCommand.TopOfDocument, ["bottomOfDocument"] = NavigationCommand.BottomOfDocument,
        ["selectCharacter_forward"] = NavigationCommand.SelectNextChar, ["selectCharacter_back"] = NavigationCommand.SelectPrevChar,
        ["selectWord_forward"] = NavigationCommand.SelectNextWord, ["selectWord_back"] = NavigationCommand.SelectPrevWord,
        ["selectLine_forward"] = NavigationCommand.SelectNextLine, ["selectLine_back"] = NavigationCommand.SelectPrevLine,
        ["selectToBeginningOfLine"] = NavigationCommand.SelectToStartOfLine, ["selectToEndOfLine"] = NavigationCommand.SelectToEndOfLine,
        ["selectToTopOfDocument"] = NavigationCommand.SelectToTop, ["selectToBottomOfDocument"] = NavigationCommand.SelectToBottom,
        ["selectAll"] = NavigationCommand.SelectAll, ["copyToClipboard"] = NavigationCommand.CopySelection,
        ["find"] = NavigationCommand.Find, ["findNext"] = NavigationCommand.FindNext, ["findPrevious"] = NavigationCommand.FindPrevious,
        // Global commands
        ["sayAll"] = NavigationCommand.SayAll,
        ["reportCurrentLine"] = NavigationCommand.ReadCurrentLine,
        ["reportCurrentSelection"] = NavigationCommand.ReadSelection,
        ["reportFormatting"] = NavigationCommand.ReadFormatting, ["reportOrShowFormattingAtCaret"] = NavigationCommand.ReadFormatting,
        ["title"] = NavigationCommand.SayTitle,
        ["reportCurrentFocus"] = NavigationCommand.SayFocus,
        ["reportStatusLine"] = NavigationCommand.SayStatusBar,
        ["dateTime"] = NavigationCommand.SayTime,
        ["say_battery_status"] = NavigationCommand.SayBattery,
        ["speakForeground"] = NavigationCommand.ReadWindow,
        ["reportLinkDestination"] = NavigationCommand.SayLinkUrl,
        ["toggleInputHelp"] = NavigationCommand.ToggleInputHelp,
        ["toggleCurrentAppSleepMode"] = NavigationCommand.ToggleSleepMode,
        ["showGui"] = NavigationCommand.OpenVoxMenu,
        ["activateGeneralSettingsDialog"] = NavigationCommand.OpenSettings,
        ["activateInputGesturesDialog"] = NavigationCommand.OpenInputGestures,
        ["activateConfigProfilesDialog"] = NavigationCommand.OpenProfiles,
        ["quit"] = NavigationCommand.Quit,
        ["checkForUpdate"] = NavigationCommand.CheckForUpdates,
        ["navigatorObject_devInfo"] = NavigationCommand.DeveloperInfo,
        ["navigatorObject_parent"] = NavigationCommand.NavigatorParent,
        ["navigatorObject_firstChild"] = NavigationCommand.NavigatorFirstChild,
        ["navigatorObject_previous"] = NavigationCommand.NavigatorPrevious,
        ["navigatorObject_next"] = NavigationCommand.NavigatorNext,
        ["navigatorObject_current"] = NavigationCommand.ReportNavigator,
        ["navigatorObject_toFocus"] = NavigationCommand.NavigatorToFocus,
        ["navigatorObject_moveFocus"] = NavigationCommand.FocusToNavigator,
        ["review_activate"] = NavigationCommand.ActivateNavigator,
        ["review_previousLine"] = NavigationCommand.ReviewPrevLine, ["review_currentLine"] = NavigationCommand.ReviewCurrentLine,
        ["review_nextLine"] = NavigationCommand.ReviewNextLine,
        ["review_previousWord"] = NavigationCommand.ReviewPrevWord, ["review_currentWord"] = NavigationCommand.ReviewCurrentWord,
        ["review_nextWord"] = NavigationCommand.ReviewNextWord,
        ["review_previousCharacter"] = NavigationCommand.ReviewPrevChar, ["review_currentCharacter"] = NavigationCommand.ReviewCurrentChar,
        ["review_nextCharacter"] = NavigationCommand.ReviewNextChar,
        ["review_top"] = NavigationCommand.ReviewTop, ["review_bottom"] = NavigationCommand.ReviewBottom,
        ["reviewMode_next"] = NavigationCommand.NextReviewMode, ["reviewMode_previous"] = NavigationCommand.PrevReviewMode,
        ["toggleMouseTracking"] = NavigationCommand.ToggleMouseTracking,
        ["moveMouseToNavigatorObject"] = NavigationCommand.RouteMouseToNavigator,
        ["leftMouseClick"] = NavigationCommand.MouseLeftClick, ["rightMouseClick"] = NavigationCommand.MouseRightClick,
        ["toggleLeftMouseButton"] = NavigationCommand.ToggleLeftMouseLock,
        ["nextSynthSetting"] = NavigationCommand.SettingsRingNext, ["previousSynthSetting"] = NavigationCommand.SettingsRingPrevious,
        ["increaseSynthSetting"] = NavigationCommand.SettingsRingIncrease, ["decreaseSynthSetting"] = NavigationCommand.SettingsRingDecrease,
        ["braille_scrollBack"] = NavigationCommand.BraillePanLeft, ["braille_scrollForward"] = NavigationCommand.BraillePanRight,
        ["braille_toggleTether"] = NavigationCommand.CycleBrailleTether,
        ["recognizeWithUwpOcr"] = NavigationCommand.RecognizeNavigator,
    };

    // Sections whose scripts Vox has: NVDA's global commands work everywhere, the rest in documents
    private static string? DefaultModeFor(string section)
    {
        string module = section.Split('.')[0];
        return module.ToLowerInvariant() switch
        {
            "globalcommands" => "Any",
            "browsemode" or "virtualbuffers" or "cursormanager" or "documentbase" or "documentnavigation" => "Browse",
            _ => null,
        };
    }

    /// <summary>The keys in a gestures.ini's text for commands Vox has, for <paramref name="layout"/>; the rest reported.</summary>
    public static IReadOnlyList<ImportedKey> Parse(string text, KeyboardLayout layout, ImportReport report, string fileName = FileName)
    {
        var file = ConfigObjFile.Parse(text);
        report.AddMalformed(fileName, file.Malformed);
        var keys = new List<ImportedKey>();
        foreach (var section in file.Root.Sections.Values)
        {
            string? mode = DefaultModeFor(section.Name);
            foreach (var (script, value) in section.Values)
            {
                string source = $"{section.Name}/{script}";
                if (script.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    // Keys NVDA's user took away from NVDA: Vox's own keys differ, so nothing to do
                    report.AddSkipped(source, value, ImportSkipReason.Unsupported, "removed key");
                    continue;
                }
                if (mode is null || !Scripts.TryGetValue(script, out var command))
                {
                    report.AddSkipped(source, value, ImportSkipReason.NoCounterpart);
                    continue;
                }
                foreach (var gesture in SplitGestures(value))
                {
                    if (TryParseGesture(gesture, layout, out var modifiers, out int vkCode, out var reason))
                        keys.Add(new ImportedKey($"{source} {gesture}", command, modifiers, vkCode, mode));
                    else
                        report.AddSkipped(source, gesture, reason);
                }
            }
        }
        return keys;
    }

    /// <summary>A gestures.ini value's gestures: "kb:NVDA+a, kb:NVDA+downArrow"; a comma key ("kb:NVDA+,") is kept.</summary>
    public static IReadOnlyList<string> SplitGestures(string value)
    {
        var gestures = new List<string>();
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != ',' || (i > 0 && value[i - 1] is ':' or '+'))
                continue;
            Add(value[start..i]);
            start = i + 1;
        }
        Add(value[start..]);
        return gestures;

        void Add(string gesture)
        {
            gesture = gesture.Trim().Trim('"', '\'');
            if (gesture.Length > 0)
                gestures.Add(gesture);
        }
    }

    /// <summary>
    /// The keys of one NVDA gesture ("kb:NVDA+control+f", "kb(laptop):NVDA+shift+["), or why Vox can't
    /// bind it: not a keyboard gesture, another layout, the Windows key, or a key Vox doesn't know.
    /// </summary>
    public static bool TryParseGesture(string gesture, KeyboardLayout layout, out KeyModifiers modifiers, out int vkCode,
        out ImportSkipReason reason)
    {
        modifiers = KeyModifiers.None;
        vkCode = 0;
        reason = ImportSkipReason.InvalidValue;
        int colon = gesture.IndexOf(':');
        if (colon < 0)
            return false;
        string kind = gesture[..colon].Trim().ToLowerInvariant();
        if (kind.StartsWith("kb(", StringComparison.Ordinal))
        {
            string gestureLayout = kind[3..].TrimEnd(')');
            bool laptop = layout == KeyboardLayout.Laptop;
            if (gestureLayout != (laptop ? "laptop" : "desktop"))
            {
                reason = ImportSkipReason.Unsupported;
                return false;
            }
        }
        else if (kind != "kb")
        {
            // Braille display, touch and other gestures
            reason = ImportSkipReason.NoCounterpart;
            return false;
        }

        string keys = gesture[(colon + 1)..].Trim();
        var parts = keys.EndsWith("++", StringComparison.Ordinal)
            ? [.. keys[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries), "+"]
            : keys.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "nvda": modifiers |= KeyModifiers.Insert; break;
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
        string key = parts[^1].Trim();
        if (key.StartsWith("numLock", StringComparison.OrdinalIgnoreCase))
        {
            // Num Lock digits type; Vox never binds them
            reason = ImportSkipReason.Unsupported;
            return false;
        }
        if (VkCodeOf(key) is not { } code)
            return false;
        vkCode = code;
        return true;
    }

    /// <summary>The virtual-key code (as Vox's keymap binds it) of an NVDA key name, or null.</summary>
    public static int? VkCodeOf(string key)
    {
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return c;
            return c switch
            {
                ';' => 186, '=' => 187, ',' => 188, '-' => 189, '.' => 190, '/' => 191, '`' => 192,
                '[' => 219, '\\' => 220, ']' => 221, '\'' => 222,
                _ => null,
            };
        }
        string name = key.ToLowerInvariant();
        if (name.Length is 2 or 3 && name[0] == 'f' && int.TryParse(name[1..], out int f) && f is >= 1 and <= 24)
            return 0x6F + f;
        if (name.Length == 7 && name.StartsWith("numpad", StringComparison.Ordinal) && char.IsAsciiDigit(name[6]))
            return 0x60 + (name[6] - '0');
        return name switch
        {
            "uparrow" => 38, "downarrow" => 40, "leftarrow" => 37, "rightarrow" => 39,
            "home" => 36, "end" => 35, "pageup" => 33, "pagedown" => 34,
            "enter" => 13, "escape" => 27, "space" => 32, "tab" => 9, "backspace" => 8,
            "delete" => 46, "insert" => 45, "applications" => 93, "printscreen" => 44, "pause" => 19,
            "numpadinsert" => 96, "numpaddelete" or "numpaddecimal" => 110, "numpadenter" => NumpadKeys.NumpadEnter,
            "numpadplus" => 107, "numpadminus" => 109, "numpadmultiply" => 106, "numpaddivide" => 111,
            _ => null,
        };
    }
}
