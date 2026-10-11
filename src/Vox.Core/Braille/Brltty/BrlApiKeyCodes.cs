namespace Vox.Core.Braille.Brltty;

/// <summary>
/// Turns BrlAPI key codes (brlapi_keycodes.h, brlapi_constants.h) into the key names Vox's braille
/// key maps use. BRLTTY has already resolved the display's chord into one command, so each key
/// code becomes one <see cref="BrailleKeyEventArgs"/>:
/// <list type="bullet">
/// <item>basic commands: one name from <see cref="Commands"/> ("lineUp", "panLeft"), unknown ones "cmd&lt;n&gt;";</item>
/// <item>routing keys (the ROUTE block): "routing", with <see cref="BrailleKeyEventArgs.RoutingCell"/> the 0-based cell;
/// other blocks that take a cell (CLIP_NEW, DESCCHAR, ...) likewise, with their name from <see cref="Blocks"/>;</item>
/// <item>braille input (the PASSDOTS block): "space" when the space bar was part of the chord, then
/// "dots" and the dots pressed in order ("dots125"); the space bar alone is just "space";</item>
/// <item>keyboard keys (keysyms): "key_" and the X keysym name ("key_BackSpace", "key_Return",
/// "key_F1"), and a typed character "char_" and the character ("char_a");</item>
/// </list>
/// Modifier flags come first as their own names ("shift", "control", "meta", "altGr", "gui", "upper").
/// </summary>
public static class BrlApiKeyCodes
{
    /// <summary><c>BRLAPI_KEY_FLAGS_MASK</c>: modifier and command flags in the high 32 bits.</summary>
    public const ulong FlagsMask = 0xFFFFFFFF_00000000;
    /// <summary><c>BRLAPI_KEY_FLAGS_SHIFT</c>.</summary>
    public const int FlagsShift = 32;
    /// <summary><c>BRLAPI_KEY_TYPE_MASK</c>.</summary>
    public const ulong TypeMask = 0x00000000_E0000000;
    /// <summary><c>BRLAPI_KEY_TYPE_CMD</c>: a BRLTTY command.</summary>
    public const ulong TypeCommand = 0x00000000_20000000;
    /// <summary><c>BRLAPI_KEY_TYPE_SYM</c>: an X keysym.</summary>
    public const ulong TypeSymbol = 0;
    /// <summary><c>BRLAPI_KEY_CODE_MASK</c>.</summary>
    public const ulong CodeMask = 0x00000000_1FFFFFFF;
    /// <summary><c>BRLAPI_KEY_CMD_BLK_MASK</c>: the command block.</summary>
    public const ulong BlockMask = 0x1FFF0000;
    /// <summary><c>BRLAPI_KEY_CMD_BLK_SHIFT</c>.</summary>
    public const int BlockShift = 16;
    /// <summary><c>BRLAPI_KEY_CMD_ARG_MASK</c>: the command's argument (a cell, dots, ...).</summary>
    public const ulong ArgumentMask = 0x0000FFFF;

    /// <summary><c>BRLAPI_DOTC</c>: the space bar (chord) in a PASSDOTS argument; dots 1–8 are bits 0–7.</summary>
    public const int DotChord = 0x100;

    /// <summary><c>BRLAPI_KEY_SYM_UNICODE</c>: a keysym that is a Unicode code point in its low 24 bits.</summary>
    public const ulong SymbolUnicode = 0x01000000;

    /// <summary><c>BRLAPI_KEY_CMD_ROUTE</c> block.</summary>
    public const int RouteBlock = 0x01;
    /// <summary><c>BRLAPI_KEY_CMD_PASSDOTS</c> block.</summary>
    public const int PassDotsBlock = 0x22;

