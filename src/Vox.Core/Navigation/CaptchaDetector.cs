using Vox.Core.Buffer;
using Vox.Core.Localization;

namespace Vox.Core.Navigation;

/// <summary>The kinds of CAPTCHA Vox knows the accessible way through.</summary>
public enum CaptchaKind
{
    /// <summary>Google reCAPTCHA: a check box, then perhaps a picture challenge with an audio one beside it.</summary>
    ReCaptcha,
    /// <summary>hCaptcha: picture challenges; its accessibility cookie lets blind people through.</summary>
    HCaptcha,
    /// <summary>Cloudflare Turnstile: usually passes by itself, or with one check box.</summary>
    Turnstile,
    /// <summary>Any other: distorted text, a puzzle.</summary>
    Other,
}

/// <summary>A CAPTCHA found on a page, and its audio alternative when the page has one.</summary>
public sealed record Captcha(VBufferNode Node, CaptchaKind Kind, VBufferNode? AudioAlternative);

/// <summary>
/// Finds CAPTCHAs on a page so they can be announced with the accessible way through: the audio
/// challenge, an accessibility option, or waiting. Vox never tries to solve one.
/// </summary>
public static class CaptchaDetector
{
    // Words that make a button or link the audio alternative
    private static readonly string[] AudioWords = ["audio", "listen", "play sound", "hear"];

    public static Captcha? Find(VBufferDocument document)
    {
        foreach (var node in document.AllNodes)
        {
            if (Classify(node) is { } kind)
                return new Captcha(node, kind, AudioAlternative(document, kind));
        }
        return null;
    }

    /// <summary>What kind of CAPTCHA a node is (by its name or description), or null.</summary>
    public static CaptchaKind? Classify(VBufferNode node)
    {
        // A widget (frame, image, check box, field), not the page or prose about CAPTCHAs
        if (node.Parent is null || node.IsHeading || node.IsLink
            || string.Equals(node.ControlType, "Text", StringComparison.OrdinalIgnoreCase))
            return null;
        var text = $"{node.Name} {node.Description} {node.RoleDescription}";
        if (text.Length < 4)
            return null;
        if (text.Contains("recaptcha", StringComparison.OrdinalIgnoreCase))
            return CaptchaKind.ReCaptcha;
        if (text.Contains("hcaptcha", StringComparison.OrdinalIgnoreCase))
            return CaptchaKind.HCaptcha;
        if (text.Contains("turnstile", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Cloudflare security challenge", StringComparison.OrdinalIgnoreCase))
            return CaptchaKind.Turnstile;
        if (text.Contains("captcha", StringComparison.OrdinalIgnoreCase)
            || text.Contains("I'm not a robot", StringComparison.OrdinalIgnoreCase)
            || text.Contains("I am not a robot", StringComparison.OrdinalIgnoreCase))
            return CaptchaKind.Other;
        return null;
    }

    /// <summary>What to say about <paramref name="captcha"/>: what it is and the accessible way through it.</summary>
    public static string Announcement(Captcha captcha)
    {
        var guidance = VoxStrings.Get($"Captcha.{captcha.Kind}");
        return captcha.AudioAlternative is { } audio
            ? $"{guidance} {VoxStrings.Format("Captcha.AudioButton", audio.Name.Trim())}"
            : guidance;
    }

    private static VBufferNode? AudioAlternative(VBufferDocument document, CaptchaKind kind)
    {
        // hCaptcha and Turnstile have no audio challenge: an "audio" button there is the page's own
        if (kind is CaptchaKind.HCaptcha or CaptchaKind.Turnstile)
            return null;
        return document.Buttons.Concat(document.Links)
            .Where(n => n.Name.Length > 0 && document.InScope(n))
            .FirstOrDefault(n => AudioWords.Any(word => n.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
                && (n.Name.Contains("challenge", StringComparison.OrdinalIgnoreCase)
                    || n.Name.Contains("captcha", StringComparison.OrdinalIgnoreCase)
                    || InsideCaptcha(n)));
    }

    // An audio button inside the CAPTCHA's own frame or form ("Play", "Listen")
    private static bool InsideCaptcha(VBufferNode node)
    {
        for (var n = node.Parent; n is not null; n = n.Parent)
        {
            if (Classify(n) is not null)
                return true;
        }
        return false;
    }
}
