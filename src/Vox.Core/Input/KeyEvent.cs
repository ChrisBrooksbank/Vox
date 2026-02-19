namespace Vox.Core.Input;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Insert = 1 << 3
}

public readonly struct KeyEvent
{
    public int VkCode { get; init; }
    public KeyModifiers Modifiers { get; init; }
    public bool IsKeyDown { get; init; }
    public long Timestamp { get; init; }
}
