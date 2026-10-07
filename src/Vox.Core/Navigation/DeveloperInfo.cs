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
            $"Name: {Show(Name)}",
            $"Control type: {Show(ControlType)}",
            $"ARIA role: {Show(AriaRole)}",
            $"Framework: {Show(FrameworkId)}",
            $"Class: {Show(ClassName)}",
            $"Automation id: {Show(AutomationId)}",
            $"Process: {Show(ProcessName)} ({ProcessId})",
            $"Runtime id: {(RuntimeId.Length == 0 ? "none" : string.Join(".", RuntimeId))}",
        };
        return string.Join(Environment.NewLine, lines);
    }

    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value;
}
