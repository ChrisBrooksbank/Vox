using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;
using Vox.Core.Pipeline;

namespace Vox.Core.Input;

/// <summary>
/// Handles typing echo by listening for RawKeyEvents and posting TypingEchoEvents.
///
/// Character echo: on key-up, speaks the character the key typed (mapped through the keyboard
/// layout, using the modifiers and Caps Lock state from the key-down).
/// Word echo: Space, Enter and punctuation end a word; the accumulated word is then spoken.
/// The word buffer is cleared when the caret or focus moves (arrows, Tab, Home/End, focus changes)
/// and by Ctrl+Backspace. Shortcuts (Ctrl/Alt/screen reader modifier) are never echoed, and in
/// password fields every character is echoed as "star".
///
/// Respects TypingEchoMode: None / Characters / Words / Both.
/// </summary>
public sealed class TypingEchoHandler
{
    private readonly IEventSink _pipeline;
    private readonly Func<TypingEchoMode> _getMode;
    private readonly ILogger<TypingEchoHandler> _logger;

    // Rolling buffer for word echo – stores chars since last word boundary
    private readonly System.Text.StringBuilder _wordBuffer = new();

    // VK codes that delete content (Backspace)
    private static readonly HashSet<int> DeleteVkCodes = new()
    {
        0x08, // Backspace
        0x2E, // Delete
    };

    // Keys that move the caret or focus: a new word starts wherever the user types next
    private static readonly HashSet<int> CaretMovementVkCodes = new()
    {
        0x09,                   // Tab
        0x1B,                   // Escape
        0x21, 0x22, 0x23, 0x24, // Page Up, Page Down, End, Home
        0x25, 0x26, 0x27, 0x28, // arrows
    };

    private const int VK_RETURN = 0x0D;
    private const int VK_BACK = 0x08;

    private readonly Func<KeyEvent, TypedChar> _charMapper;

    // Accent from a dead key, waiting to combine with the next character (e.g. '^' + 'e' = 'ê')
    private char? _pendingDeadKey;

    // Modifiers and Caps Lock at each key's key-down: echo happens on key-up, when the user may
    // already have released Shift (a capital) or Ctrl (a shortcut)
    private readonly KeyModifiers[] _downModifiers = new KeyModifiers[256];
    private readonly bool[] _downCapsLock = new bool[256];
    private readonly bool[] _seenDown = new bool[256];

    /// <summary>
    /// True while a password field has focus: characters are echoed as "star" and never buffered
    /// or spoken as words. Set by focus tracking on the pipeline thread (the same thread that
    /// calls <see cref="HandleKeyEvent"/>).
    /// </summary>
    public bool PasswordMode
    {
        get => _passwordMode;
        set
        {
            _passwordMode = value;
            if (value) _wordBuffer.Clear();
        }
    }
    private volatile bool _passwordMode;

    /// <param name="charMapper">
    /// Maps a key press to the character it types. Defaults to a US-layout table; the app passes
    /// <see cref="KeyboardLayoutMapper.ToChar"/> to follow the user's keyboard layout.
    /// </param>
    public TypingEchoHandler(
        IEventSink pipeline,
        Func<TypingEchoMode> getMode,
        ILogger<TypingEchoHandler> logger,
        Func<KeyEvent, TypedChar>? charMapper = null)
    {
        _pipeline = pipeline;
        _getMode = getMode;
        _logger = logger;
        _charMapper = charMapper ?? (e => (TypedChar)VkCodeToChar(e.VkCode, e.Modifiers, e.CapsLockOn));
    }

    /// <summary>Forgets the partly typed word (focus or caret moved elsewhere).</summary>
    public void ResetWord() => _wordBuffer.Clear();

