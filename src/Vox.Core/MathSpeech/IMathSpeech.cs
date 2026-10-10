namespace Vox.Core.MathSpeech;

/// <summary>
/// Speaks MathML and lets the user explore it (MathCAT, an optional component; see docs/mathcat.md).
/// Calls are made on the pipeline thread, one expression at a time.
/// </summary>
public interface IMathSpeech
{
    /// <summary>False when no math component is installed: math is then read as its plain text.</summary>
    bool IsAvailable { get; }

    /// <summary>Makes <paramref name="mathMl"/> the current expression and returns its speech, or null on failure.</summary>
    string? SetExpression(string mathMl);

    /// <summary>
    /// Runs a navigation command on the current expression (MathCAT's names: MoveNext,
    /// MovePrevious, ZoomIn, ZoomOut, MoveStart, MoveEnd, ReadCurrent) and returns what to say.
    /// </summary>
    string? Navigate(string command);
}

/// <summary>No math component installed.</summary>
public sealed class NoMathSpeech : IMathSpeech
{
    public bool IsAvailable => false;
    public string? SetExpression(string mathMl) => null;
    public string? Navigate(string command) => null;
}
