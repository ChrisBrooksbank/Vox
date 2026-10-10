using System.Text.RegularExpressions;

namespace Vox.Core.Accessibility;

/// <summary>
/// Tells web content apart from the rest of the UIA tree, for finding the document to browse.
/// Chromium (Chrome, Edge, Electron, WebView2) reports the "Chrome" framework, and Firefox's own
/// UIA support "Gecko"; through Windows' MSAA-to-UIA proxy (Firefox without native UIA) its
/// elements carry no such framework, so anything inside a Firefox window that isn't a plain
/// Win32 window counts as well.
/// </summary>
public static partial class WebContent
{
    private static readonly HashSet<string> WebFrameworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chrome", // Chrome, Edge, Electron, WebView2
        "Gecko",  // Firefox's native UIA
    };

    /// <summary>True for a Firefox top-level or content window class (MozillaWindowClass, MozillaDialogClass...).</summary>
    public static bool IsFirefoxWindowClass(string? className) =>
        className is not null && className.StartsWith("Mozilla", StringComparison.Ordinal);

    /// <summary>
    /// Whether an element is web content: its framework is a browser engine's, or it is inside a
    /// Firefox window and isn't a Win32 window itself.
    /// </summary>
    public static bool IsWebElement(string? frameworkId, bool insideFirefoxWindow)
    {
        if (frameworkId is not null && WebFrameworks.Contains(frameworkId))
            return true;
        return insideFirefoxWindow && !string.Equals(frameworkId, "Win32", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A heading level from the localized control type ("heading", "heading 2", "heading level 3"),
    /// for providers that expose no HeadingLevel property (Firefox through the MSAA proxy); 0 when
    /// it isn't a heading. English names only.
    /// </summary>
    public static int HeadingLevelFromLocalizedType(string? localizedControlType)
    {
        if (string.IsNullOrWhiteSpace(localizedControlType))
            return 0;
        var match = HeadingType().Match(localizedControlType.Trim());
        if (!match.Success)
            return 0;
        return match.Groups[1].Success && int.TryParse(match.Groups[1].Value, out var level) && level is >= 1 and <= 6
            ? level
            : 2;
    }

    [GeneratedRegex(@"^heading(?:\s+(?:level\s+)?(\d))?$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingType();
}
