using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Input;

/// <summary>
/// Tests for TypingEchoHandler covering:
/// - Character echo (Characters and Both modes)
/// - Word echo (Words and Both modes)
/// - None mode (no output)
/// - Word boundary detection (Space, Enter, punctuation)
/// - Backspace trims rolling buffer
/// </summary>
public class TypingEchoHandlerTests
{
    // Helper: create a key-up RawKeyEvent
    private static RawKeyEvent KeyUp(int vkCode, KeyModifiers modifiers = KeyModifiers.None)
        => new(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vkCode, Modifiers = modifiers, IsKeyDown = false, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

    // Helper: create a key-down RawKeyEvent
    private static RawKeyEvent KeyDown(int vkCode, KeyModifiers modifiers = KeyModifiers.None)
        => new(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vkCode, Modifiers = modifiers, IsKeyDown = true, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

    // Fake event sink that captures events
    private sealed class CapturingEventSink : IEventSink
    {
        public readonly List<TypingEchoEvent> Events = new();

        public void Post(ScreenReaderEvent evt)
        {
            if (evt is TypingEchoEvent echo)
                Events.Add(echo);
        }
    }

    private static (TypingEchoHandler handler, CapturingEventSink sink) CreateHandler(TypingEchoMode mode)
    {
        var sink = new CapturingEventSink();
        var handler = new TypingEchoHandler(sink, () => mode, NullLogger<TypingEchoHandler>.Instance);
        return (handler, sink);
    }

    // -------------------------------------------------------------------------
    // None mode
    // -------------------------------------------------------------------------

    [Fact]
    public void NoneMode_PrintableChar_NoEvent()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.None);

        handler.HandleKeyEvent(KeyUp(0x41)); // 'a'

        Assert.Empty(sink.Events);
    }

