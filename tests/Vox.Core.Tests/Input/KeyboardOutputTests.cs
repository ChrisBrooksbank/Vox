using Vox.Core.Input;
using Xunit;

namespace Vox.Core.Tests.Input;

/// <summary><see cref="OutputKey.Parse"/>: keys as braille key maps write them.</summary>
public class KeyboardOutputTests
{
    [Theory]
    [InlineData("Enter", 0x0D, KeyModifiers.None)]
    [InlineData("enter", 0x0D, KeyModifiers.None)]
    [InlineData("Backspace", 0x08, KeyModifiers.None)]
    [InlineData("Escape", 0x1B, KeyModifiers.None)]
    [InlineData("Down", 0x28, KeyModifiers.None)]
    [InlineData("PageUp", 0x21, KeyModifiers.None)]
    [InlineData("Shift+Tab", 0x09, KeyModifiers.Shift)]
    [InlineData("Ctrl+Home", 0x24, KeyModifiers.Ctrl)]
    [InlineData("Control+End", 0x23, KeyModifiers.Ctrl)]
    [InlineData("Alt+F4", 0x73, KeyModifiers.Alt)]
    [InlineData("Ctrl+Shift+Left", 0x25, KeyModifiers.Ctrl | KeyModifiers.Shift)]
    [InlineData(" Ctrl + Home ", 0x24, KeyModifiers.Ctrl)]
    [InlineData("F1", 0x70, KeyModifiers.None)]
    [InlineData("F5", 0x74, KeyModifiers.None)]
    [InlineData("f12", 0x7B, KeyModifiers.None)]
    [InlineData("F24", 0x87, KeyModifiers.None)]
    [InlineData("a", 0x41, KeyModifiers.None)]
    [InlineData("Z", 0x5A, KeyModifiers.None)]
    [InlineData("7", 0x37, KeyModifiers.None)]
    [InlineData("Ctrl+c", 0x43, KeyModifiers.Ctrl)]
    public void Parse_KnownKeys(string text, int vk, KeyModifiers modifiers)
    {
        Assert.Equal(new OutputKey(vk, modifiers), OutputKey.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("Hyper+A")]
    [InlineData("Enterr")]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("Fx")]
    [InlineData("é")]
    [InlineData("ab")]
    [InlineData("Ctrl+")]
    [InlineData("Shift+Hyper")]
    public void Parse_UnknownKeys_AreNull(string text)
    {
        Assert.Null(OutputKey.Parse(text));
    }
}
