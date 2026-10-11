using Vox.Core.Braille;

namespace Vox.Core.Tests.TestSupport;

/// <summary>
/// An <see cref="IBrailleDisplaySource"/> over a <see cref="FakeBrailleDisplay"/> that a test can
/// swap (another display, none) and whose keys it passes on, like the display manager.
/// </summary>
public sealed class FakeBrailleDisplaySource : IBrailleDisplaySource
{
    private IBrailleDisplay _current;

    public FakeBrailleDisplaySource(IBrailleDisplay display)
    {
        _current = display;
        display.KeyPressed += OnKeyPressed;
    }

    public IBrailleDisplay Current => _current;

    public event EventHandler? DisplayChanged;
    public event EventHandler<BrailleKeyEventArgs>? KeyPressed;

    /// <summary>Uses <paramref name="display"/> from now on and raises <see cref="DisplayChanged"/>.</summary>
    public void Change(IBrailleDisplay display)
    {
        _current.KeyPressed -= OnKeyPressed;
        _current = display;
        display.KeyPressed += OnKeyPressed;
        DisplayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="DisplayChanged"/> for the same display (connected or lost).</summary>
    public void RaiseChanged() => DisplayChanged?.Invoke(this, EventArgs.Empty);

    private void OnKeyPressed(object? sender, BrailleKeyEventArgs e) => KeyPressed?.Invoke(this, e);
}
