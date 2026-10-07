using Interop.UIAutomationClient;
using Vox.Core.Text;

namespace Vox.Core.Accessibility;

/// <summary>
/// <see cref="IFocusedTextSource"/> for the focused UIA element: its text pattern when it has one,
/// otherwise (an edit control known only by its value) its ValuePattern text with the caret from
/// Win32 (<see cref="Win32Caret"/>). <see cref="GetFocusedDocument"/> runs on the UIA thread.
/// </summary>
public sealed class UIAFocusedTextSource : IFocusedTextSource
{
    private const int UIA_ValuePatternId = 10002;

    private readonly UIAEventSubscriber _subscriber;
    private readonly UIAProvider _provider;

    public UIAFocusedTextSource(UIAEventSubscriber subscriber, UIAProvider provider)
    {
        _subscriber = subscriber;
        _provider = provider;
    }

    public bool RaisesCaretEvents => _subscriber.FocusedTextKind == FocusedTextKind.TextPattern;

    public bool HasText => _subscriber.FocusedTextKind != FocusedTextKind.None;

    public bool IsTerminal => _subscriber.FocusedIsTerminal;

    public IReadOnlyList<string>? GetVisibleLines()
    {
        var element = _subscriber.FocusedTextElement;
        if (element is null || _subscriber.FocusedTextKind != FocusedTextKind.TextPattern)
            return null;
        return UIATextDocument.TryCreate(element, _provider.Automation)?.GetVisibleLines();
    }

    public ITextDocument? GetFocusedDocument()
    {
        var element = _subscriber.FocusedTextElement;
        if (element is null)
            return null;

        if (_subscriber.FocusedTextKind == FocusedTextKind.TextPattern)
            return UIATextDocument.TryCreate(element, _provider.Automation);

        return ValueDocument(element);
    }

    /// <summary>The element's value with the caret and selection of the window that owns the caret.</summary>
    private static ITextDocument? ValueDocument(IUIAutomationElement element)
    {
        if (element.GetCurrentPattern(UIA_ValuePatternId) is not IUIAutomationValuePattern value)
            return null;
        var text = value.CurrentValue ?? string.Empty;
        var selection = Win32Caret.GetEditSelection(Win32Caret.GetCaretWindow());
        if (selection is not { } sel)
            return null;
        return FromValue(text, sel.Start, sel.End);
    }

    /// <summary>A document for a value-only edit control with the given selection (start == end: caret).</summary>
    public static StringTextDocument FromValue(string text, int selectionStart, int selectionEnd)
    {
        // EM_GETSEL doesn't say which end the caret is at; the end is right for the common
        // cases (typing, Shift+Right/Down extending forwards)
        return new StringTextDocument(text, selectionEnd, selectionStart, selectionEnd);
    }
}

/// <summary>What kind of text the focused element exposes.</summary>
public enum FocusedTextKind
{
    None,

    /// <summary>A UIA text pattern, with caret and text change events.</summary>
    TextPattern,

    /// <summary>Only a value (ValuePattern), with the caret read through Win32.</summary>
    ValueOnly,
}
