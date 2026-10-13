using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Vox.Core.Navigation;

/// <summary>
/// Base for Vox's own modal dialogs opened by a hotkey while another app (the browser) is in
/// the foreground (the Elements List, the find prompt): takes the foreground and keyboard focus,
/// and closes as cancelled if it can't get it or loses it, so it never sits unfocused in the
/// background with browse-mode keys disabled.
/// </summary>
public abstract class VoxDialog : Form
{
    /// <summary>Shows the dialog modally, centred, on top and in the foreground.</summary>
    public DialogResult ShowInForeground()
    {
        // Arabic and Hebrew: laid out right to left
        if (Localization.VoxStrings.IsRightToLeft)
        {
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
        }
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Shown += (_, _) => BringToForeground();
        return ShowDialog();
    }

    // -------------------------------------------------------------------------
    // Foreground handling
    // -------------------------------------------------------------------------

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, nint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint hWnd);

    private const int ActivationTimeoutMs = 2000;
    private bool _activated;

    /// <summary>
    /// Takes the foreground. Vox isn't the foreground process when the hotkey fires (the browser
    /// is), so plain Activate() is blocked by the foreground lock and only flashes the taskbar.
    /// Briefly attaching to the foreground thread's input state lifts that restriction.
    /// If activation still hasn't happened after a moment, the dialog closes as cancelled so
    /// browse-mode keys (disabled while it is open) come back.
    /// </summary>
    private void BringToForeground()
    {
        try
        {
            var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), nint.Zero);
            var thisThread = GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != thisThread
                            && AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                BringWindowToTop(Handle);
                SetForegroundWindow(Handle);
                Activate();
            }
            finally
            {
                if (attached)
                    AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
        catch (Exception)
        {
            Activate();
        }

        _activationTimer?.Dispose();
        _activationTimer = new System.Windows.Forms.Timer { Interval = ActivationTimeoutMs };
        _activationTimer.Tick += (_, _) =>
        {
            StopActivationTimer();
            if (!_activated && Visible)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        _activationTimer.Start();
    }

    // A running WinForms timer roots itself (and, through its handler, this dialog) until it is
    // stopped; a dialog closed before it ticks, whose thread then ends, would never release it
    private System.Windows.Forms.Timer? _activationTimer;

    private void StopActivationTimer()
    {
        var timer = _activationTimer;
        _activationTimer = null;
        timer?.Stop();
        timer?.Dispose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopActivationTimer();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            StopActivationTimer();
        base.Dispose(disposing);
    }

    protected override void OnActivated(EventArgs e)
    {
        _activated = true;
        base.OnActivated(e);
    }

    /// <summary>
    /// Focus leaving the dialog (Alt+Tab, a click elsewhere) cancels it, so it can never sit
    /// unfocused in the background with browse-mode keys disabled.
    /// </summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Visible && DialogResult == DialogResult.None)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