    /// <summary>
    /// Processes a RawKeyEvent. Should be called for every RawKeyEvent coming through the pipeline.
    /// </summary>
    public void HandleKeyEvent(RawKeyEvent rawKeyEvent)
    {
        var evt = rawKeyEvent.Key;
        var mode = _getMode();

        if (mode == TypingEchoMode.None)
        {
            _wordBuffer.Clear();
            return;
        }

        var slot = evt.VkCode & 0xFF;

        // Only process key-up events for echo
        if (evt.IsKeyDown)
        {
            _downModifiers[slot] = evt.Modifiers;
            _downCapsLock[slot] = evt.CapsLockOn;
            _seenDown[slot] = true;

            if (evt.VkCode == VK_BACK && (evt.Modifiers & KeyModifiers.Ctrl) != 0)
                _wordBuffer.Clear(); // Ctrl+Backspace deletes the whole word
            else if (DeleteVkCodes.Contains(evt.VkCode) && _wordBuffer.Length > 0)
                _wordBuffer.Remove(_wordBuffer.Length - 1, 1);
            else if (CaretMovementVkCodes.Contains(evt.VkCode))
                _wordBuffer.Clear();
            return;
        }

        // Key-up from here on: use the state from the key-down
        if (_seenDown[slot])
        {
            evt = evt with { Modifiers = _downModifiers[slot], CapsLockOn = _downCapsLock[slot] };
            _seenDown[slot] = false;
        }

        // Shortcuts (Ctrl+S, Alt+F, Insert+…) don't type anything. Ctrl+Alt together is AltGr,
        // which types characters on many layouts; it counts as a shortcut only if it types nothing.
        bool ctrl = (evt.Modifiers & KeyModifiers.Ctrl) != 0;
        bool alt = (evt.Modifiers & KeyModifiers.Alt) != 0;
        bool altGr = ctrl && alt;
        if ((evt.Modifiers & KeyModifiers.Insert) != 0 || ((ctrl || alt) && !altGr))
            return;

        if (evt.VkCode == VK_RETURN && !altGr)
        {
            _pendingDeadKey = null;
            HandleWordBoundary("Return", mode);
            return;
        }

        // The character this key types in the user's keyboard layout
        var typed = _charMapper(evt);
        if (typed.IsDeadKey)
        {
            // Nothing is typed yet: the accent combines with the next character
            _pendingDeadKey = typed.Char;
            return;
        }

        var ch = typed.Char;
        if (ch == '\0')
            return; // Non-printable key (arrows, F-keys, etc.), or an AltGr shortcut

        if (_pendingDeadKey is { } accent)
        {
            _pendingDeadKey = null;
            ch = ComposeWithDeadKey(accent, ch);
        }

        // Space and punctuation end a word
        if (ch == ' ' || char.IsPunctuation(ch) || char.IsSymbol(ch))
        {
            HandleWordBoundary(PasswordMode ? "star" : GetCharacterName(ch), mode);
            return;
        }

        if (PasswordMode)
        {
            // Never reveal password characters, and never collect them into a word
            if (mode == TypingEchoMode.Characters || mode == TypingEchoMode.Both)
                _pipeline.Post(new TypingEchoEvent(DateTimeOffset.UtcNow, "star", IsWord: false));
            return;
        }

        // Append to rolling word buffer
        _wordBuffer.Append(ch);

        // Character echo
        if (mode == TypingEchoMode.Characters || mode == TypingEchoMode.Both)
        {
            var charText = GetCharacterName(ch);
            _pipeline.Post(new TypingEchoEvent(DateTimeOffset.UtcNow, charText, IsWord: false));
            _logger.LogDebug("TypingEcho char: {Char}", charText);
        }
    }

    /// <summary>
    /// The character typed after dead key <paramref name="accent"/>: the precomposed letter when
    /// one exists ('^' + 'e' → 'ê'), the accent itself after Space, otherwise the plain character.
    /// </summary>
    public static char ComposeWithDeadKey(char accent, char ch)
    {
        if (ch == ' ')
            return accent;

        char? combining = accent switch
        {
            '^' => '\u0302',
            '´' or '\'' => '\u0301',
            '`' => '\u0300',
            '¨' or '"' => '\u0308',
            '~' => '\u0303',
            '¸' => '\u0327',
            'ˇ' => '\u030C',
            '°' or '˚' => '\u030A',
            _ => null,
        };
        if (combining is null)
            return ch;

        var composed = string.Concat(ch, combining.Value).Normalize(System.Text.NormalizationForm.FormC);
        return composed.Length == 1 ? composed[0] : ch;
    }

