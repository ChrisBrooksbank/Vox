using Vox.Core.Localization;

namespace Vox.Core.Navigation;

/// <summary>What the developer info command reports about the focused element.</summary>
public sealed record DeveloperInfo(
    string Name,
    string ControlType,
    string? AriaRole,
    string? FrameworkId,
    string? ClassName,
    string? AutomationId,
    int ProcessId,
    string? ProcessName,
    int[] RuntimeId)
{
    /// <summary>One line per field, for speech and the clipboard (bug reports).</summary>
    public string Format()
    {
        var lines = new List<string>
        {
            VoxStrings.Format("DevInfo.Name", Show(Name)),
            VoxStrings.Format("DevInfo.ControlType", Show(ControlType)),
            VoxStrings.Format("DevInfo.AriaRole", Show(AriaRole)),
            VoxStrings.Format("DevInfo.Framework", Show(FrameworkId)),
            VoxStrings.Format("DevInfo.Class", Show(ClassName)),
            VoxStrings.Format("DevInfo.AutomationId", Show(AutomationId)),
            VoxStrings.Format("DevInfo.Process", Show(ProcessName), ProcessId),
            VoxStrings.Format("DevInfo.RuntimeId", RuntimeId.Length == 0 ? VoxStrings.Get("DevInfo.None") : string.Join(".", RuntimeId)),
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? VoxStrings.Get("DevInfo.None") : value;
}
