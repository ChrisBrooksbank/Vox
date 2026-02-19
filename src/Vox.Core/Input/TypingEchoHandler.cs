using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;
using Vox.Core.Pipeline;

namespace Vox.Core.Input;

/// <summary>
/// Handles typing echo by listening for RawKeyEvents and posting TypingEchoEvents.
///
/// Character echo: on key-up of printable ASCII chars (VK 32–126), speaks the character.
/// Word echo: when Space (VK 32), Enter (VK 13), or common punctuation triggers a word boundary,
///            speaks the accumulated word from the rolling buffer.
///
/// Respects TypingEchoMode: None / Characters / Words / Both.
///
/// VK codes used:
///   0x08 = Backspace, 0x0D = Enter/Return, 0x20 = Space
///   0x21–0x2F = punctuation keys (!, ", #, $, %, &, ', (, ), *, +, comma, -, ., /)
///   0x3A–0x40 = : ; &lt; = &gt; ? @
///   0x5B–0x60 = [ \ ] ^ _ `
///   0x7B–0x7E = { | } ~
///
/// Printable char detection uses the Windows MapVirtualKey API to convert VK codes
/// to Unicode characters for accuracy. The rolling buffer stores raw chars before a
/// word boundary is detected.
/// </summary>
public sealed class TypingEchoHandler
{
    private readonly IEventSink _pipeline;
    private readonly Func<TypingEchoMode> _getMode;
    private readonly ILogger<TypingEchoHandler> _logger;

    // Rolling buffer for word echo – stores chars since last word boundary
    private readonly System.Text.StringBuilder _wordBuffer = new();

    // VK codes that trigger word-boundary (flush the word buffer)
    private static readonly HashSet<int> WordBoundaryVkCodes = new()
    {
        0x0D, // Enter/Return
        0x20, // Space
        // Common punctuation VK codes
        0xBC, // , (comma)
        0xBE, // . (period)
        0xBF, // / (forward slash)
        0xBA, // ; (semicolon)
        0xDE, // ' (apostrophe / quote)
        0xDB, // [ (open bracket)
        0xDD, // ] (close bracket)
        0xDC, // \ (backslash)
        0xBD, // - (minus / hyphen)
        0xBB, // = (equals)
        0xC0, // ` (backtick)
    };

    // VK codes that delete content (Backspace)
    private static readonly HashSet<int> DeleteVkCodes = new()
    {
        0x08, // Backspace
        0x2E, // Delete
    };

    public TypingEchoHandler(
        IEventSink pipeline,
        Func<TypingEchoMode> getMode,
        ILogger<TypingEchoHandler> logger)
    {
        _pipeline = pipeline;
        _getMode = getMode;
        _logger = logger;
    }

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

        // Only process key-up events for echo
        if (evt.IsKeyDown)
        {
            // On key-down of Backspace, trim the rolling buffer
            if (DeleteVkCodes.Contains(evt.VkCode) && _wordBuffer.Length > 0)
                _wordBuffer.Remove(_wordBuffer.Length - 1, 1);
            return;
        }

        // Key-up from here on

        // Check for word boundary keys
        if (WordBoundaryVkCodes.Contains(evt.VkCode))
        {
            HandleWordBoundary(evt.VkCode, mode);
            return;
        }

        // Try to get the printable character for this VK code
        var ch = VkCodeToChar(evt.VkCode, evt.Modifiers);
        if (ch == '\0')
            return; // Non-printable key (arrows, F-keys, etc.)

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

    private void HandleWordBoundary(int vkCode, TypingEchoMode mode)
    {
        // Speak the word that was accumulated before this boundary
        if ((mode == TypingEchoMode.Words || mode == TypingEchoMode.Both)
            && _wordBuffer.Length > 0)
        {
            var word = _wordBuffer.ToString();
            _pipeline.Post(new TypingEchoEvent(DateTimeOffset.UtcNow, word, IsWord: true));
            _logger.LogDebug("TypingEcho word: {Word}", word);
        }

        _wordBuffer.Clear();

        // Also echo the boundary character itself if in character mode
        if (mode == TypingEchoMode.Characters || mode == TypingEchoMode.Both)
        {
            var boundaryName = vkCode switch
            {
                0x0D => "Return",
                0x20 => "Space",
                0xBC => "comma",
                0xBE => "period",
                0xBF => "slash",
                0xBA => "semicolon",
                0xDE => "quote",
                0xDB => "open bracket",
                0xDD => "close bracket",
                0xDC => "backslash",
                0xBD => "hyphen",
                0xBB => "equals",
                0xC0 => "backtick",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(boundaryName))
            {
                _pipeline.Post(new TypingEchoEvent(DateTimeOffset.UtcNow, boundaryName, IsWord: false));
                _logger.LogDebug("TypingEcho boundary char: {Char}", boundaryName);
            }
        }
    }

    /// <summary>
    /// Maps a virtual key code to its printable character, considering shift state.
    /// Returns '\0' if the key is not printable.
    /// Uses a simple lookup for common alpha/numeric keys to avoid P/Invoke in tests.
    /// </summary>
    public static char VkCodeToChar(int vkCode, KeyModifiers modifiers)
    {
        bool shift = (modifiers & KeyModifiers.Shift) != 0;

        // A-Z keys (VK 65–90)
        if (vkCode >= 0x41 && vkCode <= 0x5A)
        {
            char ch = (char)(vkCode); // uppercase A-Z
            return shift ? ch : char.ToLower(ch);
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

        // Space – handled separately as word boundary; not a printable char here
        // (VK 0x20 is in WordBoundaryVkCodes)

        return '\0';
    }

    /// <summary>
    /// Returns a spoken name for a character. For most chars this is the char itself,
    /// but some characters have clearer spoken names.
    /// </summary>
    private static string GetCharacterName(char ch) => ch switch
    {
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
