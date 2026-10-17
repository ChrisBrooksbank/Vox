namespace Vox.Core.Input;

/// <summary>A key as it is bound: the keys, and where it applies ("Browse", "Focus" or "Any").</summary>
public sealed record Gesture(KeyModifiers Modifiers, int VkCode, string Mode, bool IsKeypad = false)
{
    /// <summary>The key's name, and the mode it works in unless it works everywhere ("Shift+H (browse mode)").</summary>
    public string Describe(string? screenReaderKey = null)
    {
        var key = InputHelp.KeyName(Modifiers, VkCode, IsKeypad || VkCode is >= 0x60 and <= 0x6F or 269, screenReaderKey);
        if (Mode.Equals("Any", StringComparison.OrdinalIgnoreCase))
            return key;
        if (Mode.Equals("Browse", StringComparison.OrdinalIgnoreCase))
            return Localization.VoxStrings.Format("Gesture.BrowseMode", key);
        if (Mode.Equals("Focus", StringComparison.OrdinalIgnoreCase))
            return Localization.VoxStrings.Format("Gesture.FocusMode", key);
        return Localization.VoxStrings.Format("Gesture.OtherMode", key, Mode.ToLowerInvariant());
    }

    /// <summary>
    /// The gesture for a key pressed while the dialog waits for one, bound the way the keymap
    /// resolves it: a keypad key with Num Lock off as its keypad code (Numpad 8, not the Up arrow
    /// it sends; Numpad Enter as <see cref="NumpadKeys.NumpadEnter"/>). Null for a Num Lock digit,
    /// which types and is never bound.
    /// </summary>
    public static Gesture? FromKey(KeyModifiers modifiers, int vkCode, bool isKeypad, string mode)
    {
        var (code, _) = NumpadKeys.BindingCodes(vkCode, isKeypad);
        return code < 0 ? null : new Gesture(modifiers, code, mode, isKeypad);
    }
}

/// <summary>
/// The model behind the input gestures dialog: the layout's bindings with the user's on top,
/// changed by adding and removing keys, and saved back as just the user's differences from the
/// layout (the user keymap, <see cref="KeyMap.UserFileName"/>).
/// </summary>
/// <remarks>
/// Each key has three slots, as <see cref="KeyMap"/> resolves it: browse mode, focus mode, and
/// outside web pages (only an "Any" binding fills that one).
/// </remarks>
public sealed class GestureEditor
{
    private readonly record struct Slot(string Command, bool PassThrough);

    private sealed class Slots
    {
        public Slot? Browse;
        public Slot? Focus;
        public Slot? Outside;

        public Slots Copy() => new() { Browse = Browse, Focus = Focus, Outside = Outside };

        public bool SameAs(Slots other) => Browse == other.Browse && Focus == other.Focus && Outside == other.Outside;
    }

    private readonly Dictionary<(KeyModifiers, int), Slots> _layout;
    private readonly Dictionary<(KeyModifiers, int), Slots> _keys;

    public GestureEditor(IEnumerable<KeyBinding> layout, IEnumerable<KeyBinding>? user = null)
    {
        _layout = Layer(layout);
        _keys = Layer(layout.Concat(user ?? []));
    }

    /// <summary>Whether anything differs from when the editor was made.</summary>
    public bool IsChanged { get; private set; }

    /// <summary>The keys bound to <paramref name="command"/>.</summary>
    public IReadOnlyList<Gesture> GesturesFor(NavigationCommand command) => GesturesFor(command.ToString());

    /// <summary>The keys bound to a command by its binding name (a <see cref="NavigationCommand"/> name, or an add-on command's <see cref="AddOnCommandRef.BindingName"/>).</summary>
    public IReadOnlyList<Gesture> GesturesFor(string name)
    {
        var gestures = new List<Gesture>();
        foreach (var ((modifiers, vk), slots) in _keys.OrderBy(k => k.Key.Item2).ThenBy(k => k.Key.Item1))
        {
            bool browse = slots.Browse?.Command == name, focus = slots.Focus?.Command == name, outside = slots.Outside?.Command == name;
            if (browse && focus && outside)
                gestures.Add(new Gesture(modifiers, vk, "Any"));
            else
            {
                if (browse) gestures.Add(new Gesture(modifiers, vk, "Browse"));
                if (focus) gestures.Add(new Gesture(modifiers, vk, "Focus"));
            }
        }
        return gestures;
    }

    /// <summary>The commands <paramref name="gesture"/> would take its keys from (other than <paramref name="command"/>).</summary>
    public IReadOnlyList<NavigationCommand> ConflictsWith(Gesture gesture, NavigationCommand command) =>
        ConflictNames(gesture, command.ToString())
            .Select(n => Enum.TryParse<NavigationCommand>(n, out var c) ? c : (NavigationCommand?)null)
            .OfType<NavigationCommand>()
            .ToList();

    /// <summary>The binding names of the commands (built-in or add-on) <paramref name="gesture"/> would take its keys from.</summary>
    public IReadOnlyList<string> ConflictNames(Gesture gesture, string command)
    {
        if (!_keys.TryGetValue((gesture.Modifiers, gesture.VkCode), out var slots))
            return [];
        var taken = new List<Slot?>();
        bool any = IsAny(gesture.Mode);
        if (any || IsMode(gesture.Mode, "Browse")) taken.Add(slots.Browse);
        if (any || IsMode(gesture.Mode, "Focus")) taken.Add(slots.Focus);
        if (any) taken.Add(slots.Outside);
        return taken.Where(s => s is not null && s.Value.Command != command)
            .Select(s => s!.Value.Command)
            .Distinct()
            .ToList();
    }