    /// <summary>
    /// Basic commands (block 0, <c>BRLAPI_KEY_CMD_*</c> = <c>BRL_CMD_*</c> in brl_cmds.h): code,
    /// BRLTTY's name, the name Vox reports.
    /// </summary>
    public static readonly IReadOnlyList<(int Code, string Brltty, string Name)> Commands =
    [
        (1, "LNUP", "lineUp"),
        (2, "LNDN", "lineDown"),
        (3, "WINUP", "windowUp"),
        (4, "WINDN", "windowDown"),
        (5, "PRDIFLN", "previousDifferentLine"),
        (6, "NXDIFLN", "nextDifferentLine"),
        (7, "ATTRUP", "attributeUp"),
        (8, "ATTRDN", "attributeDown"),
        (9, "TOP", "top"),
        (10, "BOT", "bottom"),
        (11, "TOP_LEFT", "topLeft"),
        (12, "BOT_LEFT", "bottomLeft"),
        (13, "PRPGRPH", "previousParagraph"),
        (14, "NXPGRPH", "nextParagraph"),
        (15, "PRPROMPT", "previousPrompt"),
        (16, "NXPROMPT", "nextPrompt"),
        (17, "PRSEARCH", "previousSearch"),
        (18, "NXSEARCH", "nextSearch"),
        (19, "CHRLT", "charLeft"),
        (20, "CHRRT", "charRight"),
        (21, "HWINLT", "halfPanLeft"),
        (22, "HWINRT", "halfPanRight"),
        (23, "FWINLT", "panLeft"),
        (24, "FWINRT", "panRight"),
        (25, "FWINLTSKIP", "panLeftSkip"),
        (26, "FWINRTSKIP", "panRightSkip"),
        (27, "LNBEG", "lineBegin"),
        (28, "LNEND", "lineEnd"),
        (29, "HOME", "home"),
        (30, "BACK", "back"),
        (31, "RETURN", "return"),
        (32, "FREEZE", "freeze"),
        (33, "DISPMD", "displayMode"),
        (34, "SIXDOTS", "sixDots"),
        (35, "SLIDEWIN", "slideWindow"),
        (36, "SKPIDLNS", "skipIdenticalLines"),
        (37, "SKPBLNKWINS", "skipBlankWindows"),
        (38, "CSRVIS", "cursorVisible"),
        (39, "CSRHIDE", "cursorHide"),
        (40, "CSRTRK", "toggleTether"),
        (41, "CSRSIZE", "cursorSize"),
        (42, "CSRBLINK", "cursorBlink"),
        (43, "ATTRVIS", "attributesVisible"),
        (44, "ATTRBLINK", "attributesBlink"),
        (45, "CAPBLINK", "capitalsBlink"),
        (46, "TUNES", "tunes"),
        (47, "AUTOREPEAT", "autorepeat"),
        (48, "AUTOSPEAK", "autospeak"),
        (49, "HELP", "help"),
        (50, "INFO", "info"),
        (51, "LEARN", "learn"),
        (52, "PREFMENU", "preferencesMenu"),
        (53, "PREFSAVE", "preferencesSave"),
        (54, "PREFLOAD", "preferencesLoad"),
        (55, "MENU_FIRST_ITEM", "menuFirstItem"),
        (56, "MENU_LAST_ITEM", "menuLastItem"),
        (57, "MENU_PREV_ITEM", "menuPreviousItem"),
        (58, "MENU_NEXT_ITEM", "menuNextItem"),
        (59, "MENU_PREV_SETTING", "menuPreviousSetting"),
        (60, "MENU_NEXT_SETTING", "menuNextSetting"),
        (61, "MUTE", "mute"),
        (62, "SPKHOME", "speechHome"),
        (63, "SAY_LINE", "sayLine"),
        (64, "SAY_ABOVE", "sayAbove"),
        (65, "SAY_BELOW", "sayBelow"),
        (66, "SAY_SLOWER", "saySlower"),
        (67, "SAY_FASTER", "sayFaster"),
        (68, "SAY_SOFTER", "saySofter"),
        (69, "SAY_LOUDER", "sayLouder"),
        (70, "SWITCHVT_PREV", "previousTerminal"),
        (71, "SWITCHVT_NEXT", "nextTerminal"),
        (72, "CSRJMP_VERT", "cursorJumpVertical"),
        (73, "PASTE", "paste"),
        (74, "RESTARTBRL", "restartBraille"),
        (75, "RESTARTSPEECH", "restartSpeech"),
        (76, "OFFLINE", "offline"),
        (77, "SHIFT", "shiftKey"),
        (78, "UPPER", "upperKey"),
        (79, "CONTROL", "controlKey"),
        (80, "META", "metaKey"),
        (81, "TIME", "time"),
    ];

    /// <summary>
    /// Command blocks whose argument is a cell (<c>BRL_BLK_*</c>): block number, BRLTTY's name, the
    /// name Vox reports. PASSDOTS (0x22) and the keyboard blocks are handled on their own.
    /// </summary>
    public static readonly IReadOnlyList<(int Block, string Brltty, string Name)> Blocks =
    [
        (0x01, "ROUTE", "routing"),
        (0x02, "CLIP_NEW", "clipNew"),
        (0x03, "CLIP_ADD", "clipAdd"),
        (0x04, "COPY_RECT", "copyRectangle"),
        (0x05, "COPY_LINE", "copyLine"),
        (0x06, "SWITCHVT", "switchTerminal"),
        (0x07, "PRINDENT", "previousIndent"),
        (0x08, "NXINDENT", "nextIndent"),
        (0x09, "DESCCHAR", "describeChar"),
        (0x0A, "SETLEFT", "setLeft"),
        (0x0B, "SETMARK", "setMark"),
        (0x0C, "GOTOMARK", "gotoMark"),
        (0x0D, "GOTOLINE", "gotoLine"),
        (0x0E, "PRDIFCHAR", "previousDifferentChar"),
        (0x0F, "NXDIFCHAR", "nextDifferentChar"),
    ];

