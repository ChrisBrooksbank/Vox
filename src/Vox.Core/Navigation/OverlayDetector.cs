using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>
/// An overlay found on a page: a modal dialog or a cookie/consent banner, and the button that
/// rejects or closes it (null when there is none).
/// </summary>
public sealed record Overlay(VBufferNode Container, string Kind, VBufferNode? DismissButton);

/// <summary>
/// Finds overlays (an open modal dialog, a cookie or consent banner) and the button that rejects
/// or closes them, so the user can be told about one and press that button with one key. Never
/// presses anything itself.
/// </summary>
public static class OverlayDetector
{
    public const string CookieBanner = "Cookie banner";
    public const string Dialog = "Dialog";

    // A banner's text is short; anything longer is the page itself
    private const int MaxBannerTextLength = 3000;
    private const int MaxBannerDepth = 8;

    private static readonly string[] ConsentWords = ["cookie", "consent", "gdpr"];

    // Preferred first: rejecting (or keeping only what is needed) over just closing
    private static readonly string[] RejectWords =
    [
        "reject all", "reject", "decline", "deny", "refuse", "only necessary", "necessary only",
        "essential only", "only essential", "necessary cookies", "essential cookies", "no thanks", "no, thanks",
    ];
    private static readonly string[] CloseWords = ["close", "dismiss", "not now", "maybe later", "continue without"];
    private static readonly string[] CloseSymbols = ["x", "×", "✕", "✖"];

    // A button that also accepts ("Accept and close") is never the one to press
    private static readonly string[] AcceptWords = ["accept", "agree", "allow", "ok", "got it"];

    public static Overlay? Find(VBufferDocument document)
    {
        // An open modal dialog
        var modal = document.AllNodes.LastOrDefault(n => n.IsModal);
        if (modal is not null)
        {
            var kind = MentionsConsent(document, modal) ? CookieBanner : Dialog;
            return new Overlay(modal, kind, DismissButton(document, modal));
        }

        // A consent banner (often not a dialog at all): the smallest container around one of its
        // buttons whose text mentions cookies or consent
        foreach (var button in Candidates(document))
        {
            var container = button.Parent;
            for (int depth = 0; container is not null && container.Parent is not null && depth < MaxBannerDepth;
                 depth++, container = container.Parent)
            {
                var (start, end) = VBufferDocument.SubtreeSpan(container);
                if (end - start > MaxBannerTextLength)
                    break;
                if (MentionsConsent(document, container))
                    return new Overlay(container, CookieBanner, DismissButton(document, container));
            }
        }
        return null;
    }

    /// <summary>What to say about an overlay, naming the key that presses its dismiss button.</summary>
    public static string Announcement(Overlay overlay, string key)
    {
        var kind = overlay.Kind == Dialog && overlay.Container.Name.Trim() is { Length: > 0 } name
            ? $"Dialog, {name}"
            : overlay.Kind;
        return overlay.DismissButton is { } button
            ? $"{kind}. {key} presses {button.Name.Trim()}"
            : kind;
    }

    private static IEnumerable<VBufferNode> Candidates(VBufferDocument document) =>
        document.Buttons.Concat(document.Links).Where(n => n.Name.Length > 0);

    private static bool MentionsConsent(VBufferDocument document, VBufferNode container)
    {
        var (start, end) = VBufferDocument.SubtreeSpan(container);
        if (end <= start || end > document.FlatText.Length)
            return false;
        var text = document.FlatText.AsSpan(start, end - start);
        foreach (var word in ConsentWords)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static VBufferNode? DismissButton(VBufferDocument document, VBufferNode container)
    {
        var inside = Candidates(document).Where(n => IsInside(n, container)).ToList();
        foreach (var word in RejectWords)
        {
            var match = inside.FirstOrDefault(n => Matches(n.Name, word));
            if (match is not null)
                return match;
        }
        foreach (var word in CloseWords)
        {
            var match = inside.FirstOrDefault(n => Matches(n.Name, word));
            if (match is not null)
                return match;
        }
        return inside.FirstOrDefault(n => CloseSymbols.Contains(n.Name.Trim(), StringComparer.OrdinalIgnoreCase));
    }

    private static bool Matches(string name, string word)
    {
        var text = name.Trim();
        if (!text.Contains(word, StringComparison.OrdinalIgnoreCase))
            return false;
        // "Accept and close", "Allow all": not a way out
        foreach (var accept in AcceptWords)
        {
            if (ContainsWord(text, accept))
                return false;
        }
        return true;
    }

    private static bool ContainsWord(string text, string word)
    {
        int index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool startsWord = index == 0 || !char.IsLetter(text[index - 1]);
            int after = index + word.Length;
            bool endsWord = after >= text.Length || !char.IsLetter(text[after]);
            if (startsWord && endsWord)
                return true;
            index = after;
        }
        return false;
    }

    private static bool IsInside(VBufferNode node, VBufferNode container)
    {
        for (var n = node.Parent; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, container))
                return true;
        }
        return false;
    }
}
