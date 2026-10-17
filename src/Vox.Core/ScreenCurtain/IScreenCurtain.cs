namespace Vox.Core.ScreenCurtain;

/// <summary>
/// Blacks out the display while the screen reader keeps working. The Windows implementation
/// (<see cref="WindowsScreenCurtain"/>) uses the Magnification API's full-screen colour effect;
/// tests use a fake. Thread-safe.
/// </summary>
public interface IScreenCurtain : IDisposable
{
    /// <summary>Whether the display is black now.</summary>
    bool IsActive { get; }

    /// <summary>Blacks out the display; false when that is not possible here.</summary>
    bool Apply();

    /// <summary>Puts the display back as it was.</summary>
    void Remove();
}
