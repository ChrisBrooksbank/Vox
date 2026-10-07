using System.Globalization;
using Vox.Core.Buffer;

namespace Vox.Core.Navigation;

/// <summary>Battery and power state, for the battery command.</summary>
public readonly record struct PowerInfo(bool HasBattery, int? Percent, bool Charging, bool PluggedIn);

/// <summary>
/// The texts of the "where am I" commands: time and date, battery, status bar, and the
/// foreground window read in order.
/// </summary>
public static class WhereAmI
{
    public static string TimeText(DateTime now, CultureInfo? culture = null) =>
        PlainSpaces(now.ToString("t", culture ?? CultureInfo.CurrentCulture));

    public static string DateText(DateTime now, CultureInfo? culture = null) =>
        PlainSpaces(now.ToString("D", culture ?? CultureInfo.CurrentCulture));

    // Some cultures format with no-break spaces ("3:45\u202FPM"): plain spaces read reliably
    private static string PlainSpaces(string text) => text.Replace('\u202F', ' ').Replace('\u00A0', ' ');

    public static string BatteryText(PowerInfo power)
    {
        if (!power.HasBattery)
            return power.PluggedIn ? "No battery, plugged in" : "No battery";
        var parts = new List<string>(2);
        if (power.Percent is { } percent)
            parts.Add($"{percent} percent");
        parts.Add(power.Charging ? "charging" : power.PluggedIn ? "plugged in" : "on battery");
        return string.Join(", ", parts);
    }

    /// <summary>The text of the window's status bar, or null if it has none.</summary>
    public static string? StatusBarText(IVBufferElement window)
    {
        var statusBar = Find(window, e => e.ControlType == "StatusBar");
        if (statusBar is null)
            return null;
        var texts = new List<string>();
        Collect(statusBar, texts, withRoles: false);
        if (texts.Count == 0 && !string.IsNullOrWhiteSpace(statusBar.Name))
            texts.Add(statusBar.Name.Trim());
        return texts.Count == 0 ? "Status bar is empty" : string.Join(", ", texts);
    }

    /// <summary>The window's contents in reading order: text, and controls with their roles.</summary>
    public static string WindowText(IVBufferElement window, int maxItems = 200)
    {
        var texts = new List<string>();
        if (!string.IsNullOrWhiteSpace(window.Name))
            texts.Add(window.Name.Trim());
        Collect(window, texts, withRoles: true, maxItems);
        return string.Join(". ", texts);
    }

    private static void Collect(IVBufferElement element, List<string> texts, bool withRoles, int maxItems = 200)
    {
        foreach (var child in element.GetChildren())
        {
            if (texts.Count >= maxItems)
                return;
            var name = child.Name.Trim();
            bool isText = child.ControlType == "Text";
            bool isControl = child.IsFocusable || ControlTypeNames.ToSpoken(child.ControlType) is not null;
            if (name.Length > 0 && (isText || isControl))
            {
                var role = withRoles && !isText ? ControlTypeNames.ToSpoken(child.ControlType) : null;
                var item = role is null ? name : $"{name}, {role}";
                if (texts.Count == 0 || texts[^1] != item)
                    texts.Add(item);
                // A named control speaks for its content (a button's caption)
                if (isControl && !isText)
                    continue;
            }
            Collect(child, texts, withRoles, maxItems);
        }
    }

    private static IVBufferElement? Find(IVBufferElement element, Func<IVBufferElement, bool> match)
    {
        foreach (var child in element.GetChildren())
        {
            if (match(child))
                return child;
            if (Find(child, match) is { } found)
                return found;
        }
        return null;
    }
}
