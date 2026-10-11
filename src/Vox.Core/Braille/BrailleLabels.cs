using System.Globalization;
using System.Text;
using System.Text.Json;
using Vox.Core.Buffer;

namespace Vox.Core.Braille;

/// <summary>
/// The short role and state labels braille shows ("lnk", "btn", "h2", "( )"), from a localisable
/// table (<c>assets/braille/labels-en.json</c>, embedded). <see cref="Describe"/> writes an object
/// as braille shows it: name, role, value, states and position ("Remember me chk ( )").
/// </summary>
public sealed class BrailleLabels
{
    private readonly Dictionary<string, string> _roles;
    private readonly Dictionary<string, string> _ariaRoles;
    private readonly Dictionary<string, string> _landmarks;
    private readonly Dictionary<string, string> _states;
    private readonly string _heading;
    private readonly string _position;
    private readonly string _level;

    private BrailleLabels(LabelTable table)
    {
        _roles = new(table.Roles ?? [], StringComparer.OrdinalIgnoreCase);
        _ariaRoles = new(table.AriaRoles ?? [], StringComparer.OrdinalIgnoreCase);
        _landmarks = new(table.Landmarks ?? [], StringComparer.OrdinalIgnoreCase);
        _states = new(table.States ?? [], StringComparer.OrdinalIgnoreCase);
        _heading = table.Heading ?? "h{0}";
        _position = table.Position ?? "{0}/{1}";
        _level = table.Level ?? "lv {0}";
    }

    private static readonly Lazy<BrailleLabels> English = new(() => Load("labels-en.json"));

    /// <summary>The built-in English labels.</summary>
    public static BrailleLabels Default => English.Value;

    /// <summary>Reads a label table from JSON (the format of <c>labels-en.json</c>).</summary>
    public static BrailleLabels FromJson(string json) =>
        new(JsonSerializer.Deserialize<LabelTable>(json, JsonOptions) ?? new LabelTable());

    private static BrailleLabels Load(string fileName)
    {
        using var stream = typeof(BrailleLabels).Assembly.GetManifestResourceStream("Vox.Core.braille." + fileName)
            ?? throw new InvalidOperationException($"Embedded braille labels {fileName} not found");
        using var reader = new StreamReader(stream);
        return FromJson(reader.ReadToEnd());
    }

    /// <summary>The label for a state ("checked", "collapsed"), or the state's name when the table has none.</summary>
    public string State(string state) => _states.TryGetValue(state, out var label) ? label : state;

    /// <summary>
    /// The role label of <paramref name="obj"/>: its heading level ("h2"), its landmark ("nav"),
    /// its author's role description, its ARIA role or control type; null for structural types
    /// (text, unnamed groups) that braille doesn't label.
    /// </summary>
    public string? Role(BrailleObject obj)
    {
        if (obj.HeadingLevel > 0)
            return string.Format(CultureInfo.InvariantCulture, _heading, obj.HeadingLevel);
        if (!string.IsNullOrEmpty(obj.LandmarkType))
            return _landmarks.TryGetValue(obj.LandmarkType, out var landmark) ? landmark : obj.LandmarkType;
        if (!string.IsNullOrWhiteSpace(obj.RoleDescription))
            return obj.RoleDescription.Trim();
        if (!string.IsNullOrEmpty(obj.AriaRole) && _ariaRoles.TryGetValue(obj.AriaRole, out var aria))
            return aria;
        if (obj.IsLink)
            return _roles.GetValueOrDefault("Hyperlink", "lnk");
        if (_roles.TryGetValue(obj.ControlType, out var role))
            return role;
        // Types braille has no label for are structural (Text, Group, Pane, Custom) or rare: as speech says them
        return Navigation.ControlTypeNames.ToSpoken(obj.ControlType);
    }

    /// <summary>
    /// <paramref name="obj"/> as braille shows it: name, role, value (never a password's), then its
    /// states and position in its set, separated by spaces. Empty when there is nothing to show.
    /// </summary>
    public string Describe(BrailleObject obj)
    {
        var text = new StringBuilder();
        void Add(string? part)
        {
            if (string.IsNullOrWhiteSpace(part))
                return;
            if (text.Length > 0)
                text.Append(' ');
            text.Append(part.Trim());
        }

        Add(Flatten(obj.Name));
        Add(Role(obj));
        if (obj.IsPassword)
        {
            if (obj.Value.Length > 0)
                Add(State("password"));
        }
        else if (FormControls.ShowsValue(obj.ControlType, obj.AriaRole) && Flatten(obj.Value) is { Length: > 0 } value && value != Flatten(obj.Name))
        {
            Add(value);
        }
        if (obj.IsLink && obj.IsVisited)
            Add(State("visited"));
        if (obj.IsRequired)
            Add(State("required"));
        if (obj.Invalid.Length > 0 && obj.Invalid != "false")
            Add(State("invalid"));
        if (obj.Current.Length > 0 && obj.Current != "false")
            Add(State("current"));
        if (obj.IsExpandable)
            Add(State(obj.IsExpanded ? "expanded" : "collapsed"));
        if (AriaStates.IsToggleButton(obj.ControlType, obj.AriaRole, obj.ToggleState))
            Add(State(obj.ToggleState == ControlState.ToggleOn ? "pressed" : "notPressed"));
        else if (obj.ToggleState is { } toggle)
            Add(State(toggle switch
            {
                ControlState.ToggleOn => "checked",
                ControlState.ToggleIndeterminate => "halfChecked",
                _ => "notChecked",
            }));
        else if (obj.IsSelected is { } selected)
        {
            if (obj.ControlType == "RadioButton")
                Add(State(selected ? "checked" : "notChecked"));
            else if (selected)
                Add(State("selected"));
        }
        if (obj.IsReadOnly && FormControls.ShowsValue(obj.ControlType, obj.AriaRole) && obj.ControlType is "Edit" or "Document")
            Add(State("readOnly"));
        if (obj.Level > 0 && obj.HeadingLevel == 0)
            Add(string.Format(CultureInfo.InvariantCulture, _level, obj.Level));
        if (obj.PositionInSet > 0 && obj.SizeOfSet > 0)
            Add(string.Format(CultureInfo.InvariantCulture, _position, obj.PositionInSet, obj.SizeOfSet));
        return text.ToString();
    }

    /// <summary>One line: line breaks and runs of white space become single spaces.</summary>
    private static string Flatten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var flat = new StringBuilder(text.Length);
        bool space = false;
        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }
            if (space)
                flat.Append(' ');
            space = false;
            flat.Append(c);
        }
        return flat.ToString();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class LabelTable
    {
        public Dictionary<string, string>? Roles { get; set; }
        public Dictionary<string, string>? AriaRoles { get; set; }
        public Dictionary<string, string>? Landmarks { get; set; }
        public Dictionary<string, string>? States { get; set; }
        public string? Heading { get; set; }
        public string? Position { get; set; }
        public string? Level { get; set; }
    }
}
