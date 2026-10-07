namespace Vox.Core.Text;

/// <summary>
/// The text of the focused control, for reading caret moves. In production the UIA
/// implementation is used on the UIA thread.
/// </summary>
public interface IFocusedTextSource
{
    /// <summary>
    /// Whether the focused control reports caret moves itself (a text pattern with caret events).
    /// When false but <see cref="HasText"/>, the caret is read shortly after each caret key instead.
    /// </summary>
    bool RaisesCaretEvents { get; }

    /// <summary>Whether the focused control has text to read at all.</summary>
    bool HasText { get; }

    /// <summary>The focused control's text document, or null. Call on the source's thread.</summary>
    ITextDocument? GetFocusedDocument();

    /// <summary>Whether the focused control is a terminal (its output is read as it appears).</summary>
    bool IsTerminal => false;

    /// <summary>The lines visible in the focused control, or null. Call on the source's thread.</summary>
    IReadOnlyList<string>? GetVisibleLines() => null;
}
