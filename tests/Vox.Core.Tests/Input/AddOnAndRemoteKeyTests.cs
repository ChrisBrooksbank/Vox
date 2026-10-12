using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Vox.Sdk;
using Xunit;

namespace Vox.Core.Tests.Input;

/// <summary>The dispatcher's add-on key layer and its forwarding of keys to a computer controlled remotely.</summary>
public class AddOnAndRemoteKeyTests
{
    private sealed class CaptureSink : IEventSink
    {
        public List<ScreenReaderEvent> Posted { get; } = [];
        public void Post(ScreenReaderEvent evt) => Posted.Add(evt);
    }

    private static readonly AddOnCommandRef Hello = new("vox.sample", "sayHello", "Say hello", "Says hello.");

    private static (KeyInputDispatcher Dispatcher, CaptureSink Sink, Func<KeyEvent, KeyEvent> Press) Create(string keymapJson, ModifierKey modifier = ModifierKey.Insert)
    {
        var hook = new Mock<IKeyboardHook>();
        hook.SetupProperty(h => h.ScreenReaderModifier, modifier);
        EventHandler<KeyEvent>? handler = null;
        hook.SetupAdd(h => h.KeyPressed += It.IsAny<EventHandler<KeyEvent>>()).Callback<EventHandler<KeyEvent>>(h => handler = h);
        var sink = new CaptureSink();
        var dispatcher = new KeyInputDispatcher(hook.Object, KeyMap.LoadFromJson(keymapJson), sink, NullLogger<KeyInputDispatcher>.Instance);
        dispatcher.Start();
        // As the hook does: decide on key-downs, then hand the key over with the decision
        return (dispatcher, sink, key =>
        {
            var decided = key with { Decision = key.IsKeyDown ? dispatcher.Decide(key) : key.Decision };
            handler?.Invoke(hook.Object, decided);
            return decided;
        });
    }

    private const string Keymap = """
        { "bindings": [
          { "modifiers": "Insert|Ctrl", "vkCode": 123, "mode": "Any", "command": "SayTime" },
          { "modifiers": "Insert", "vkCode": 84, "mode": "Any", "command": "SayTitle" },
          { "modifiers": "Insert", "vkCode": 122, "mode": "Any", "command": "ToggleRemoteControl" },
          { "modifiers": "Insert", "vkCode": 49, "mode": "Any", "command": "ToggleInputHelp" }
        ] }
        """;

    [Fact]
    public void AnAddOnsKey_RunsItsCommand_BeforeVoxsOwn()
    {
        var (dispatcher, sink, press) = Create(Keymap);
        dispatcher.SetAddOnKeys(AddOnKeyLayer.Build([(KeyGesture.Parse("VoxKey+Ctrl+F12"), Hello)]));

        var key = press(new KeyEvent { VkCode = 123, Modifiers = KeyModifiers.Insert | KeyModifiers.Ctrl, IsKeyDown = true });

        Assert.True(key.Decision.Suppress);
        var posted = Assert.IsType<AddOnCommandEvent>(Assert.Single(sink.Posted));
        Assert.Same(Hello, posted.Command);
    }

    [Fact]
    public void WithoutTheLayer_VoxsKeyWorksAsBefore()
    {
        var (dispatcher, sink, press) = Create(Keymap);
        dispatcher.SetAddOnKeys(AddOnKeyLayer.Build([(KeyGesture.Parse("VoxKey+Ctrl+F12"), Hello)]));
        dispatcher.SetAddOnKeys(null);

        press(new KeyEvent { VkCode = 123, Modifiers = KeyModifiers.Insert | KeyModifiers.Ctrl, IsKeyDown = true });

        Assert.Equal(NavigationCommand.SayTime, Assert.IsType<NavigationCommandEvent>(Assert.Single(sink.Posted)).Command);
    }

