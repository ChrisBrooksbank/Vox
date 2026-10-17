using Microsoft.Extensions.Logging.Abstractions;
using Vox.Core.Accessibility;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Core.Speech;
using Vox.Core.Tests.AppModules;
using Xunit;

namespace Vox.Core.Tests.Accessibility;

public class ClipboardAnnouncerTests
{
    private sealed class FakeClipboard : IClipboardReader
    {
        public uint SequenceNumber { get; set; } = 1;
        public ClipboardContent? Content { get; set; }
        public ClipboardContent? Peek() => Content;
    }

    private readonly FakeClipboard _clipboard = new();
    private readonly List<Utterance> _spoken = [];
    private VoxSettings _settings = new();
    private bool _password;

    private ClipboardAnnouncer Create() => new(_clipboard, new InlineSink(), _spoken.Add,
        () => _settings, () => _password, NullLogger<ClipboardAnnouncer>.Instance, _ => Task.CompletedTask);

    private static RawKeyEvent Key(int vk, KeyModifiers modifiers = KeyModifiers.Ctrl, bool down = true) =>
        new(DateTimeOffset.UtcNow, new KeyEvent { VkCode = vk, Modifiers = modifiers, IsKeyDown = down });

    private static async Task Settle() => await Task.Delay(50);

    [Fact]
    public async Task Copy_WhenClipboardUnchanged_SaysNothing()
    {
        _clipboard.Content = new ClipboardContent("hello brave new world", 0);
        Create().HandleRawKey(Key(0x43)); // nothing selected: the sequence number stays
        await Settle();
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task Cut_WhenSequenceNumberChangesDuringSettle_SaysCut()
    {
        var gate = new TaskCompletionSource();
        var announcer = new ClipboardAnnouncer(_clipboard, new InlineSink(), _spoken.Add, () => _settings,
            () => _password, NullLogger<ClipboardAnnouncer>.Instance, _ => gate.Task);
        _clipboard.Content = new ClipboardContent("one two", 0);
        announcer.HandleRawKey(Key(0x58));
        _clipboard.SequenceNumber++;
        gate.SetResult();
        await Settle();
        Assert.Equal("cut one two", Assert.Single(_spoken).Text);
    }

    [Fact]
    public async Task Copy_LongText_SaysFirstWordsWithEllipsis()
    {
        var gate = new TaskCompletionSource();
        var announcer = new ClipboardAnnouncer(_clipboard, new InlineSink(), _spoken.Add, () => _settings,
            () => _password, NullLogger<ClipboardAnnouncer>.Instance, _ => gate.Task);
        _clipboard.Content = new ClipboardContent("a b c d e f g", 0);
        announcer.HandleRawKey(Key(0x43));
        _clipboard.SequenceNumber++;
        gate.SetResult();
        await Settle();
        Assert.Equal("copied a b c d e…", Assert.Single(_spoken).Text);
    }

    [Fact]
    public async Task Copy_Files_SaysCount()
    {
        var gate = new TaskCompletionSource();
        var announcer = new ClipboardAnnouncer(_clipboard, new InlineSink(), _spoken.Add, () => _settings,
            () => _password, NullLogger<ClipboardAnnouncer>.Instance, _ => gate.Task);
        _clipboard.Content = new ClipboardContent(null, 3);
        announcer.HandleRawKey(Key(0x43));
        _clipboard.SequenceNumber++;
        gate.SetResult();
        await Settle();
        Assert.Equal("copied 3 files", Assert.Single(_spoken).Text);
    }

    [Fact]
    public async Task Paste_SaysPastedWithoutSequenceChange()
    {
        _clipboard.Content = new ClipboardContent("some text", 0);
        Create().HandleRawKey(Key(0x56));
        await Settle();
        Assert.Equal("pasted some text", Assert.Single(_spoken).Text);
    }

    [Fact]
    public async Task Paste_EmptyClipboard_SaysNothing()
    {
        Create().HandleRawKey(Key(0x56));
        await Settle();
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task PasswordField_NeverSaysTheText()
    {
        _password = true;
        _clipboard.Content = new ClipboardContent("hunter2", 0);
        Create().HandleRawKey(Key(0x56));
        await Settle();
        Assert.Equal("pasted", Assert.Single(_spoken).Text);
    }

    [Fact]
    public async Task SettingOff_SaysNothing()
    {
        _settings = new VoxSettings { ClipboardAnnouncements = false };
        _clipboard.Content = new ClipboardContent("x", 0);
        Create().HandleRawKey(Key(0x56));
        await Settle();
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(0x56, KeyModifiers.None)]
    [InlineData(0x56, KeyModifiers.Ctrl | KeyModifiers.Shift)]
    [InlineData(0x56, KeyModifiers.Ctrl | KeyModifiers.Alt)]
    [InlineData(0x56, KeyModifiers.Ctrl | KeyModifiers.Insert)]
    [InlineData(0x41, KeyModifiers.Ctrl)]
    public async Task OtherKeys_SayNothing(int vk, KeyModifiers modifiers)
    {
        _clipboard.Content = new ClipboardContent("x", 0);
        Create().HandleRawKey(Key(vk, modifiers));
        await Settle();
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task KeyUp_SaysNothing()
    {
        _clipboard.Content = new ClipboardContent("x", 0);
        Create().HandleRawKey(Key(0x56, down: false));
        await Settle();
        Assert.Empty(_spoken);
    }

    [Fact]
    public void FirstWords_CollapsesWhitespace()
    {
        Assert.Equal("a b", ClipboardAnnouncer.FirstWords("  a\r\n b  "));
    }
}
