namespace Vox.Core.Braille.Hid;

/// <summary>
/// The HID Braille Display usage page (0x41, HID Usage Tables 1.2 and later; the usages NVDA's
/// <c>hidpi.HID_USAGE_BRAILLE_*</c> name) and the stable key names <see cref="HidBrailleDisplay"/>
/// reports in <see cref="BrailleKeyEventArgs.Keys"/>.
/// </summary>
public static class HidBrailleUsages
{
    /// <summary>The Braille Display usage page.</summary>
    public const ushort UsagePage = 0x41;

    /// <summary>The top-level collection of a braille display.</summary>
    public const ushort BrailleDisplay = 0x01;
    /// <summary>A row of cells (the collection around the cell output).</summary>
    public const ushort BrailleRow = 0x02;
    /// <summary>An output array of 8-dot cells, one byte per cell (bit n-1 = dot n).</summary>
    public const ushort EightDotCell = 0x03;
    /// <summary>An output array of 6-dot cells (dots 7 and 8 not shown).</summary>
    public const ushort SixDotCell = 0x04;

    /// <summary>The collection of the first row of routing keys (one above each cell).</summary>
    public const ushort RouterSet1 = 0xFA;
    /// <summary>A second row of routing keys.</summary>
    public const ushort RouterSet2 = 0xFB;
    /// <summary>A third row of routing keys.</summary>
    public const ushort RouterSet3 = 0xFC;
    /// <summary>A routing key; its position within its router set is the cell it is over.</summary>
    public const ushort RouterKey = 0x100;

    public const ushort Dot1 = 0x201;
    public const ushort Dot2 = 0x202;
    public const ushort Dot3 = 0x203;
    public const ushort Dot4 = 0x204;
    public const ushort Dot5 = 0x205;
    public const ushort Dot6 = 0x206;
    public const ushort Dot7 = 0x207;
    public const ushort Dot8 = 0x208;
    public const ushort Space = 0x209;
    public const ushort LeftSpace = 0x20A;
    public const ushort RightSpace = 0x20B;

    /// <summary>Collection of controls on the face of the display.</summary>
    public const ushort FaceControls = 0x20C;
    /// <summary>Collection of controls on the left of the display.</summary>
    public const ushort LeftControls = 0x20D;
    /// <summary>Collection of controls on the right of the display.</summary>
    public const ushort RightControls = 0x20E;
    /// <summary>Collection of controls on the top of the display.</summary>
    public const ushort TopControls = 0x20F;

    public const ushort JoystickCenter = 0x210;
    public const ushort JoystickUp = 0x211;
    public const ushort JoystickDown = 0x212;
    public const ushort JoystickLeft = 0x213;
    public const ushort JoystickRight = 0x214;
    public const ushort DPadCenter = 0x215;
    public const ushort DPadUp = 0x216;
    public const ushort DPadDown = 0x217;
    public const ushort DPadLeft = 0x218;
    public const ushort DPadRight = 0x219;
    public const ushort PanLeft = 0x21A;
    public const ushort PanRight = 0x21B;
    public const ushort RockerUp = 0x21C;
    public const ushort RockerDown = 0x21D;
    public const ushort RockerPress = 0x21E;

    /// <summary>The key name of a routing key in router set 1.</summary>
    public const string RoutingKeyName = "routing";

    private static readonly Dictionary<ushort, string> KeyNames = new()
    {
        [Dot1] = "dot1",
        [Dot2] = "dot2",
        [Dot3] = "dot3",
        [Dot4] = "dot4",
        [Dot5] = "dot5",
        [Dot6] = "dot6",
        [Dot7] = "dot7",
        [Dot8] = "dot8",
        [Space] = "space",
        [LeftSpace] = "leftSpace",
        [RightSpace] = "rightSpace",
        [JoystickCenter] = "joystickCenter",
        [JoystickUp] = "joystickUp",
        [JoystickDown] = "joystickDown",
        [JoystickLeft] = "joystickLeft",
        [JoystickRight] = "joystickRight",
        [DPadCenter] = "dpadCenter",
        [DPadUp] = "dpadUp",
        [DPadDown] = "dpadDown",
        [DPadLeft] = "dpadLeft",
        [DPadRight] = "dpadRight",
        [PanLeft] = "panLeft",
        [PanRight] = "panRight",
        [RockerUp] = "rockerUp",
        [RockerDown] = "rockerDown",
        [RockerPress] = "rockerPress",
    };

    /// <summary>
    /// The key name of a button usage on the braille page ("dot1", "space", "panLeft"); a usage this
    /// table doesn't know gets "braille" and its hex code ("braille0222"). Routing keys are named by
    /// <see cref="RoutingKeyNameFor"/>, since their name depends on the router set.
    /// </summary>
    public static string KeyName(ushort usage) =>
        usage == RouterKey ? RoutingKeyName
        : KeyNames.TryGetValue(usage, out var name) ? name
        : $"braille{usage:X4}";

    /// <summary>True for the usages that are keys with a fixed name (not routing keys or collections).</summary>
    public static bool IsNamedKey(ushort usage) => KeyNames.ContainsKey(usage);

    /// <summary>
    /// The router set (1–3) a collection usage stands for; router keys outside a router set
    /// collection count as set 1.
    /// </summary>
    public static int RouterSetOf(ushort linkUsagePage, ushort linkUsage) =>
        linkUsagePage != UsagePage ? 1 : linkUsage switch
        {
            RouterSet2 => 2,
            RouterSet3 => 3,
            _ => 1,
        };

    /// <summary>The key name of a routing key in router set <paramref name="routerSet"/>: "routing", "routing2", "routing3".</summary>
    public static string RoutingKeyNameFor(int routerSet) =>
        routerSet <= 1 ? RoutingKeyName : $"{RoutingKeyName}{routerSet}";
}