    [Fact]
    public void InputHelp_DescribesAnAddOnsKey()
    {
        var (dispatcher, sink, press) = Create(Keymap);
        dispatcher.SetAddOnKeys(AddOnKeyLayer.Build([(KeyGesture.Parse("VoxKey+Ctrl+F12"), Hello)]));
        dispatcher.IsInputHelpOn = true;

        press(new KeyEvent { VkCode = 123, Modifiers = KeyModifiers.Insert | KeyModifiers.Ctrl, IsKeyDown = true });

        var help = Assert.IsType<InputHelpEvent>(Assert.Single(sink.Posted));
        Assert.Same(Hello, help.AddOnCommand);
        Assert.Contains("Say hello", InputHelp.DescribeAddOnCommand(help.AddOnCommand!, help.Modifiers, help.VkCode, help.IsKeypad));
    }

    [Fact]
    public void WhileForwarding_EveryKeyGoesToTheOtherComputer_DownAndUp()
    {
        var (dispatcher, sink, press) = Create(Keymap);
        var forwarded = new List<KeyEvent>();
        dispatcher.KeyForwarder = forwarded.Add;

        var down = press(new KeyEvent { VkCode = 84, Modifiers = KeyModifiers.Insert, IsKeyDown = true });
        press(new KeyEvent { VkCode = 84, Modifiers = KeyModifiers.Insert, IsKeyDown = false, Decision = down.Decision });
        var typed = press(new KeyEvent { VkCode = 65, IsKeyDown = true });
        press(new KeyEvent { VkCode = 65, IsKeyDown = false, Decision = typed.Decision });

        Assert.True(down.Decision.Suppress);
        Assert.True(typed.Decision.Suppress);
        Assert.Equal([(84, true), (84, false), (65, true), (65, false)], forwarded.Select(k => (k.VkCode, k.IsKeyDown)).ToList());
        Assert.Empty(sink.Posted);
    }

    [Fact]
    public void WhileForwarding_TheScreenReaderKeyItselfGoesToo()
    {
        var (dispatcher, _, press) = Create(Keymap);
        var forwarded = new List<KeyEvent>();
        dispatcher.KeyForwarder = forwarded.Add;
        // The hook swallows the screen reader key itself without asking (decision without context)
        press(new KeyEvent { VkCode = 0x2D, Modifiers = KeyModifiers.Insert, IsKeyDown = false, Decision = KeyDecision.Swallow });

        var key = Assert.Single(forwarded);
        Assert.True(dispatcher.IsScreenReaderKey(key));
    }

    [Fact]
    public void WhileForwarding_TheToggleKeyStaysOnThisComputer()
    {
        var (dispatcher, sink, press) = Create(Keymap);
        var forwarded = new List<KeyEvent>();
        dispatcher.KeyForwarder = forwarded.Add;

        var toggle = press(new KeyEvent { VkCode = 122, Modifiers = KeyModifiers.Insert, IsKeyDown = true });

        Assert.Empty(forwarded);
        Assert.Equal(NavigationCommand.ToggleRemoteControl, Assert.IsType<NavigationCommandEvent>(Assert.Single(sink.Posted)).Command);
        Assert.True(toggle.Decision.Suppress);
    }

    [Fact]
    public void AKeyDownSentOver_SendsItsKeyUpEvenAfterForwardingStops()
    {
        var (dispatcher, sink, press) = Create(Keymap);
        var forwarded = new List<KeyEvent>();
        dispatcher.KeyForwarder = forwarded.Add;
        var down = press(new KeyEvent { VkCode = 65, IsKeyDown = true });

        dispatcher.KeyForwarder = null;
        press(new KeyEvent { VkCode = 65, IsKeyDown = false, Decision = down.Decision });

        Assert.Single(forwarded);
        // Not typed here either: its key-down went to the other computer
        Assert.Empty(sink.Posted);
    }

    [Theory]
    [InlineData(ModifierKey.Insert, 0x2D, true)]
    [InlineData(ModifierKey.Insert, 0x14, false)]
    [InlineData(ModifierKey.CapsLock, 0x14, true)]
    public void TheScreenReaderKey_FollowsTheSetting(ModifierKey modifier, int vk, bool expected)
    {
        var (dispatcher, _, _) = Create(Keymap, modifier);

        Assert.Equal(expected, dispatcher.IsScreenReaderKey(new KeyEvent { VkCode = vk }));
    }
}