    /// <summary>Binds <paramref name="gesture"/> to <paramref name="command"/>, replacing whatever it ran there.</summary>
    public void Add(NavigationCommand command, Gesture gesture) => Add(command.ToString(), gesture);

    /// <summary>Binds <paramref name="gesture"/> to a command by its binding name.</summary>
    public void Add(string command, Gesture gesture)
    {
        var key = (gesture.Modifiers, gesture.VkCode);
        if (!_keys.TryGetValue(key, out var slots))
            _keys[key] = slots = new Slots();
        Apply(slots, gesture.Mode, new Slot(command, false));
        IsChanged = true;
    }

    /// <summary>Takes <paramref name="gesture"/> away from <paramref name="command"/>.</summary>
    public void Remove(NavigationCommand command, Gesture gesture) => Remove(command.ToString(), gesture);

    /// <summary>Takes <paramref name="gesture"/> away from a command by its binding name.</summary>
    public void Remove(string name, Gesture gesture)
    {
        if (!_keys.TryGetValue((gesture.Modifiers, gesture.VkCode), out var slots))
            return;
        bool any = IsAny(gesture.Mode);
        if ((any || IsMode(gesture.Mode, "Browse")) && slots.Browse?.Command == name) slots.Browse = null;
        if ((any || IsMode(gesture.Mode, "Focus")) && slots.Focus?.Command == name) slots.Focus = null;
        // A key left without its browse or focus binding no longer has its "Any" one outside pages
        if (slots.Outside?.Command == name)
            slots.Outside = null;
        IsChanged = true;
    }

    /// <summary>Puts every key back as the layout has it.</summary>
    public void ResetAll()
    {
        _keys.Clear();
        foreach (var (key, slots) in _layout)
            _keys[key] = slots.Copy();
        IsChanged = true;
    }

    /// <summary>
    /// The user keymap that gives these keys over the layout: one binding per key that differs
    /// ("Any" where all three slots agree, "None" where the key is now free).
    /// </summary>
    public IReadOnlyList<KeyBinding> UserBindings()
    {
        var bindings = new List<KeyBinding>();
        foreach (var key in _keys.Keys.Union(_layout.Keys).OrderBy(k => k.Item2).ThenBy(k => k.Item1))
        {
            var now = _keys.GetValueOrDefault(key) ?? new Slots();
            var was = _layout.GetValueOrDefault(key) ?? new Slots();
            if (now.SameAs(was))
                continue;
            var (modifiers, vk) = key;
            if (now.Browse is { } all && now.Focus == all && now.Outside == all)
                bindings.Add(new KeyBinding(modifiers, vk, "Any", all.Command, all.PassThrough));
            else if (now.Browse is null && now.Focus is null)
                bindings.Add(new KeyBinding(modifiers, vk, "Any", KeyMap.UnboundCommand));
            else
            {
                // Only the modes that changed (any mode binding also ends an "Any" one outside pages)
                int before = bindings.Count;
                if (now.Browse != was.Browse)
                    bindings.Add(Binding(modifiers, vk, "Browse", now.Browse));
                if (now.Focus != was.Focus)
                    bindings.Add(Binding(modifiers, vk, "Focus", now.Focus));
                if (bindings.Count == before)
                    bindings.Add(Binding(modifiers, vk, "Browse", now.Browse));
            }
        }
        return bindings;
    }

    private static KeyBinding Binding(KeyModifiers modifiers, int vk, string mode, Slot? slot) =>
        new(modifiers, vk, mode, slot?.Command ?? KeyMap.UnboundCommand, slot?.PassThrough ?? false);

    /// <summary>Bindings applied in order, as <see cref="KeyMap"/> builds them.</summary>
    private static Dictionary<(KeyModifiers, int), Slots> Layer(IEnumerable<KeyBinding> bindings)
    {
        var keys = new Dictionary<(KeyModifiers, int), Slots>();
        foreach (var binding in bindings)
        {
            var key = (binding.Modifiers, binding.VkCode);
            if (!keys.TryGetValue(key, out var slots))
                keys[key] = slots = new Slots();
            bool unbind = string.Equals(binding.Command, KeyMap.UnboundCommand, StringComparison.OrdinalIgnoreCase);
            Apply(slots, binding.Mode, unbind ? null : new Slot(binding.Command, binding.PassThrough));
        }
        return keys;
    }

    private static void Apply(Slots slots, string mode, Slot? slot)
    {
        if (IsAny(mode))
        {
            slots.Browse = slots.Focus = slots.Outside = slot;
            return;
        }
        if (IsMode(mode, "Browse"))
            slots.Browse = slot;
        else if (IsMode(mode, "Focus"))
            slots.Focus = slot;
        else
            return;
        // A mode binding (or unbinding) leaves the key with no "Any" binding outside pages
        slots.Outside = null;
    }

    private static bool IsAny(string mode) => IsMode(mode, "Any");

    private static bool IsMode(string mode, string name) => string.Equals(mode.Trim(), name, StringComparison.OrdinalIgnoreCase);
}