    [Fact]
    public void NoneMode_Space_NoEvent()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.None);

        handler.HandleKeyEvent(KeyUp(0x20)); // Space

        Assert.Empty(sink.Events);
    }

    // -------------------------------------------------------------------------
    // Characters mode
    // -------------------------------------------------------------------------

    [Fact]
    public void CharactersMode_LowercaseLetter_EchosChar()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Characters);

        handler.HandleKeyEvent(KeyUp(0x41)); // VK_A -> 'a' (no shift)

        Assert.Single(sink.Events);
        Assert.Equal("a", sink.Events[0].Text);
        Assert.False(sink.Events[0].IsWord);
    }

    [Fact]
    public void CharactersMode_UppercaseLetter_EchosChar()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Characters);

        handler.HandleKeyEvent(KeyUp(0x41, KeyModifiers.Shift)); // Shift+A -> 'A'

        Assert.Single(sink.Events);
        Assert.Equal("A", sink.Events[0].Text);
    }

    [Fact]
    public void CharactersMode_Digit_EchosChar()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Characters);

        handler.HandleKeyEvent(KeyUp(0x35)); // '5'

        Assert.Single(sink.Events);
        Assert.Equal("5", sink.Events[0].Text);
    }

    [Fact]
    public void CharactersMode_Space_EchosSpaceAndNoWord()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Characters);

        handler.HandleKeyEvent(KeyUp(0x48)); // 'h'
        handler.HandleKeyEvent(KeyUp(0x20)); // Space

        // Should get 'h' then 'Space'
        Assert.Equal(2, sink.Events.Count);
        Assert.Equal("h", sink.Events[0].Text);
        Assert.False(sink.Events[0].IsWord);
        Assert.Equal("Space", sink.Events[1].Text);
        Assert.False(sink.Events[1].IsWord);
    }

    [Fact]
    public void CharactersMode_Enter_EchosReturn()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Characters);

        handler.HandleKeyEvent(KeyUp(0x0D)); // Enter

        Assert.Single(sink.Events);
        Assert.Equal("Return", sink.Events[0].Text);
    }

    [Fact]
    public void CharactersMode_ArrowKey_NoEvent()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Characters);

        handler.HandleKeyEvent(KeyUp(0x26)); // VK_UP

        Assert.Empty(sink.Events);
    }

    [Fact]
    public void CharactersMode_KeyDownEvent_NoEcho()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Characters);

        handler.HandleKeyEvent(KeyDown(0x41)); // key-down 'A'

        Assert.Empty(sink.Events);
    }

    // -------------------------------------------------------------------------
    // Words mode
    // -------------------------------------------------------------------------

    [Fact]
    public void WordsMode_LettersFollowedBySpace_EchosWord()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Words);

        handler.HandleKeyEvent(KeyUp(0x48)); // 'h'
        handler.HandleKeyEvent(KeyUp(0x45)); // 'e'
        handler.HandleKeyEvent(KeyUp(0x4C)); // 'l'
        handler.HandleKeyEvent(KeyUp(0x4C)); // 'l'
        handler.HandleKeyEvent(KeyUp(0x4F)); // 'o'
        handler.HandleKeyEvent(KeyUp(0x20)); // Space

        // Only one word event, no char events
        Assert.Single(sink.Events);
        Assert.Equal("hello", sink.Events[0].Text);
        Assert.True(sink.Events[0].IsWord);
    }

    [Fact]
    public void WordsMode_LettersFollowedByEnter_EchosWord()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Words);

        handler.HandleKeyEvent(KeyUp(0x48)); // 'h'
        handler.HandleKeyEvent(KeyUp(0x49)); // 'i'
        handler.HandleKeyEvent(KeyUp(0x0D)); // Enter

        Assert.Single(sink.Events);
        Assert.Equal("hi", sink.Events[0].Text);
        Assert.True(sink.Events[0].IsWord);
    }

    [Fact]
    public void WordsMode_SingleChar_NoCharEvent()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Words);

        handler.HandleKeyEvent(KeyUp(0x41)); // 'a' (no boundary yet)

        // No word boundary yet, no events
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void WordsMode_SpaceWithNoBuffer_NoEvent()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Words);

        handler.HandleKeyEvent(KeyUp(0x20)); // Space with empty buffer

        Assert.Empty(sink.Events);
    }

    [Fact]
    public void WordsMode_MultipleWords_EachWordEchoed()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Words);

        // Type "hi "
        handler.HandleKeyEvent(KeyUp(0x48)); // 'h'
        handler.HandleKeyEvent(KeyUp(0x49)); // 'i'
        handler.HandleKeyEvent(KeyUp(0x20)); // Space

        // Type "yo "
        handler.HandleKeyEvent(KeyUp(0x59)); // 'y'
        handler.HandleKeyEvent(KeyUp(0x4F)); // 'o'
        handler.HandleKeyEvent(KeyUp(0x20)); // Space

        Assert.Equal(2, sink.Events.Count);
        Assert.Equal("hi", sink.Events[0].Text);
        Assert.Equal("yo", sink.Events[1].Text);
    }

    // -------------------------------------------------------------------------
    // Both mode
    // -------------------------------------------------------------------------

    [Fact]
    public void BothMode_Letters_EchosCharsAndWordOnBoundary()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Both);

        handler.HandleKeyEvent(KeyUp(0x48)); // 'h'
        handler.HandleKeyEvent(KeyUp(0x49)); // 'i'
        handler.HandleKeyEvent(KeyUp(0x20)); // Space

        // Expect: 'h', 'i', 'hi' (word), 'Space' (boundary char)
        Assert.Equal(4, sink.Events.Count);
        Assert.Equal("h", sink.Events[0].Text);
        Assert.False(sink.Events[0].IsWord);
        Assert.Equal("i", sink.Events[1].Text);
        Assert.False(sink.Events[1].IsWord);
        Assert.Equal("hi", sink.Events[2].Text);
        Assert.True(sink.Events[2].IsWord);
        Assert.Equal("Space", sink.Events[3].Text);
        Assert.False(sink.Events[3].IsWord);
    }

    // -------------------------------------------------------------------------
    // Backspace
    // -------------------------------------------------------------------------

    [Fact]
    public void Backspace_TrimsWordBuffer()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Words);

        handler.HandleKeyEvent(KeyUp(0x48));    // 'h'
        handler.HandleKeyEvent(KeyUp(0x49));    // 'i'
        handler.HandleKeyEvent(KeyDown(0x08));  // Backspace (key-down trims buffer)
        handler.HandleKeyEvent(KeyUp(0x20));    // Space -> echo word

        Assert.Single(sink.Events);
        Assert.Equal("h", sink.Events[0].Text); // 'i' was trimmed by backspace
    }

    [Fact]
    public void Backspace_OnEmptyBuffer_DoesNotCrash()
    {
        var (handler, sink) = CreateHandler(TypingEchoMode.Words);

        // Should not throw
        handler.HandleKeyEvent(KeyDown(0x08));

        Assert.Empty(sink.Events);
    }

    // -------------------------------------------------------------------------
    // VkCodeToChar tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0x41, false, 'a')]  // A -> 'a'
    [InlineData(0x41, true,  'A')]  // Shift+A -> 'A'
    [InlineData(0x5A, false, 'z')]  // Z -> 'z'
    [InlineData(0x5A, true,  'Z')]  // Shift+Z -> 'Z'
    [InlineData(0x30, false, '0')]  // 0 -> '0'
    [InlineData(0x31, true,  '!')]  // Shift+1 -> '!'
    [InlineData(0x32, true,  '@')]  // Shift+2 -> '@'
    [InlineData(0x35, false, '5')]  // 5 -> '5'
    [InlineData(0x60, false, '0')]  // Numpad 0
    [InlineData(0x61, false, '1')]  // Numpad 1
    [InlineData(0x26, false, '\0')] // VK_UP (non-printable)
    [InlineData(0x0D, false, '\0')] // Enter -> not mapped here (word boundary)
    public void VkCodeToChar_MapsCorrectly(int vkCode, bool shift, char expected)
    {
        var modifiers = shift ? KeyModifiers.Shift : KeyModifiers.None;
        Assert.Equal(expected, TypingEchoHandler.VkCodeToChar(vkCode, modifiers));
    }
}
