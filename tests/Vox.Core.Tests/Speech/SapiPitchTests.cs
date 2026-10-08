using System.Xml.Linq;
using Moq;
using Vox.Core.Speech;
using Xunit;

namespace Vox.Core.Tests.Speech;

public class SapiPitchTests
{
    [Theory]
    [InlineData(50, "+0%")]
    [InlineData(100, "+50%")]
    [InlineData(0, "-50%")]
    [InlineData(70, "+20%")]
    [InlineData(-5, "-50%")]
    [InlineData(150, "+50%")]
    public void PitchToProsody_IsRelativeToNormalPitch(int pitch, string expected)
    {
        Assert.Equal(expected, SapiSpeechEngine.PitchToProsody(pitch));
    }

    [Fact]
    public void BuildPitchSsml_IsValidXml_WithTheTextEscaped()
    {
        var ssml = SapiSpeechEngine.BuildPitchSsml("Tom & Jerry <3 \"quotes\"", 70, "en-GB");

        var speak = XElement.Parse(ssml);
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        Assert.Equal("en-GB", speak.Attribute(XNamespace.Xml + "lang")?.Value);
        var prosody = Assert.Single(speak.Elements(ns + "prosody"));
        Assert.Equal("+20%", prosody.Attribute("pitch")?.Value);
        Assert.Equal("Tom & Jerry <3 \"quotes\"", prosody.Value);
    }

    [Fact]
    public void EnginesWithoutTheCapabilities_IgnorePitchAndVolume()
    {
        // An engine that implements only the required members keeps the defaults
        ISpeechEngine engine = new Mock<ISpeechEngine> { CallBase = true }.Object;

        engine.SetPitch(80);
        engine.SetVolume(10);

        Assert.Equal(SpeechCapabilities.None, engine.Capabilities);
        Assert.Empty(engine.GetLanguages());
    }
}
