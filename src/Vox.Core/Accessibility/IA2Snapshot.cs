using System.Text.Json;

namespace Vox.Core.Accessibility;

/// <summary>
/// A COM-free copy of one node of a Firefox document's IAccessible2 tree, captured by
/// <c>Vox.NativeHelper</c> (which hands it over as JSON) so no IA2 object crosses into managed code.
/// </summary>
public sealed record IA2Node
{
    /// <summary>MSAA/IA2 role (an <c>IA2_ROLE_*</c> value, else the MSAA <c>ROLE_SYSTEM_*</c> value).</summary>
    public int Role { get; init; }

    /// <summary>IA2 state bits (<c>IA2_STATE_*</c>) in the high word of a 64-bit value; MSAA states in the low.</summary>
    public long States { get; init; }

    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>The object attributes (<c>IAccessible2::attributes</c>): <c>xml-roles</c>, <c>level</c>, <c>live</c>, ARIA states...</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = EmptyAttributes;

    /// <summary>Relations by type (<c>describedby</c>, <c>errorMessage</c>, <c>details</c>, <c>controllerFor</c>) to the unique ids of their targets.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> Relations { get; init; } = EmptyRelations;

    /// <summary>The text attribute runs of this node's own text (<c>IAccessibleText::attributes</c>).</summary>
    public IReadOnlyList<IA2TextRun> TextRuns { get; init; } = [];

    /// <summary>IA2 unique id (<c>IAccessible2::uniqueID</c>); stable for the node's life, used as its runtime id.</summary>
    public int UniqueId { get; init; }

    public IReadOnlyList<IA2Node> Children { get; init; } = [];

    internal static readonly IReadOnlyDictionary<string, string> EmptyAttributes = new Dictionary<string, string>();
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<int>> EmptyRelations = new Dictionary<string, IReadOnlyList<int>>();

    public string? Attribute(string name) => Attributes.TryGetValue(name, out var v) ? v : null;
}

/// <summary>A run of text sharing text attributes (<c>language</c>, <c>invalid</c>, font...).</summary>
public sealed record IA2TextRun(int Start, int End, IReadOnlyDictionary<string, string> Attributes);

/// <summary>Reads the JSON that <c>Vox.NativeHelper</c> produces into an <see cref="IA2Node"/> tree.</summary>
public static class IA2SnapshotReader
{
    /// <summary>Deepest tree accepted; a deeper one is cut off rather than overflowing the stack.</summary>
    public const int MaxDepth = 256;

    /// <summary>Parses a snapshot; returns null when the text isn't a snapshot.</summary>
    public static IA2Node? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth * 2 });
            return doc.RootElement.ValueKind == JsonValueKind.Object ? ReadNode(doc.RootElement, 0) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IA2Node ReadNode(JsonElement e, int depth)
    {
        var children = new List<IA2Node>();
        if (depth < MaxDepth && e.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array)
            foreach (var k in kids.EnumerateArray())
                if (k.ValueKind == JsonValueKind.Object)
                    children.Add(ReadNode(k, depth + 1));

        var runs = new List<IA2TextRun>();
        if (e.TryGetProperty("textRuns", out var r) && r.ValueKind == JsonValueKind.Array)
            foreach (var run in r.EnumerateArray())
                if (run.ValueKind == JsonValueKind.Object)
                    runs.Add(new IA2TextRun(Int(run, "start"), Int(run, "end"), Strings(run, "attributes")));

        var relations = new Dictionary<string, IReadOnlyList<int>>();
        if (e.TryGetProperty("relations", out var rel) && rel.ValueKind == JsonValueKind.Object)
            foreach (var p in rel.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array)
                    relations[p.Name] = p.Value.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _))
                        .Select(x => x.GetInt32()).ToList();

        return new IA2Node
        {
            Role = Int(e, "role"),
            States = e.TryGetProperty("states", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var st) ? st : 0,
            Name = Str(e, "name"),
            Value = Str(e, "value"),
            Description = Str(e, "description"),
            UniqueId = Int(e, "id"),
            Attributes = Strings(e, "attributes"),
            Relations = relations,
            TextRuns = runs,
            Children = children,
        };
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static Dictionary<string, string> Strings(JsonElement e, string name)
    {
        var d = new Dictionary<string, string>();
        if (e.TryGetProperty(name, out var o) && o.ValueKind == JsonValueKind.Object)
            foreach (var p in o.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    d[p.Name] = p.Value.GetString() ?? "";
        return d;
    }
}
