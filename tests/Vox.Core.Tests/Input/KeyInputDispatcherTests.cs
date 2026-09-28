using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Input;

public class KeyInputDispatcherTests
{
    private static KeyMap BuildMap(string modifiers, int vkCode, string mode, string command)
    {
        var json = $$"""
        {
            "bindings": [
                { "modifiers": "{{modifiers}}", "vkCode": {{vkCode}}, "mode": "{{mode}}", "command": "{{command}}" }
            ]
        }
        """;
        return KeyMap.LoadFromJson(json);
    }

    /// <summary>
    /// Simple test double for IEventSink that records posted events.
    /// </summary>
    private sealed class CaptureSink : IEventSink
    {
        public List<ScreenReaderEvent> Posted { get; } = new();
        public void Post(ScreenReaderEvent evt) => Posted.Add(evt);
    }

    private static (KeyInputDispatcher dispatcher, CaptureSink sink, Action<KeyEvent> fireKey)
        Create(KeyMap keyMap)
    {
        var hookMock = new Mock<IKeyboardHook>();
        EventHandler<KeyEvent>? handler = null;
        hookMock.SetupAdd(h => h.KeyPressed += It.IsAny<EventHandler<KeyEvent>>())
            .Callback<EventHandler<KeyEvent>>(h => handler = h);

        var sink = new CaptureSink();
        var dispatcher = new KeyInputDispatcher(
            hookMock.Object, keyMap, sink, NullLogger<KeyInputDispatcher>.Instance);
        dispatcher.SetDocumentActive(true);
        dispatcher.Start();

        return (dispatcher, sink, evt => handler?.Invoke(hookMock.Object, evt));
    }

    [Fact]
    public void Start_SubscribesToKeyPressed()
    {
        var hookMock = new Mock<IKeyboardHook>();
        var keyMap = BuildMap("None", 72, "Browse", "NextHeading");
        var dispatcher = new KeyInputDispatcher(
            hookMock.Object, keyMap, new CaptureSink(), NullLogger<KeyInputDispatcher>.Instance);

        dispatcher.Start();

        hookMock.VerifyAdd(h => h.KeyPressed += It.IsAny<EventHandler<KeyEvent>>(), Times.Once);
    }

    [Fact]
    public void Stop_UnsubscribesFromKeyPressed()
    {
        var hookMock = new Mock<IKeyboardHook>();
        var keyMap = BuildMap("None", 72, "Browse", "NextHeading");
        var dispatcher = new KeyInputDispatcher(
            hookMock.Object, keyMap, new CaptureSink(), NullLogger<KeyInputDispatcher>.Instance);

        dispatcher.Start();
        dispatcher.Stop();

        hookMock.VerifyRemove(h => h.KeyPressed -= It.IsAny<EventHandler<KeyEvent>>(), Times.Once);
    }

    [Fact]
    public void SetMode_ChangesCurrentMode()
    {
        var hookMock = new Mock<IKeyboardHook>();
        var keyMap = BuildMap("None", 72, "Browse", "NextHeading");
        var dispatcher = new KeyInputDispatcher(
            hookMock.Object, keyMap, new CaptureSink(), NullLogger<KeyInputDispatcher>.Instance);

        Assert.Equal(InteractionMode.Browse, dispatcher.CurrentMode);

        dispatcher.SetMode(InteractionMode.Focus);

        Assert.Equal(InteractionMode.Focus, dispatcher.CurrentMode);
    }

    [Fact]
    public void KeyDown_WithMapping_PostsNavigationCommandEvent()
    {
        var keyMap = BuildMap("None", 72, "Browse", "NextHeading");
        var (_, sink, fireKey) = Create(keyMap);

        fireKey(new KeyEvent { VkCode = 72, Modifiers = KeyModifiers.None, IsKeyDown = true });

        Assert.Single(sink.Posted);
        var navCmd = Assert.IsType<NavigationCommandEvent>(sink.Posted[0]);
        Assert.Equal(NavigationCommand.NextHeading, navCmd.Command);
    }

    [Fact]
    public void KeyDown_WithoutMapping_PostsRawKeyEvent()
    {
        var keyMap = BuildMap("None", 72, "Browse", "NextHeading");
        var (_, sink, fireKey) = Create(keyMap);

        // 'A' key (vkCode=65) is not in the map
        fireKey(new KeyEvent { VkCode = 65, Modifiers = KeyModifiers.None, IsKeyDown = true });

        Assert.Single(sink.Posted);
        var raw = Assert.IsType<RawKeyEvent>(sink.Posted[0]);
        Assert.Equal(65, raw.Key.VkCode);
    }

    [Fact]
    public void KeyDown_MappedInBrowse_NotMappedInFocus_PostsRawKeyEvent()
    {
        var keyMap = BuildMap("None", 72, "Browse", "NextHeading");
        var (dispatcher, sink, fireKey) = Create(keyMap);
        dispatcher.SetMode(InteractionMode.Focus); // H not mapped in Focus mode

        fireKey(new KeyEvent { VkCode = 72, Modifiers = KeyModifiers.None, IsKeyDown = true });

        Assert.Single(sink.Posted);
        Assert.IsType<RawKeyEvent>(sink.Posted[0]);
    }

    [Fact]
    public void KeyUp_AlwaysPostsRawKeyEvent_EvenForMappedKey()
    {
        var keyMap = BuildMap("None", 72, "Browse", "NextHeading");
        var (_, sink, fireKey) = Create(keyMap);

        // Key-up event for H (which is mapped on key-down) — should be RawKeyEvent
        fireKey(new KeyEvent { VkCode = 72, Modifiers = KeyModifiers.None, IsKeyDown = false });

        Assert.Single(sink.Posted);
        Assert.IsType<RawKeyEvent>(sink.Posted[0]);
    }

