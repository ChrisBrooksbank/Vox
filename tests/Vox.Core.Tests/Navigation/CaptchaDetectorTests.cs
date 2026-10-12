using Vox.Core.Buffer;
using Vox.Core.Navigation;
using Vox.Core.Tests.Buffer;
using Xunit;

namespace Vox.Core.Tests.Navigation;

public class CaptchaDetectorTests
{
    private static int _id = 7000;

    private static MockElement Element(string name, string controlType = "Group") =>
        new() { RuntimeId = [_id++], Name = name, ControlType = controlType };

    private static VBufferDocument Page(params MockElement[] children)
    {
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document", Name = "Sign up" };
        root.AddChild(Element("Create your account", "Text"));
        foreach (var child in children)
            root.AddChild(child);
        return new VBufferBuilder().Build(root);
    }

    [Fact]
    public void ReCaptcha_PointsToItsAudioChallenge()
    {
        var challenge = Element("recaptcha challenge expires in two minutes", "Pane")
            .AddChild(Element("Get an audio challenge", "Button"));
        var document = Page(Element("reCAPTCHA", "Pane").AddChild(Element("I'm not a robot", "CheckBox")), challenge);

        var captcha = Assert.IsType<Captcha>(CaptchaDetector.Find(document));

        Assert.Equal(CaptchaKind.ReCaptcha, captcha.Kind);
        Assert.Equal("Get an audio challenge", captcha.AudioAlternative!.Name);
        var said = CaptchaDetector.Announcement(captcha);
        Assert.StartsWith("reCAPTCHA on this page.", said);
        Assert.EndsWith("Audio alternative: Get an audio challenge button.", said);
    }

    [Fact]
    public void HCaptcha_PointsToItsAccessibilityOption()
    {
        var document = Page(Element("Widget containing checkbox for hCaptcha security challenge", "Pane"));

        var captcha = Assert.IsType<Captcha>(CaptchaDetector.Find(document));

        Assert.Equal(CaptchaKind.HCaptcha, captcha.Kind);
        Assert.Null(captcha.AudioAlternative);
        Assert.Contains("hcaptcha.com/accessibility", CaptchaDetector.Announcement(captcha));
    }

    [Fact]
    public void Turnstile_UsuallyPassesByItself()
    {
        var document = Page(Element("Widget containing a Cloudflare security challenge", "Pane"));

        var captcha = Assert.IsType<Captcha>(CaptchaDetector.Find(document));

        Assert.Equal(CaptchaKind.Turnstile, captcha.Kind);
        Assert.Contains("passes by itself", CaptchaDetector.Announcement(captcha));
    }

    [Fact]
    public void ATextCaptcha_FindsAnAudioButtonBesideIt()
    {
        var form = Element("Security check").AddChild(Element("CAPTCHA image", "Image"))
            .AddChild(Element("Play audio CAPTCHA", "Button"))
            .AddChild(Element("Enter the characters", "Edit"));
        var document = Page(form);

        var captcha = Assert.IsType<Captcha>(CaptchaDetector.Find(document));

        Assert.Equal(CaptchaKind.Other, captcha.Kind);
        Assert.Equal("Play audio CAPTCHA", captcha.AudioAlternative!.Name);
    }

    [Fact]
    public void ProseAboutCaptchas_IsNotOne()
    {
        var root = new MockElement { RuntimeId = [_id++], ControlType = "Document", Name = "Why CAPTCHAs are hard" };
        root.AddChild(new MockElement { RuntimeId = [_id++], Name = "What is a CAPTCHA?", AriaRole = "heading", AriaProperties = "level=1" });
        root.AddChild(Element("A CAPTCHA asks you to prove you are human.", "Text"));
        root.AddChild(Element("Learn about reCAPTCHA", "Hyperlink"));

        Assert.Null(CaptchaDetector.Find(new VBufferBuilder().Build(root)));
    }

    [Fact]
    public void APageWithout_HasNone()
    {
        Assert.Null(CaptchaDetector.Find(Page(Element("Email", "Edit"), Element("Sign up", "Button"))));
    }
}