    private void HandleWordBoundary(string boundaryName, TypingEchoMode mode)
    {
        // Echo the boundary character first: character echoes interrupt speech, while the word
        // (spoken at High priority) queues after it instead of being cut off by it
        if (mode == TypingEchoMode.Characters || mode == TypingEchoMode.Both)
        {
            _pipeline.Post(new TypingEchoEvent(DateTimeOffset.UtcNow, boundaryName, IsWord: false));
            _logger.LogDebug("TypingEcho boundary char: {Char}", boundaryName);
        }

        // Speak the word that was accumulated before this boundary (never in password fields)
        if ((mode == TypingEchoMode.Words || mode == TypingEchoMode.Both)
            && _wordBuffer.Length > 0 && !PasswordMode)
        {
            var word = _wordBuffer.ToString();
            _pipeline.Post(new TypingEchoEvent(DateTimeOffset.UtcNow, word, IsWord: true));
            _logger.LogDebug("TypingEcho word: {Word}", word);
        }

        _wordBuffer.Clear();
    }

    /// <summary>
    /// Maps a virtual key code to its printable character, considering shift and Caps Lock state.
    /// Returns '\0' if the key is not printable.
    /// A US-layout table (the default mapper, used in tests); the app maps through the real layout.
    /// </summary>
    public static char VkCodeToChar(int vkCode, KeyModifiers modifiers, bool capsLockOn = false)
    {
        // The US layout has no AltGr characters: Ctrl/Alt combinations type nothing
        if ((modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0)
            return '\0';

        bool shift = (modifiers & KeyModifiers.Shift) != 0;

        // A-Z keys (VK 65–90): Caps Lock inverts the case Shift gives
        if (vkCode >= 0x41 && vkCode <= 0x5A)
        {
            char ch = (char)(vkCode); // uppercase A-Z
            return shift ^ capsLockOn ? ch : char.ToLower(ch);
        }

        // 0-9 number row (VK 48–57)
        if (vkCode >= 0x30 && vkCode <= 0x39)
        {
            if (!shift)
                return (char)(vkCode); // '0'–'9'

            // Shifted number row
            return vkCode switch
            {
                0x30 => ')',
                0x31 => '!',
                0x32 => '@',
                0x33 => '#',
                0x34 => '$',
                0x35 => '%',
                0x36 => '^',
                0x37 => '&',
                0x38 => '*',
                0x39 => '(',
                _ => '\0'
            };
        }

        // Numpad 0-9 (VK 96–105)
        if (vkCode >= 0x60 && vkCode <= 0x69)
            return (char)('0' + (vkCode - 0x60));

        return vkCode switch
        {
            0x20 => ' ',
            // Numpad operators
            0x6A => '*',
            0x6B => '+',
            0x6D => '-',
            0x6E => '.',
            0x6F => '/',
            // OEM punctuation keys (US layout), unshifted / shifted
            0xBA => shift ? ':' : ';',
            0xBB => shift ? '+' : '=',
            0xBC => shift ? '<' : ',',
            0xBD => shift ? '_' : '-',
            0xBE => shift ? '>' : '.',
            0xBF => shift ? '?' : '/',
            0xC0 => shift ? '~' : '`',
            0xDB => shift ? '{' : '[',
            0xDC => shift ? '|' : '\\',
            0xDD => shift ? '}' : ']',
            0xDE => shift ? '"' : '\'',
            _ => '\0'
        };
    }

    /// <summary>
    /// Returns a spoken name for a character. For most chars this is the char itself,
    /// but some characters have clearer spoken names.
    /// </summary>
    public static string GetCharacterName(char ch) => ch switch
    {
        '\n' => "blank",
        ' ' => "Space",
        '\t' => "Tab",
        '@' => "at",
        '#' => "hash",
        '$' => "dollar",
        '%' => "percent",
        '^' => "caret",
        '&' => "ampersand",
        '*' => "asterisk",
        '(' => "open paren",
        ')' => "close paren",
        '!' => "exclamation",
        '-' => "hyphen",
        '_' => "underscore",
        '=' => "equals",
        '+' => "plus",
        '[' => "open bracket",
        ']' => "close bracket",
        '{' => "open brace",
        '}' => "close brace",
        '\\' => "backslash",
        '|' => "pipe",
        ';' => "semicolon",
        ':' => "colon",
        '\'' => "apostrophe",
        '"' => "quote",
        ',' => "comma",
        '.' => "period",
        '<' => "less than",
        '>' => "greater than",
        '/' => "slash",
        '?' => "question mark",
        '`' => "backtick",
        '~' => "tilde",
        _ => ch.ToString()
    };
}