    [Fact]
    public void KeyDown_WithInsertModifierMapping_PostsNavigationCommandEvent()
    {
        var keyMap = BuildMap("Insert", 40, "Any", "SayAll"); // Insert+Down
        var (_, sink, fireKey) = Create(keyMap);

        fireKey(new KeyEvent { VkCode = 40, Modifiers = KeyModifiers.Insert, IsKeyDown = true });

        Assert.Single(sink.Posted);
        var navCmd = Assert.IsType<NavigationCommandEvent>(sink.Posted[0]);
        Assert.Equal(NavigationCommand.SayAll, navCmd.Command);
    }

    [Fact]
    public void KeyDown_ModeSwitch_UsesUpdatedMode()
    {
        var json = """
        {
            "bindings": [
                { "modifiers": "None", "vkCode": 72, "mode": "Browse", "command": "NextHeading" },
                { "modifiers": "None", "vkCode": 75, "mode": "Focus", "command": "NextLink" }
            ]
        }
        """;
        var keyMap = KeyMap.LoadFromJson(json);
        var (dispatcher, sink, fireKey) = Create(keyMap);

        // In Browse mode, H -> NextHeading
        fireKey(new KeyEvent { VkCode = 72, Modifiers = KeyModifiers.None, IsKeyDown = true });
        Assert.Single(sink.Posted);
        Assert.Equal(NavigationCommand.NextHeading, ((NavigationCommandEvent)sink.Posted[0]).Command);

        // Switch to Focus mode
        dispatcher.SetMode(InteractionMode.Focus);
        sink.Posted.Clear();

        // In Focus mode, K -> NextLink
        fireKey(new KeyEvent { VkCode = 75, Modifiers = KeyModifiers.None, IsKeyDown = true });
        Assert.Single(sink.Posted);
        Assert.Equal(NavigationCommand.NextLink, ((NavigationCommandEvent)sink.Posted[0]).Command);
    }

    // -------------------------------------------------------------------------
    // Effective mode, document activity and key suppression
    // -------------------------------------------------------------------------

    private static KeyMap BuildMultiMap() => KeyMap.LoadFromJson("""
        {
            "bindings": [
                { "modifiers": "None",   "vkCode": 72,  "mode": "Browse", "command": "NextHeading" },
                { "modifiers": "Insert", "vkCode": 32,  "mode": "Any",    "command": "ToggleMode" },
                { "modifiers": "None",   "vkCode": 162, "mode": "Any",    "command": "StopSpeech", "passThrough": true }
            ]
        }
        """);

    [Fact]
    public void KeyDown_BrowseBinding_InFocusMode_PostsRawKeyEvent()
    {
        var (dispatcher, sink, fireKey) = Create(BuildMultiMap());
        dispatcher.SetMode(InteractionMode.Focus);

        fireKey(new KeyEvent { VkCode = 72, IsKeyDown = true });

        Assert.IsType<RawKeyEvent>(Assert.Single(sink.Posted));
    }

    [Fact]
    public void KeyDown_BrowseBinding_WithoutActiveDocument_PostsRawKeyEvent()
    {
        var (dispatcher, sink, fireKey) = Create(BuildMultiMap());
        dispatcher.SetDocumentActive(false);

        fireKey(new KeyEvent { VkCode = 72, IsKeyDown = true });

        Assert.Equal(InteractionMode.Focus, dispatcher.EffectiveMode);
        Assert.IsType<RawKeyEvent>(Assert.Single(sink.Posted));
    }

    [Fact]
    public void ShouldSuppress_BoundKey_ReturnsTrue()
    {
        var (dispatcher, _, _) = Create(BuildMultiMap());

        Assert.True(dispatcher.ShouldSuppress(new KeyEvent { VkCode = 72, IsKeyDown = true }));
        Assert.True(dispatcher.ShouldSuppress(
            new KeyEvent { VkCode = 32, Modifiers = KeyModifiers.Insert, IsKeyDown = true }));
    }

    [Fact]
    public void ShouldSuppress_UnboundOrPassThroughKey_ReturnsFalse()
    {
        var (dispatcher, _, _) = Create(BuildMultiMap());

        Assert.False(dispatcher.ShouldSuppress(new KeyEvent { VkCode = 65, IsKeyDown = true }));
        Assert.False(dispatcher.ShouldSuppress(new KeyEvent { VkCode = 162, IsKeyDown = true }));
    }

    [Fact]
    public void ShouldSuppress_BrowseKey_InFocusMode_ReturnsFalse()
    {
        var (dispatcher, _, _) = Create(BuildMultiMap());
        dispatcher.SetMode(InteractionMode.Focus);

        Assert.False(dispatcher.ShouldSuppress(new KeyEvent { VkCode = 72, IsKeyDown = true }));
        Assert.True(dispatcher.ShouldSuppress(
            new KeyEvent { VkCode = 32, Modifiers = KeyModifiers.Insert, IsKeyDown = true }));
    }

    [Fact]
    public void Start_InstallsSuppressionFilter_StopRemovesIt()
    {
        var hookMock = new Mock<IKeyboardHook>();
        hookMock.SetupProperty(h => h.SuppressionFilter);
        var dispatcher = new KeyInputDispatcher(
            hookMock.Object, BuildMultiMap(), new CaptureSink(), NullLogger<KeyInputDispatcher>.Instance);

        dispatcher.Start();
        Assert.NotNull(hookMock.Object.SuppressionFilter);

        dispatcher.Stop();
        Assert.Null(hookMock.Object.SuppressionFilter);
    }
}
