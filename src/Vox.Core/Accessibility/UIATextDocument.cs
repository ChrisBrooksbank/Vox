using System.Globalization;
using Interop.UIAutomationClient;
using Vox.Core.Text;
using TextUnit = Vox.Core.Text.TextUnit;

namespace Vox.Core.Accessibility;

/// <summary>
/// <see cref="ITextDocument"/> over a UIA TextPattern (TextPattern2 for the caret where available).
/// </summary>
/// <remarks>
/// It and its ranges wrap COM objects: use them only on the UIA STA thread, inside
/// <see cref="UIAThread.RunAsync{T}(Func{T}, TimeSpan?)"/>, and hand results to other threads as
/// strings or as a <see cref="Snapshot"/>.
/// </remarks>
public sealed class UIATextDocument : ITextDocument
{
    internal const int UIA_TextPatternId = 10014;
    internal const int UIA_TextPattern2Id = 10024;

    private readonly IUIAutomationTextPattern _pattern;
    private readonly IUIAutomation? _automation;

    public UIATextDocument(IUIAutomationTextPattern pattern, IUIAutomation? automation = null)
    {
        _pattern = pattern;
        _automation = automation;
    }

    /// <summary>The element's text pattern as a document, or null if it has none. STA thread only.</summary>
    public static UIATextDocument? TryCreate(IUIAutomationElement element, IUIAutomation? automation = null)
    {
        try
        {
            if (element.GetCurrentPattern(UIA_TextPattern2Id) is IUIAutomationTextPattern2 pattern2)
                return new UIATextDocument(pattern2, automation);
            if (element.GetCurrentPattern(UIA_TextPatternId) is IUIAutomationTextPattern pattern)
                return new UIATextDocument(pattern, automation);
        }
        catch
        {
            // Element gone or pattern unavailable
        }
        return null;
    }

    public ITextRange DocumentRange => Wrap(_pattern.DocumentRange);

    /// <summary>The <paramref name="unit"/> of text at a screen point, or null when there is none there.</summary>
    public string? TextAt(tagPOINT point, TextUnit unit, int maxLength = 1000)
    {
        try
        {
            var range = Wrap(_pattern.RangeFromPoint(point)).ExpandToEnclosingUnit(unit);
            return range.GetText(maxLength);
        }
        catch
        {
            // No text at that point, or the provider doesn't support it
            return null;
        }
    }

    public ITextRange? GetCaret()
    {
        if (_pattern is IUIAutomationTextPattern2 pattern2)
        {
            try
            {
                var caret = pattern2.GetCaretRange(out _);
                if (caret is not null)
                    return Wrap(caret);
            }
            catch
            {
                // Fall back to the selection
            }
        }

        // TextPattern only: the caret is at the end of the selection the user extended, which
        // UIA doesn't say; the start is right for an empty selection, the common case
        var selection = GetSelection();
        if (selection.Count == 0)
            return null;
        var start = (UIATextRange)selection[0];
        var degenerate = start.Native.Clone();
        degenerate.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, degenerate,
            TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
        return Wrap(degenerate);
    }

