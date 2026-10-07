using Microsoft.Extensions.Logging;

namespace Vox.Core.Accessibility;

/// <summary>Puts text on the clipboard (any thread).</summary>
public interface IClipboard
{
    /// <summary>Copies <paramref name="text"/>; returns whether it worked.</summary>
    bool SetText(string text);
}

/// <summary>
/// <see cref="IClipboard"/> through WinForms, on a short-lived STA thread (the clipboard needs
/// one, and the UIA thread must not wait on another application's clipboard viewer).
/// </summary>
public sealed class StaClipboard(ILogger<StaClipboard> logger) : IClipboard
{
    public bool SetText(string text)
    {
        bool copied = false;
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Clipboard.SetText(text);
                copied = true;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not copy to the clipboard");
            }
        });
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(2)) && copied;
    }
}
