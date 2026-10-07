namespace Vox.Core.Navigation;

/// <summary>What a read command reads.</summary>
public enum TextReadKind
{
    Character,
    Word,
    Line,
    Selection,
    Formatting,
}

/// <summary>
/// Reads from the focused text control (an edit field outside a web page, or one inside a page in
/// Focus mode). Implemented in Vox.Core/Accessibility over UIA.
/// </summary>
public interface IFocusedTextReader
{
    /// <summary>Whether the focused control has text to read.</summary>
    bool HasFocusedText { get; }

    /// <summary>Reads <paramref name="kind"/> at the caret of the focused control (asynchronously; speaks the result).</summary>
    void Read(TextReadKind kind);

    /// <summary>Lines of the focused control from its caret, for Say All.</summary>
    ISayAllSource CreateSayAllSource();
}
