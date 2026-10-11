using Vox.Core.Input;

namespace Vox.Core.Braille;

/// <summary>
/// Typing on a braille display's keyboard. In uncontracted braille each cell is back-translated
/// and typed at once; in contracted braille cells are kept until the word ends (space, Enter),
/// then the word is back-translated and typed, since a cell's meaning depends on the cells around
/// it. Backspace removes the last cell kept, or is sent when there is none.
/// </summary>
public sealed class BrailleInput
{
    private readonly Func<IBrailleTranslator> _translator;
    private readonly Func<bool> _contracted;
    private readonly IKeyboardOutput _output;
    private readonly List<byte> _pending = [];
    private readonly object _lock = new();

    private static readonly OutputKey BackspaceKey = new(0x08);
    private static readonly OutputKey EnterKey = new(0x0D);

    public BrailleInput(Func<IBrailleTranslator> translator, Func<bool> contracted, IKeyboardOutput output)
    {
        _translator = translator;
        _contracted = contracted;
        _output = output;
    }

    /// <summary>The cells typed in contracted braille and not yet translated.</summary>
    public IReadOnlyList<byte> Pending
    {
        get { lock (_lock) return _pending.ToArray(); }
    }

    public void Cell(byte cell)
    {
        lock (_lock)
        {
            if (_contracted())
            {
                _pending.Add(cell);
                return;
            }
        }
        Type(_translator().BackTranslate(new[] { cell }));
    }

    public void Space()
    {
        Flush();
        _output.TypeText(" ");
    }

    public void Enter()
    {
        Flush();
        _output.SendKey(EnterKey);
    }

    public void Backspace()
    {
        lock (_lock)
        {
            if (_pending.Count > 0)
            {
                _pending.RemoveAt(_pending.Count - 1);
                return;
            }
        }
        _output.SendKey(BackspaceKey);
    }

    /// <summary>Types the cells kept so far (before another key, or when the focus moves).</summary>
    public void Flush()
    {
        byte[] cells;
        lock (_lock)
        {
            if (_pending.Count == 0)
                return;
            cells = _pending.ToArray();
            _pending.Clear();
        }
        Type(_translator().BackTranslate(cells));
    }

    /// <summary>Forgets the cells kept (the focus moved elsewhere before the word ended).</summary>
    public void Discard()
    {
        lock (_lock) _pending.Clear();
    }

    private void Type(string text)
    {
        if (text.Length > 0)
            _output.TypeText(text);
    }
}
