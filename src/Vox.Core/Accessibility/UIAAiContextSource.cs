using Vox.Core.Ai;

namespace Vox.Core.Accessibility;

/// <summary><see cref="IAiContextSource"/> through UIA (on the UIA thread) and the foreground window.</summary>
public sealed class UIAAiContextSource(UIAThread uiaThread, UIAProvider provider, ObjectNavigationCommands navigation,
    IReviewTextSource reviewText, IForegroundWindow foregroundWindow) : IAiContextSource
{
    public async Task<AiTarget> NavigatorTargetAsync()
    {
        bool password = await uiaThread.RunAsync(() => navigation.CurrentObject()?.Describe().IsPassword ?? false)
            .ConfigureAwait(false);
        return new AiTarget(password, ForegroundProcess());
    }

    public async Task<AiTarget> WindowTargetAsync()
    {
        bool password = await uiaThread.RunAsync(() =>
        {
            try { return provider.Automation.GetFocusedElement()?.CurrentIsPassword != 0; }
            catch (System.Runtime.InteropServices.COMException) { return false; }
        }).ConfigureAwait(false);
        return new AiTarget(password, ForegroundProcess());
    }

    public async Task<string?> WindowTextAsync()
    {
        var window = foregroundWindow.Get();
        if (window is not { } info)
            return null;
        var text = await uiaThread.RunAsync(() => reviewText.GetWindowText(info.Handle)?.DocumentRange.GetText(),
            UIAThread.DocumentTimeout).ConfigureAwait(false); // captures the whole window
        var title = info.Title.Trim();
        return title.Length == 0 ? text : $"{title}\n{text}";
    }

    private string? ForegroundProcess()
    {
        if (foregroundWindow.Get() is not { } info)
            return null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(info.ProcessId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