    public IReadOnlyList<ITextRange> GetSelection()
    {
        try
        {
            var array = _pattern.GetSelection();
            if (array is null)
                return [];
            var ranges = new List<ITextRange>(array.Length);
            for (int i = 0; i < array.Length; i++)
                ranges.Add(Wrap(array.GetElement(i)));
            return ranges;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Copies the document into a COM-free <see cref="StringTextDocument"/> (text, caret and first
    /// selection), or returns null if the text is longer than <paramref name="maxLength"/>.
    /// STA thread only; the snapshot can be used anywhere.
    /// </summary>
    public StringTextDocument? Snapshot(int maxLength = 100_000)
    {
        var document = _pattern.DocumentRange;
        var text = document.GetText(maxLength + 1);
        if (text.Length > maxLength)
            return null;

        int OffsetOf(ITextRange? range, TextEndpoint endpoint)
        {
            if (range is not UIATextRange uia)
                return 0;
            var prefix = document.Clone();
            prefix.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End, uia.Native,
                endpoint == TextEndpoint.Start
                    ? TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start
                    : TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
            return prefix.GetText(-1).Length;
        }

        var caret = GetCaret();
        var selection = GetSelection();
        var first = selection.Count > 0 ? selection[0] : null;
        return new StringTextDocument(text, OffsetOf(caret, TextEndpoint.Start),
            OffsetOf(first, TextEndpoint.Start), OffsetOf(first, TextEndpoint.End));
    }

    /// <summary>The text of the visible ranges, split into lines. STA thread only.</summary>
    public IReadOnlyList<string> GetVisibleLines(int maxLength = 100_000)
    {
        var builder = new System.Text.StringBuilder();
        var ranges = _pattern.GetVisibleRanges();
        for (int i = 0; ranges is not null && i < ranges.Length && builder.Length < maxLength; i++)
            builder.Append(ranges.GetElement(i).GetText(maxLength - builder.Length));
        return builder.ToString().Replace("\r\n", "\n").Split('\n');
    }

    private UIATextRange Wrap(IUIAutomationTextRange range) => new(range, _automation);
}

/// <summary><see cref="ITextRange"/> over a UIA text range. STA thread only.</summary>
public sealed class UIATextRange : ITextRange
{
    // Text attribute ids (UIAutomationClient.h)
    internal const int UIA_CultureAttributeId = 40004;
    internal const int UIA_FontNameAttributeId = 40005;
    internal const int UIA_FontSizeAttributeId = 40006;
    internal const int UIA_FontWeightAttributeId = 40007;
    internal const int UIA_ForegroundColorAttributeId = 40008;
    internal const int UIA_IsItalicAttributeId = 40014;
    internal const int UIA_UnderlineStyleAttributeId = 40029;
    internal const int UIA_AnnotationTypesAttributeId = 40031;

    private readonly IUIAutomation? _automation;

    public UIATextRange(IUIAutomationTextRange native, IUIAutomation? automation = null)
    {
        Native = native;
        _automation = automation;
    }

    public IUIAutomationTextRange Native { get; }

    public bool IsDegenerate =>
        Native.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start, Native,
            TextPatternRangeEndpoint.TextPatternRangeEndpoint_End) == 0;

    public string GetText(int maxLength = -1) => Native.GetText(maxLength) ?? string.Empty;

    public ITextRange ExpandToEnclosingUnit(TextUnit unit)
    {
        var clone = Native.Clone();
        clone.ExpandToEnclosingUnit(ToUia(unit));
        return new UIATextRange(clone, _automation);
    }

    public (ITextRange Range, int Moved) Move(TextUnit unit, int count)
    {
        var clone = Native.Clone();
        int moved = clone.Move(ToUia(unit), count);
        return (new UIATextRange(clone, _automation), moved);
    }

    public int CompareEndpoints(TextEndpoint endpoint, ITextRange other, TextEndpoint otherEndpoint) =>
        Native.CompareEndpoints(ToUia(endpoint), AsUia(other).Native, ToUia(otherEndpoint));

    public ITextRange WithEndpoint(TextEndpoint endpoint, ITextRange other, TextEndpoint otherEndpoint)
    {
        var clone = Native.Clone();
        clone.MoveEndpointByRange(ToUia(endpoint), AsUia(other).Native, ToUia(otherEndpoint));
        return new UIATextRange(clone, _automation);
    }

    public TextAttributes GetAttributes()
    {
        object? Read(int id)
        {
            try
            {
                var value = Native.GetAttributeValue(id);
                // "Mixed" and "not supported" are sentinel objects: treat them as unknown
                if (_automation is not null &&
                    (ReferenceEquals(value, _automation.ReservedMixedAttributeValue) ||
                     ReferenceEquals(value, _automation.ReservedNotSupportedValue)))
                    return null;
                return value;
            }
            catch
            {
                return null;
            }
        }

        return UIATextAttributes.FromValues(
            fontName: Read(UIA_FontNameAttributeId),
            fontSize: Read(UIA_FontSizeAttributeId),
            fontWeight: Read(UIA_FontWeightAttributeId),
            isItalic: Read(UIA_IsItalicAttributeId),
            underlineStyle: Read(UIA_UnderlineStyleAttributeId),
            foregroundColor: Read(UIA_ForegroundColorAttributeId),
            annotationTypes: Read(UIA_AnnotationTypesAttributeId),
            culture: Read(UIA_CultureAttributeId));
    }

    private static UIATextRange AsUia(ITextRange range) =>
        range as UIATextRange ?? throw new ArgumentException("Not a UIA text range", nameof(range));

    private static Interop.UIAutomationClient.TextUnit ToUia(TextUnit unit) => unit switch
    {
        TextUnit.Character => Interop.UIAutomationClient.TextUnit.TextUnit_Character,
        TextUnit.Word => Interop.UIAutomationClient.TextUnit.TextUnit_Word,
        TextUnit.Line => Interop.UIAutomationClient.TextUnit.TextUnit_Line,
        TextUnit.Paragraph => Interop.UIAutomationClient.TextUnit.TextUnit_Paragraph,
        _ => Interop.UIAutomationClient.TextUnit.TextUnit_Document,
    };

    private static TextPatternRangeEndpoint ToUia(TextEndpoint endpoint) => endpoint == TextEndpoint.Start
        ? TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start
        : TextPatternRangeEndpoint.TextPatternRangeEndpoint_End;
}

/// <summary>Maps raw UIA text attribute values to <see cref="TextAttributes"/>.</summary>
public static class UIATextAttributes
{
    private const int AnnotationType_SpellingError = 60001;
    private const int AnnotationType_GrammarError = 60002;
    private const int FontWeightBold = 600; // semibold and heavier read as bold

    public static TextAttributes FromValues(object? fontName, object? fontSize, object? fontWeight, object? isItalic,
        object? underlineStyle, object? foregroundColor, object? annotationTypes, object? culture)
    {
        var annotations = annotationTypes switch
        {
            int[] ints => ints,
            Array array => array.Cast<object>().Select(o => o is int i ? i : 0).ToArray(),
            int single => [single],
            _ => [],
        };

        return new TextAttributes
        {
            FontName = fontName as string is { Length: > 0 } name ? name : null,
            FontSize = fontSize is double size and > 0 ? size : null,
            IsBold = fontWeight is int weight ? weight >= FontWeightBold : null,
            IsItalic = isItalic is bool italic ? italic : null,
            IsUnderline = underlineStyle is int underline ? underline != 0 : null,
            ForegroundColor = foregroundColor is int color ? ColorName(color) : null,
            IsSpellingError = annotations.Contains(AnnotationType_SpellingError),
            IsGrammarError = annotations.Contains(AnnotationType_GrammarError),
            Language = culture is int lcid ? CultureName(lcid) : null,
        };
    }

    /// <summary>UIA colors are 0x00BBGGRR. Returns a hex RGB string such as "#FF0000".</summary>
    public static string ColorName(int bgr)
    {
        int r = bgr & 0xFF, g = (bgr >> 8) & 0xFF, b = (bgr >> 16) & 0xFF;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string? CultureName(int lcid)
    {
        if (lcid == 0)
            return null;
        try { return CultureInfo.GetCultureInfo(lcid).Name; }
        catch (CultureNotFoundException) { return null; }
    }
}
