using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Input;

public class NumpadKeysTests
{
    private const int VK_UP = 0x26;
    private const int VK_RETURN = 0x0D;
    private const int VK_INSERT = 0x2D;

    [Theory]
    [InlineData(VK_UP, false, true)]      // keypad 8, Num Lock off
    [InlineData(VK_UP, true, false)]      // dedicated Up arrow
    [InlineData(VK_RETURN, true, true)]   // keypad Enter
    [InlineData(VK_RETURN, false, false)] // main Enter
    [InlineData(0x41, false, false)]      // A
    public void IsKeypad_UsesTheExtendedFlag(int vk, bool extended, bool expected) =>
        Assert.Equal(expected, NumpadKeys.IsKeypad(vk, extended));

    [Fact]
    public void KeypadArrow_ResolvesAsKeypadThenArrow() =>
        Assert.Equal((NumpadKeys.Numpad8, (int?)VK_UP), NumpadKeys.BindingCodes(VK_UP, isKeypad: true));

    [Fact]
    public void KeypadEnter_ResolvesAsNumpadEnterThenEnter() =>
        Assert.Equal((NumpadKeys.NumpadEnter, (int?)VK_RETURN), NumpadKeys.BindingCodes(VK_RETURN, isKeypad: true));

    [Fact]
    public void DedicatedKey_ResolvesAsItself() =>
        Assert.Equal((VK_UP, (int?)null), NumpadKeys.BindingCodes(VK_UP, isKeypad: false));

    [Fact]
    public void NumLockDigits_AreNeverBound() =>
        Assert.Equal(-1, NumpadKeys.BindingCodes(NumpadKeys.Numpad8, isKeypad: false).Primary);

    private sealed class CaptureSink : IEventSink
    {
        public List<ScreenReaderEvent> Posted { get; } = new();
        public void Post(ScreenReaderEvent evt) => Posted.Add(evt);
    }

    private sealed class Harness
    {
        private readonly CaptureSink _sink = new();
        private EventHandler<KeyEvent>? _keyPressed;

        public Harness(bool documentActive)
        {
            var hook = new Mock<IKeyboardHook>();
            hook.SetupAdd(h => h.KeyPressed += It.IsAny<EventHandler<KeyEvent>>())
                .Callback<EventHandler<KeyEvent>>(h => _keyPressed = h);
            Dispatcher = new KeyInputDispatcher(hook.Object, KeyMap.LoadBuiltIn(), _sink, NullLogger<KeyInputDispatcher>.Instance);
            Dispatcher.SetDocumentActive(documentActive);
            Dispatcher.Start();
        }

        public KeyInputDispatcher Dispatcher { get; }

        /// <summary>Presses the key as the hook would (decision first) and returns the command it ran.</summary>
        public NavigationCommand? Press(KeyEvent key)
        {
            _sink.Posted.Clear();
            key = key with { IsKeyDown = true };
            key = key with { Decision = Dispatcher.Decide(key) };
            _keyPressed?.Invoke(null, key);
            return _sink.Posted.OfType<NavigationCommandEvent>().SingleOrDefault()?.Command;
        }
    }

    [Fact]
    public void InsertKeypad8_IsNavigatorParent_InsertUp_IsReadCurrentLine()
    {
        var keys = new Harness(documentActive: false);

        Assert.Equal(NavigationCommand.NavigatorParent,
            keys.Press(new KeyEvent { VkCode = VK_UP, IsKeypad = true, Modifiers = KeyModifiers.Insert }));
        Assert.Equal(NavigationCommand.ReadCurrentLine,
            keys.Press(new KeyEvent { VkCode = VK_UP, Modifiers = KeyModifiers.Insert }));
    }

    [Fact]
    public void UnboundKeypadArrow_WorksAsTheArrow()
    {
        var keys = new Harness(documentActive: true);
        keys.Dispatcher.SetMode(InteractionMode.Browse);

        Assert.Equal(NavigationCommand.NextLine,
            keys.Press(new KeyEvent { VkCode = 0x28, IsKeypad = true }));
    }

    [Fact]
    public void InsertKeypadEnter_ActivatesTheNavigator()
    {
        var keys = new Harness(documentActive: false);

        Assert.Equal(NavigationCommand.ActivateNavigator,
            keys.Press(new KeyEvent { VkCode = VK_RETURN, IsKeypad = true, Modifiers = KeyModifiers.Insert }));
    }

    [Theory]
    [InlineData(NumpadKeys.Subtract, KeyModifiers.Insert, NavigationCommand.NavigatorToFocus)]
    [InlineData(NumpadKeys.Subtract, KeyModifiers.Insert | KeyModifiers.Shift, NavigationCommand.FocusToNavigator)]
    [InlineData(0x0C, KeyModifiers.Insert, NavigationCommand.ReportNavigator)] // keypad 5 sends Clear
    [InlineData(0x28, KeyModifiers.Insert, NavigationCommand.NavigatorFirstChild)]
    [InlineData(0x25, KeyModifiers.Insert, NavigationCommand.NavigatorPrevious)]
    [InlineData(0x27, KeyModifiers.Insert, NavigationCommand.NavigatorNext)]
    public void ObjectNavigationBindings(int vk, KeyModifiers modifiers, NavigationCommand expected)
    {
        var keys = new Harness(documentActive: false);

        Assert.Equal(expected, keys.Press(new KeyEvent { VkCode = vk, IsKeypad = NumpadKeys.IsKeypad(vk, extendedFlag: false), Modifiers = modifiers }));
    }

    [Fact]
    public void NumLockDigit_IsNotACommand()
    {
        var keys = new Harness(documentActive: false);

        Assert.Null(keys.Press(new KeyEvent { VkCode = NumpadKeys.Numpad8, Modifiers = KeyModifiers.Insert }));
    }
}