    /// <summary>X keysyms (<c>BRLAPI_KEY_SYM_*</c>) of the keyboard keys BRLTTY passes on, with their X names.</summary>
    public static readonly IReadOnlyList<(uint Keysym, string Name)> Keysyms =
    [
        (0xFF08, "BackSpace"),
        (0xFF09, "Tab"),
        (0xFF0D, "Return"),
        (0xFF1B, "Escape"),
        (0xFF50, "Home"),
        (0xFF51, "Left"),
        (0xFF52, "Up"),
        (0xFF53, "Right"),
        (0xFF54, "Down"),
        (0xFF55, "Page_Up"),
        (0xFF56, "Page_Down"),
        (0xFF57, "End"),
        (0xFF63, "Insert"),
        (0xFFFF, "Delete"),
    ];

    /// <summary><c>BRLAPI_KEY_SYM_FUNCTION</c>: F1; F2 … F35 follow it.</summary>
    public const uint KeysymF1 = 0xFFBE;

    /// <summary>
    /// Modifier flags (the high 32 bits, <c>BRLAPI_KEY_FLG_*</c>) and their names, in the order
    /// they are reported.
    /// </summary>
    public static readonly IReadOnlyList<(uint Flag, string Name)> Modifiers =
    [
        (0x01, "shift"),
        (0x02, "upper"),
        (0x04, "control"),
        (0x08, "meta"),
        (0x10, "altGr"),
        (0x20, "gui"),
    ];

    private static readonly Dictionary<int, string> CommandNames = Commands.ToDictionary(c => c.Code, c => c.Name);
    private static readonly Dictionary<int, string> BlockNames = Blocks.ToDictionary(b => b.Block, b => b.Name);
    private static readonly Dictionary<uint, string> KeysymNames = Keysyms.ToDictionary(k => k.Keysym, k => k.Name);

    /// <summary>
    /// The key names for a BrlAPI key code, or null for codes Vox ignores (NOOP, unknown blocks
    /// such as raw keyboard scan codes).
    /// </summary>
    public static BrailleKeyEventArgs? Decode(ulong keyCode)
    {
        var flags = (uint)((keyCode & FlagsMask) >> FlagsShift);
        var code = keyCode & CodeMask;
        var keys = new List<string>(4);

        if ((keyCode & TypeMask) == TypeCommand)
        {
            var block = (int)((code & BlockMask) >> BlockShift);
            var argument = (int)(code & ArgumentMask);

            if (block == 0)
            {
                if (argument == 0)
                    return null; // NOOP
                keys.Add(CommandNames.TryGetValue(argument, out var name) ? name : $"cmd{argument}");
                return new BrailleKeyEventArgs(keys);
            }

            if (block == PassDotsBlock)
            {
                AddModifiers(keys, flags);
                if ((argument & DotChord) != 0)
                    keys.Add("space");
                var dots = BrailleDots.ToDots((byte)(argument & 0xFF));
                if (dots.Length > 0)
                {
                    keys.Add("dots");
                    keys.Add("dots" + dots);
                }
                return keys.Count == 0 ? null : new BrailleKeyEventArgs(keys);
            }

            if (BlockNames.TryGetValue(block, out var blockName))
            {
                keys.Add(blockName);
                return new BrailleKeyEventArgs(keys, argument);
            }
            return null;
        }

        if ((keyCode & TypeMask) == TypeSymbol)
        {
            var keysym = (uint)code;
            string? name = null;
            if (KeysymNames.TryGetValue(keysym, out var known))
                name = "key_" + known;
            else if (keysym >= KeysymF1 && keysym < KeysymF1 + 35)
                name = "key_F" + (keysym - KeysymF1 + 1);
            else if ((keysym & SymbolUnicode) != 0)
                name = CharName(keysym & 0x00FFFFFF);
            else if (keysym is >= 0x20 and <= 0xFF and not 0x7F)
                name = CharName(keysym); // Latin-1 keysyms are their code points
            if (name is null)
                return null;
            AddModifiers(keys, flags);
            keys.Add(name);
            return new BrailleKeyEventArgs(keys);
        }

        return null;
    }

    private static string? CharName(uint codePoint)
    {
        if (codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF || codePoint < 0x20)
            return null;
        return "char_" + char.ConvertFromUtf32((int)codePoint);
    }

    private static void AddModifiers(List<string> keys, uint flags)
    {
        foreach (var (flag, name) in Modifiers)
        {
            if ((flags & flag) != 0)
                keys.Add(name);
        }
    }
}
