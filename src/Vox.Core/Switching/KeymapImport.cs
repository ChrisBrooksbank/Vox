using Vox.Core.Input;

namespace Vox.Core.Switching;

/// <summary>
/// A key another screen reader binds to something Vox has: where it came from (for the report),
/// the Vox command, the keys, and the mode to bind it in when Vox's layout has no key for the
/// command ("Browse" for a browse-mode script, "Any" for a global one).
/// </summary>
public sealed record ImportedKey(string Source, NavigationCommand Command, KeyModifiers Modifiers, int VkCode, string DefaultMode);

/// <summary>
/// Adds imported keys to the user keymap (<see cref="KeyMap.UserFileName"/>), shared by the NVDA
/// and JAWS importers. A key is bound in the modes Vox's layout binds its command in (so a command
/// that works everywhere gets an "Any" key), else its default mode. It is added beside the
/// command's own keys; a key that did something else in Vox is taken over, and the report says what
/// it did before. A key already bound to the same command is reported unchanged.
/// </summary>
public static class KeymapImport
{
    /// <summary>The user bindings with <paramref name="keys"/> added, or the same list when nothing changed.</summary>
    public static IReadOnlyList<KeyBinding> Merge(IReadOnlyList<KeyBinding> layout, IReadOnlyList<KeyBinding> user,
        IEnumerable<ImportedKey> keys, ImportReport report)
    {
        var result = user.ToList();
        foreach (var key in keys)
        {
            foreach (var mode in ModesFor(layout, key))
            {
                string? previous = CommandOn(layout, result, key.Modifiers, key.VkCode, mode);
                string command = key.Command.ToString();
                report.AddImported(key.Source, $"{KeyMap.UserFileName}: {KeyText(key.Modifiers, key.VkCode)} ({mode})", command, previous);
                if (string.Equals(previous, command, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.RemoveAll(b => SameKey(b, key.Modifiers, key.VkCode, mode));
                result.Add(new KeyBinding(key.Modifiers, key.VkCode, mode, command));
            }
        }
        return result;
    }

    /// <summary>
    /// The user keymap in <paramref name="voxUserFolder"/> with <paramref name="keys"/> added, as a
    /// planned file, or null when nothing would change.
    /// </summary>
    public static PlannedFile? PlanFile(string voxUserFolder, IReadOnlyList<KeyBinding> layout, IEnumerable<ImportedKey> keys, ImportReport report)
    {
        string path = Path.Combine(voxUserFolder, KeyMap.UserFileName);
        var user = KeyMap.ReadUserBindings(path);
        var merged = Merge(layout, user, keys, report);
        return merged.SequenceEqual(user) ? null : new PlannedFile(path, KeyMap.ToJson(merged));
    }

    /// <summary>The modes the layout binds the command in ("Any" alone when it has an "Any" key), else the key's default.</summary>
    private static IEnumerable<string> ModesFor(IReadOnlyList<KeyBinding> layout, ImportedKey key)
    {
        string command = key.Command.ToString();
        var modes = layout.Where(b => string.Equals(b.Command, command, StringComparison.OrdinalIgnoreCase))
            .Select(b => Normalize(b.Mode)).Distinct().ToList();
        if (modes.Count == 0)
            return [key.DefaultMode];
        return modes.Contains("Any") ? ["Any"] : modes;
    }

    // The command the key runs now in that mode: the user's binding, else the layout's
    private static string? CommandOn(IReadOnlyList<KeyBinding> layout, IReadOnlyList<KeyBinding> user, KeyModifiers modifiers, int vkCode, string mode)
    {
        var binding = user.LastOrDefault(b => SameKey(b, modifiers, vkCode, mode))
            ?? layout.LastOrDefault(b => SameKey(b, modifiers, vkCode, mode));
        return binding is null || binding.Command.Equals(KeyMap.UnboundCommand, StringComparison.OrdinalIgnoreCase) ? null : binding.Command;
    }

    private static bool SameKey(KeyBinding binding, KeyModifiers modifiers, int vkCode, string mode) =>
        binding.Modifiers == modifiers && binding.VkCode == vkCode && Normalize(binding.Mode) == mode;

    private static string Normalize(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "browse" => "Browse",
        "focus" => "Focus",
        _ => "Any",
    };

    /// <summary>A key as the report shows it, the same in every language ("Insert+Ctrl+key 70").</summary>
    public static string KeyText(KeyModifiers modifiers, int vkCode)
    {
        var parts = new List<string>();
        if ((modifiers & KeyModifiers.Insert) != 0) parts.Add("Insert");
        if ((modifiers & KeyModifiers.Ctrl) != 0) parts.Add("Ctrl");
        if ((modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        parts.Add(vkCode switch
        {
            >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vkCode).ToString(),
            >= 0x70 and <= 0x87 => $"F{vkCode - 0x6F}",
            >= 0x60 and <= 0x69 => $"Numpad{vkCode - 0x60}",
            _ => $"key {vkCode}",
        });
        return string.Join("+", parts);
    }
}
