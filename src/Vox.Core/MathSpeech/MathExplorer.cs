using Vox.Core.Input;

namespace Vox.Core.MathSpeech;

/// <summary>
/// Interactive exploration of one expression: the browse keys move through it instead of the
/// page (Right/Left next/previous part, Down zooms in, Up zooms out, Home/End first/last part,
/// Insert+Up reads the current part), as in NVDA with MathCAT.
/// </summary>
public sealed class MathExplorer(IMathSpeech speech)
{
    public bool IsActive { get; private set; }

    /// <summary>Starts exploring <paramref name="mathMl"/>; returns what to say, or null when it can't be explored.</summary>
    public string? Start(string mathMl)
    {
        if (!speech.IsAvailable || speech.SetExpression(mathMl) is not { } text)
            return null;
        IsActive = true;
        return text;
    }

    public void Stop() => IsActive = false;

    /// <summary>The MathCAT command for a browse command, or null when it isn't one.</summary>
    public static string? CommandFor(NavigationCommand command) => command switch
    {
        NavigationCommand.NextChar => "MoveNext",
        NavigationCommand.PrevChar => "MovePrevious",
        NavigationCommand.NextWord => "MoveNext",
        NavigationCommand.PrevWord => "MovePrevious",
        NavigationCommand.NextLine => "ZoomIn",
        NavigationCommand.PrevLine => "ZoomOut",
        NavigationCommand.StartOfLine => "MoveStart",
        NavigationCommand.EndOfLine => "MoveEnd",
        NavigationCommand.ReadCurrentLine => "ReadCurrent",
        NavigationCommand.ReadCurrentWord => "ReadCurrent",
        NavigationCommand.ReadCurrentChar => "ReadCurrent",
        _ => null,
    };

    /// <summary>
    /// Handles a browse command while exploring: the speech for a math command (null when there
    /// is nothing to say), with <c>Handled</c> false for any other command (which ends exploring).
    /// </summary>
    public (bool Handled, string? Speech) Handle(NavigationCommand command)
    {
        if (!IsActive || CommandFor(command) is not { } mathCommand)
            return (false, null);
        return (true, speech.Navigate(mathCommand));
    }
}
