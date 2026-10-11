using System.Text.Json;
using Vox.Core.Input;

namespace Vox.Core.Braille;

/// <summary>What a braille display key does, besides running a <see cref="NavigationCommand"/> or sending a key.</summary>
public enum BrailleAction
{
    None,
    PanLeft,
    PanRight,
    /// <summary>A routing key: move the cursor or caret to the cell; pressed again there, activate.</summary>
    Route,
    /// <summary>The line before the one shown (in the document, the edit control or the review).</summary>
    PreviousLine,
    NextLine,
    /// <summary>Switch what braille follows: automatic, focus, review.</summary>
    CycleTether,
    /// <summary>Type a space (and translate the word typed in contracted braille).</summary>
    Space,
    /// <summary>Remove the last cell typed, or send Backspace.</summary>
    Backspace,
    /// <summary>Translate what was typed and send Enter.</summary>
    Enter,
}

/// <summary>A chord on a braille display and what it does: one of an action, a command or a key.</summary>
public sealed record BrailleBinding(IReadOnlySet<string> Keys, BrailleAction Action = BrailleAction.None,
    NavigationCommand? Command = null, OutputKey? Key = null);

/// <summary>What a chord resolved to.</summary>
public abstract record BrailleKeyResult
{
    /// <summary>A bound chord.</summary>
    public sealed record Bound(BrailleBinding Binding, int? RoutingCell) : BrailleKeyResult;
    /// <summary>Dots typed on the braille keyboard (no binding): a cell to type.</summary>
    public sealed record Typed(byte Cell) : BrailleKeyResult;
    /// <summary>A character typed on the display's own keyboard (BRLTTY "char_a"): typed as it is.</summary>
    public sealed record Character(string Text) : BrailleKeyResult;
    /// <summary>Nothing.</summary>
    public sealed record Unbound : BrailleKeyResult;
}

/// <summary>
/// The keys of a braille display (<c>assets/braille/keymaps/common.json</c>, overlaid by the
/// driver's own file, <c>hid.json</c>, <c>brltty.json</c>): chords of the driver's key names to
/// braille actions, commands or keys sent to the application. A chord of dot keys alone that
/// isn't bound is braille input: its dots are the cell typed.
/// </summary>
public sealed class BrailleKeyMap
{
    private readonly Dictionary<string, BrailleBinding> _bindings;

    public BrailleKeyMap(IEnumerable<BrailleBinding> bindings)
    {
        _bindings = new(StringComparer.Ordinal);
        foreach (var binding in bindings)
            _bindings[ChordKey(binding.Keys)] = binding;
    }

    public IReadOnlyCollection<BrailleBinding> Bindings => _bindings.Values;

    /// <summary>The common bindings overlaid by the display driver's (<paramref name="driverId"/>, case-insensitive).</summary>
    public static BrailleKeyMap ForDriver(string? driverId)
    {
        var bindings = new List<BrailleBinding>(ReadBuiltIn("common"));
        if (!string.IsNullOrEmpty(driverId))
            bindings.AddRange(ReadBuiltIn(driverId.ToLowerInvariant()));
        return new BrailleKeyMap(bindings);
    }

    /// <summary>What the chord in <paramref name="e"/> does.</summary>
    public BrailleKeyResult Resolve(BrailleKeyEventArgs e)
    {
        var keys = Normalize(e.Keys);
        if (keys.Count == 0)
            return new BrailleKeyResult.Unbound();
        if (_bindings.TryGetValue(ChordKey(keys), out var binding))
            return new BrailleKeyResult.Bound(binding, e.RoutingCell);
        if (DotsOf(keys) is { } cell)
            return new BrailleKeyResult.Typed(cell);
        if (keys.Count == 1 && keys.First() is { Length: > 5 } single && single.StartsWith("char_", StringComparison.Ordinal))
            return new BrailleKeyResult.Character(single[5..]);
        return new BrailleKeyResult.Unbound();
    }

    /// <summary>
    /// The chord's keys as the key maps name them: BRLTTY reports typed dots as "dots" plus
    /// "dots125", which become "dot1", "dot2", "dot5", so the common chords work on it too.
    /// </summary>
    public static HashSet<string> Normalize(IEnumerable<string> keys)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (key == "dots")
                continue;
            if (key.Length > 4 && key.StartsWith("dots", StringComparison.Ordinal) && key[4..].All(c => c is >= '1' and <= '8'))
            {
                foreach (var dot in key[4..])
                    normalized.Add("dot" + dot);
                continue;
            }
            normalized.Add(key);
        }
        return normalized;
    }

    /// <summary>The cell of a chord made only of "dot1"–"dot8" keys, or null.</summary>
    public static byte? DotsOf(IReadOnlyCollection<string> keys)
    {
        byte cell = 0;
        foreach (var key in keys)
        {
            if (key.Length != 4 || !key.StartsWith("dot", StringComparison.Ordinal) || key[3] is < '1' or > '8')
                return null;
            cell |= (byte)(1 << (key[3] - '1'));
        }
        return cell == 0 ? null : cell;
    }

    /// <summary>
    /// Reads bindings from JSON: <c>{"bindings": [{"keys": ["space", "dot1"], "command": "PrevLine"},
    /// {"keys": ["panLeft"], "action": "PanLeft"}, {"keys": ["dot8"], "key": "Enter"}]}</c>.
    /// Entries with unknown names are skipped and reported in <paramref name="warnings"/>.
    /// </summary>
    public static IReadOnlyList<BrailleBinding> Parse(string json, List<string>? warnings = null)
    {
        var bindings = new List<BrailleBinding>();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!document.RootElement.TryGetProperty("bindings", out var array) || array.ValueKind != JsonValueKind.Array)
            return bindings;
        foreach (var entry in array.EnumerateArray())
        {
            if (!entry.TryGetProperty("keys", out var keysElement) || keysElement.ValueKind != JsonValueKind.Array)
                continue;
            var keys = keysElement.EnumerateArray().Select(k => k.GetString() ?? string.Empty)
                .Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
            if (keys.Count == 0)
                continue;
            var name = string.Join("+", keys);
            if (entry.TryGetProperty("action", out var action))
            {
                if (Enum.TryParse<BrailleAction>(action.GetString(), ignoreCase: true, out var value))
                    bindings.Add(new BrailleBinding(keys, Action: value));
                else
                    warnings?.Add($"{name}: unknown action '{action.GetString()}'");
            }
            else if (entry.TryGetProperty("command", out var command))
            {
                if (Enum.TryParse<NavigationCommand>(command.GetString(), ignoreCase: true, out var value))
                    bindings.Add(new BrailleBinding(keys, Command: value));
                else
                    warnings?.Add($"{name}: unknown command '{command.GetString()}'");
            }
            else if (entry.TryGetProperty("key", out var key))
            {
                if (OutputKey.Parse(key.GetString() ?? string.Empty) is { } output)
                    bindings.Add(new BrailleBinding(keys, Key: output));
                else
                    warnings?.Add($"{name}: unknown key '{key.GetString()}'");
            }
        }
        return bindings;
    }

    private static IReadOnlyList<BrailleBinding> ReadBuiltIn(string name)
    {
        using var stream = typeof(BrailleKeyMap).Assembly.GetManifestResourceStream($"Vox.Core.braille.keymaps.{name}.json");
        if (stream is null)
            return [];
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static string ChordKey(IEnumerable<string> keys) => string.Join("+", keys.Order(StringComparer.Ordinal));
}
