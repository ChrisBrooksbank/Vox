using Vox.Core.Pipeline;
using Vox.Sdk;

namespace Vox.Core.Input;

/// <summary>An add-on's command as the key dispatcher and input help see it.</summary>
public sealed record AddOnCommandRef(string AddOnId, string CommandId, string Name, string Description);

/// <summary>
/// The keys of add-on commands (the active add-on app module's, then every add-on's global ones),
/// looked up by <see cref="KeyInputDispatcher"/> before Vox's own keys. Modes work as in the
/// keymap: "Any" keys apply everywhere, Browse and Focus keys only on web pages in that mode.
/// Immutable: a new layer replaces the old one whole.
/// </summary>
public sealed class AddOnKeyLayer
{
    private readonly Dictionary<(KeyModifiers, int, InteractionMode), AddOnCommandRef> _bindings = new();
    private readonly Dictionary<(KeyModifiers, int), AddOnCommandRef> _anyBindings = new();
    private readonly List<(KeyGesture Gesture, AddOnCommandRef Command)> _all = [];

    /// <summary>A layer of <paramref name="gestures"/>; when two commands want one key, the first listed keeps it.</summary>
    public static AddOnKeyLayer Build(IEnumerable<(KeyGesture Gesture, AddOnCommandRef Command)> gestures)
    {
        var layer = new AddOnKeyLayer();
        foreach (var (gesture, command) in gestures)
        {
            // The modifier flags have the same values in the SDK and in Vox
            var modifiers = (KeyModifiers)(int)gesture.Modifiers;
            bool added = false;
            if (gesture.Mode == GestureMode.Any)
            {
                if (layer._anyBindings.TryAdd((modifiers, gesture.VirtualKey), command))
                {
                    layer._bindings.TryAdd((modifiers, gesture.VirtualKey, InteractionMode.Browse), command);
                    layer._bindings.TryAdd((modifiers, gesture.VirtualKey, InteractionMode.Focus), command);
                    added = true;
                }
            }
            else
            {
                var mode = gesture.Mode == GestureMode.Browse ? InteractionMode.Browse : InteractionMode.Focus;
                added = layer._bindings.TryAdd((modifiers, gesture.VirtualKey, mode), command);
            }
            if (added)
                layer._all.Add((gesture, command));
        }
        return layer;
    }

    public int Count => _all.Count;

    /// <summary>Every key that is in the layer, with its command.</summary>
    public IReadOnlyList<(KeyGesture Gesture, AddOnCommandRef Command)> Bindings => _all;

    /// <summary>The command on a key on a web page in <paramref name="mode"/>.</summary>
    public bool TryResolve(KeyModifiers modifiers, int vkCode, InteractionMode mode, out AddOnCommandRef command) =>
        _bindings.TryGetValue((modifiers, vkCode, mode), out command!);

    /// <summary>The command on a key outside web pages, where only "Any" keys apply.</summary>
    public bool TryResolveOutsideDocument(KeyModifiers modifiers, int vkCode, out AddOnCommandRef command) =>
        _anyBindings.TryGetValue((modifiers, vkCode), out command!);
}
