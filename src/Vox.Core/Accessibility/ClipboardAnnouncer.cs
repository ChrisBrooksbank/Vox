using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Localization;
using Vox.Core.Pipeline;
using Vox.Core.Speech;

namespace Vox.Core.Accessibility;

/// <summary>What is on the clipboard: some text, or a number of files.</summary>
public readonly record struct ClipboardContent(string? Text, int FileCount);

/// <summary>Reads the clipboard (any thread).</summary>
public interface IClipboardReader
{
    /// <summary>Changes whenever the clipboard's contents do.</summary>
    uint SequenceNumber { get; }

    /// <summary>What is on the clipboard now; null when empty or unreadable.</summary>
    ClipboardContent? Peek();
}

/// <summary><see cref="IClipboardReader"/> through the Win32 sequence number and WinForms (on a short-lived STA thread).</summary>
public sealed partial class WindowsClipboardReader(ILogger<WindowsClipboardReader> logger) : IClipboardReader
{
    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    public uint SequenceNumber => GetClipboardSequenceNumber();

    public ClipboardContent? Peek()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        ClipboardContent? result = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Forms.Clipboard.ContainsFileDropList())
                    result = new ClipboardContent(null, System.Windows.Forms.Clipboard.GetFileDropList().Count);
                else if (System.Windows.Forms.Clipboard.ContainsText())
                    result = new ClipboardContent(System.Windows.Forms.Clipboard.GetText(), 0);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read the clipboard");
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(1)) ? result : null;
    }
}

/// <summary>
/// Says "cut", "copied" or "pasted" after Ctrl+X, Ctrl+C or Ctrl+V (which the keymap passes
/// through), with a few words of the text or a file count. Cut and copy are said only when the
/// clipboard's sequence number changed; paste when something is on the clipboard. A password field
/// gets the plain word, never its text. Never logs what the clipboard holds.
/// </summary>
public sealed class ClipboardAnnouncer
{
    /// <summary>Words of the text said after the verb.</summary>
    public const int MaxWords = 5;

    private const int VkC = 0x43, VkV = 0x56, VkX = 0x58, VkInsert = 0x2D;

    private readonly IClipboardReader _clipboard;
    private readonly IEventSink _pipeline;
    private readonly Action<Utterance> _speak;
    private readonly Func<VoxSettings> _settings;
    private readonly Func<bool> _isPassword;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly ILogger<ClipboardAnnouncer> _logger;
    private readonly TimeProvider _time;
    private int _generation;

    public ClipboardAnnouncer(
        IClipboardReader clipboard,
        IEventSink pipeline,
        Action<Utterance> speak,
        Func<VoxSettings> settings,
        Func<bool> isPassword,
        ILogger<ClipboardAnnouncer> logger,
        Func<TimeSpan, Task>? delay = null,
        TimeProvider? time = null)
    {
        _clipboard = clipboard;
        _pipeline = pipeline;
        _speak = speak;
        _settings = settings;
        _isPassword = isPassword;
        _logger = logger;
        _delay = delay ?? Task.Delay;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How long the application gets to change the clipboard after the key.</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Call for every raw key (pipeline thread).</summary>
    public void HandleRawKey(RawKeyEvent e)
    {
        var key = e.Key;
        if (!key.IsKeyDown || !_settings().ClipboardAnnouncements)
            return;
        // Ctrl alone with X, C or V (Ctrl+Insert and Shift+Insert copy and paste too)
        var m = key.Modifiers;
        bool ctrl = (m & KeyModifiers.Ctrl) != 0;
        bool shift = (m & KeyModifiers.Shift) != 0;
        if ((m & (KeyModifiers.Alt | KeyModifiers.Insert)) != 0)
            return;
        string? verb = key.VkCode switch
        {
            VkX when ctrl && !shift => "Clipboard.Cut",
            VkC when ctrl && !shift => "Clipboard.Copied",
            VkV when ctrl && !shift => "Clipboard.Pasted",
            VkInsert when ctrl && !shift => "Clipboard.Copied",
            VkInsert when shift && !ctrl => "Clipboard.Pasted",
            _ => null,
        };
        if (verb is null)
            return;

        bool paste = verb == "Clipboard.Pasted";
        uint before = _clipboard.SequenceNumber;
        int generation = Interlocked.Increment(ref _generation);
        _ = AnnounceAsync(verb, paste, before, generation);
    }

    private async Task AnnounceAsync(string verb, bool paste, uint before, int generation)
    {
        try
        {
            await _delay(Settle).ConfigureAwait(false);
            // Off the key's thread: the clipboard can be slow, and the speech goes through the pipeline
            ClipboardContent? content = null;
            bool changed = _clipboard.SequenceNumber != before;
            if (changed || paste)
                content = _clipboard.Peek();
            _pipeline.Post(new PipelineWorkEvent(_time.GetUtcNow(), () =>
            {
                if (generation != Volatile.Read(ref _generation))
                    return; // a newer shortcut is being announced
                Say(verb, paste, changed, content);
            }));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Clipboard announcement failed");
        }
    }

    private void Say(string verb, bool paste, bool changed, ClipboardContent? content)
    {
        if (!paste && !changed)
            return; // nothing was cut or copied (no selection)
        if (paste && content is null)
            return; // nothing to paste
        _speak(new Utterance(Describe(verb, content, _isPassword()), SpeechPriority.High));
    }

    /// <summary>The announcement for <paramref name="verb"/> (a string name) over <paramref name="content"/>.</summary>
    public static string Describe(string verb, ClipboardContent? content, bool password)
    {
        var word = VoxStrings.Get(verb);
        if (content is not { } c)
            return word;
        if (c.FileCount > 0)
            return VoxStrings.Format(c.FileCount == 1 ? "Clipboard.OneFile" : "Clipboard.Files", word, c.FileCount);
        // A password field's text is never said
        if (password || string.IsNullOrWhiteSpace(c.Text))
            return word;
        return VoxStrings.Format("Clipboard.WithText", word, FirstWords(c.Text!));
    }

    /// <summary>The first few words of <paramref name="text"/>, with an ellipsis when there are more.</summary>
    public static string FirstWords(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var head = string.Join(' ', words.Take(MaxWords));
        return words.Length > MaxWords ? head + "…" : head;
    }
}
