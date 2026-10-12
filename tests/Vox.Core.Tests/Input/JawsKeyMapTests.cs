using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vox.Core.Configuration;
using Vox.Core.Input;
using Vox.Core.Pipeline;
using Xunit;

namespace Vox.Core.Tests.Input;

public class JawsKeyMapTests
{
    private static readonly string ConfigDirectory =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "config");

    private const KeyModifiers Insert = KeyModifiers.Insert;

    [Theory]
    [InlineData(KeyboardLayout.Jaws, KeyboardLayout.Desktop)]
    [InlineData(KeyboardLayout.JawsLaptop, KeyboardLayout.Laptop)]
    public void JawsLayout_KeepsEveryCommandOfItsBase_WithAKey(KeyboardLayout jaws, KeyboardLayout baseLayout)
    {
        var base_ = KeyMap.LoadBuiltIn(baseLayout, out _);
        var map = KeyMap.LoadBuiltIn(jaws, out var warnings);
        Assert.Empty(warnings);

        foreach (var mode in new[] { InteractionMode.Browse, InteractionMode.Focus })
        {
            var baseCommands = base_.Bindings.Where(b => b.Key.Mode == mode).Select(b => b.Command).ToHashSet();
            var jawsCommands = map.Bindings.Where(b => b.Key.Mode == mode).Select(b => b.Command).ToHashSet();
            var lost = baseCommands.Except(jawsCommands).ToList();
            Assert.True(lost.Count == 0, $"{jaws} leaves without a key in {mode} mode: {string.Join(", ", lost)}");
        }
    }

    [Theory]
    [InlineData(KeyboardLayout.Jaws)]
    [InlineData(KeyboardLayout.JawsLaptop)]
    public void JawsLayout_ResolvesTheCommonJawsKeys(KeyboardLayout layout)
    {
        var map = KeyMap.LoadBuiltIn(layout, out _);

        void Any(KeyModifiers modifiers, int vk, NavigationCommand expected)
        {
            Assert.True(map.TryResolveOutsideDocument(modifiers, vk, out var command, out _), $"{modifiers}+{vk} unbound in {layout}");
            Assert.Equal(expected, command);
        }
        void Browse(KeyModifiers modifiers, int vk, NavigationCommand expected)
        {
            Assert.True(map.TryResolve(modifiers, vk, InteractionMode.Browse, out var command));
            Assert.Equal(expected, command);
        }

        Any(Insert, 90, NavigationCommand.ToggleMode);             // Insert+Z virtual cursor
        Any(Insert, 118, NavigationCommand.ElementsList);          // Insert+F7 links list
        Any(Insert, 117, NavigationCommand.ElementsList);          // Insert+F6 headings list
        Any(Insert, 116, NavigationCommand.ElementsList);          // Insert+F5 form fields list
        Any(Insert, 84, NavigationCommand.SayTitle);               // Insert+T title
        Any(Insert, 123, NavigationCommand.SayTime);               // Insert+F12 time
        Any(Insert, 9, NavigationCommand.SayFocus);                // Insert+Tab
        Any(Insert, 74, NavigationCommand.OpenVoxMenu);            // Insert+J JAWS window
        Any(Insert, 115, NavigationCommand.Quit);                  // Insert+F4
        Browse(KeyModifiers.Ctrl, 36, NavigationCommand.TopOfDocument);
        Browse(KeyModifiers.Ctrl, 35, NavigationCommand.BottomOfDocument);
        Browse(KeyModifiers.None, 82, NavigationCommand.NextLandmark);   // R region, not radio button
        Browse(KeyModifiers.Shift, 82, NavigationCommand.PrevLandmark);
        Browse(KeyModifiers.None, 186, NavigationCommand.NextLandmark);  // ; landmark
        Browse(KeyModifiers.None, 65, NavigationCommand.NextRadioButton);
        Browse(KeyModifiers.None, 72, NavigationCommand.NextHeading);    // shared quick keys stay

        if (layout == KeyboardLayout.Jaws)
        {
            Any(Insert, 40, NavigationCommand.SayAll);
            Any(Insert, 38, NavigationCommand.ReadCurrentLine);
            Any(KeyModifiers.None, NumpadKeys.Numpad5, NavigationCommand.ReadCurrentChar);
            Any(Insert, NumpadKeys.Numpad5, NavigationCommand.ReadCurrentWord);
            Any(Insert | KeyModifiers.Shift, 79, NavigationCommand.ReportNavigator);
        }
        else
        {
            Any(Insert, 65, NavigationCommand.SayAll);                 // Caps Lock+A
            Any(Insert, 73, NavigationCommand.ReadCurrentLine);        // Caps Lock+I
            Any(Insert, 75, NavigationCommand.ReadCurrentWord);        // Caps Lock+K
            Any(Insert, 188, NavigationCommand.ReadCurrentChar);       // Caps Lock+Comma
            Browse(Insert | KeyModifiers.Shift, 75, NavigationCommand.SayLinkUrl);
        }
    }

    [Fact]
    public void JawsLaptop_UsesNoKeypadKeys()
    {
        var map = KeyMap.LoadBuiltIn(KeyboardLayout.JawsLaptop, out _);

        Assert.DoesNotContain(map.Bindings, b => NumpadKeys.IsKeypadBindingCode(b.Key.VkCode));
    }

    [Fact]
    public void JawsFile_BindsEachKeyOncePerList()
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(ConfigDirectory, KeyMap.JawsFileName)));
        foreach (var list in new[] { "bindings", "laptopBindings" })
        {
            var keys = document.RootElement.GetProperty(list).EnumerateArray()
                .Select(b => (b.GetProperty("modifiers").GetString(), b.GetProperty("vkCode").GetInt32(), b.GetProperty("mode").GetString()))
                .ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Theory]
    [InlineData(KeyboardLayout.Jaws)]
    [InlineData(KeyboardLayout.JawsLaptop)]
    public void LoadLayout_FromFiles_MatchesTheBuiltInLayout_AndLayoutBindings(KeyboardLayout layout)
    {
        var fromFiles = KeyMap.LoadLayout(ConfigDirectory, layout, out var error, out _);
        var fromBindings = KeyMap.FromBindings(KeyMap.LayoutBindings(ConfigDirectory, layout), out _);

        Assert.Null(error);
        Assert.Equal(KeyMap.LoadBuiltIn(layout, out _).Bindings.ToHashSet(), fromFiles.Bindings.ToHashSet());
        Assert.Equal(fromFiles.Bindings.ToHashSet(), fromBindings.Bindings.ToHashSet());
    }

    [Fact]
    public void LoadLayout_MissingJawsFile_FallsBackToTheBuiltInJawsLayout()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VoxJaws_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(ConfigDirectory, KeyMap.DesktopFileName), Path.Combine(directory, KeyMap.DesktopFileName));

            var map = KeyMap.LoadLayout(directory, KeyboardLayout.Jaws, out var error, out _);

            Assert.NotNull(error);
            Assert.True(map.TryResolveOutsideDocument(Insert, 90, out var command, out _));
            Assert.Equal(NavigationCommand.ToggleMode, command);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UserKeys_AreLayeredOverTheJawsLayout()
    {
        var path = Path.Combine(Path.GetTempPath(), "VoxJawsUser_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """
            { "bindings": [ { "modifiers": "Insert", "vkCode": 90, "mode": "Any", "command": "SayTime" } ] }
            """);
        try
        {
            var map = KeyMap.LoadLayout(ConfigDirectory, KeyboardLayout.Jaws, path, out _, out var warnings);

            Assert.True(map.TryResolveOutsideDocument(Insert, 90, out var command, out _));
            Assert.Equal(NavigationCommand.SayTime, command);
            // Toggle mode keeps Vox's own Insert+Space, so nothing is reported as left without a key
            Assert.DoesNotContain(warnings, w => w.Contains(nameof(NavigationCommand.ToggleMode)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(KeyboardLayout.Jaws)]
    [InlineData(KeyboardLayout.JawsLaptop)]
    public void KeysNamedByTheReferenceAndLessons_RunTheirCommand(KeyboardLayout layout)
    {
        // The keyboard reference and the practice lessons name keys through a GestureEditor over the
        // layout's bindings: each key they name for a command must run that command in the layout
        var keys = new GestureEditor(KeyMap.LayoutBindings(ConfigDirectory, layout));
        var map = KeyMap.LoadBuiltIn(layout, out _);

        foreach (var command in TutorialLessons.All.SelectMany(l => l.Steps).Select(s => s.Command).Concat(
            [NavigationCommand.ToggleMode, NavigationCommand.ReportNavigator, NavigationCommand.ReadCurrentWord, NavigationCommand.NextRadioButton]))
        {
            var gesture = TutorialLessons.KeyFor(command, keys);
            Assert.True(gesture is not null, $"{command} has no key in {layout}");
            var mode = gesture.Mode == "Focus" ? InteractionMode.Focus : InteractionMode.Browse;
            Assert.True(map.TryResolve(gesture.Modifiers, gesture.VkCode, mode, out var resolved), $"{gesture.Describe()} is unbound in {layout}");
            Assert.Equal(command, resolved);
        }
    }

    [Fact]
    public void KeyboardReference_JawsPage_HasTheJawsKeys()
    {
        var html = KeyboardReference.JawsHtml(ConfigDirectory);

        Assert.Contains("<th scope=\"col\">JAWS desktop</th><th scope=\"col\">JAWS laptop</th>", html);
        var toggle = html[html.IndexOf("<th scope=\"row\">" + System.Net.WebUtility.HtmlEncode(CommandCatalog.Describe(NavigationCommand.ToggleMode).Name) + "</th>", StringComparison.Ordinal)..];
        toggle = toggle[..toggle.IndexOf("</tr>", StringComparison.Ordinal)];
        Assert.Contains("<kbd>Insert+Z</kbd>", toggle);
        foreach (var info in CommandCatalog.All)
            Assert.Contains($"<th scope=\"row\">{System.Net.WebUtility.HtmlEncode(info.Name)}</th>", html);
        // The standard page still has two layouts
        Assert.DoesNotContain("JAWS desktop", KeyboardReference.Html(ConfigDirectory));
    }

    [Fact]
    public void InputHelp_DescribesJawsKeys_WithTheJawsLayout()
    {
        var (dispatcher, sink, fireKey) = Create(KeyMap.LoadBuiltIn(KeyboardLayout.Jaws, out _));
        void Press(KeyEvent key) => fireKey(key with { Decision = dispatcher.Decide(key) });

        Press(new KeyEvent { VkCode = 49, Modifiers = Insert, IsKeyDown = true });   // Insert+1
        Press(new KeyEvent { VkCode = 90, Modifiers = Insert, IsKeyDown = true });   // Insert+Z
        Press(new KeyEvent { VkCode = 82, IsKeyDown = true });                       // R

        var help = sink.Posted.OfType<InputHelpEvent>().Select(h => h.Command).ToList();
        Assert.Equal([NavigationCommand.ToggleMode, NavigationCommand.NextLandmark], help);
    }

    private sealed class CaptureSink : IEventSink
    {
        public List<ScreenReaderEvent> Posted { get; } = [];
        public void Post(ScreenReaderEvent evt) => Posted.Add(evt);
    }

    private static (KeyInputDispatcher dispatcher, CaptureSink sink, Action<KeyEvent> fireKey) Create(KeyMap keyMap)
    {
        var hook = new Mock<IKeyboardHook>();
        EventHandler<KeyEvent>? handler = null;
        hook.SetupAdd(h => h.KeyPressed += It.IsAny<EventHandler<KeyEvent>>()).Callback<EventHandler<KeyEvent>>(h => handler = h);
        var sink = new CaptureSink();
        var dispatcher = new KeyInputDispatcher(hook.Object, keyMap, sink, NullLogger<KeyInputDispatcher>.Instance);
        dispatcher.SetDocumentActive(true);
        dispatcher.Start();
        return (dispatcher, sink, evt => handler?.Invoke(hook.Object, evt));
    }
}
