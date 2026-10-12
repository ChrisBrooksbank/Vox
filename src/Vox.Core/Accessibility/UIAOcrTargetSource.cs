using System.Runtime.InteropServices;
using Vox.Core.Buffer;
using Vox.Core.Ocr;

namespace Vox.Core.Accessibility;

/// <summary>
/// <see cref="IOcrTargetSource"/> through UIA (the navigator object and page elements, on the
/// UIA thread) and Win32 (the foreground window).
/// </summary>
public sealed class UIAOcrTargetSource(UIAThread uiaThread, ObjectNavigationCommands navigation,
    BrowseDocumentTracker documents, IForegroundWindow foregroundWindow) : IOcrTargetSource
{
    public Task<ScreenRect?> NavigatorBoundsAsync() =>
        uiaThread.RunAsync(() => navigation.CurrentObject()?.GetBounds());

    public Task<ScreenRect?> WindowBoundsAsync()
    {
        if (foregroundWindow.Get() is not { } window || IsIconic(window.Handle) || !GetWindowRect(window.Handle, out var rect))
            return Task.FromResult<ScreenRect?>(null);
        var bounds = ScreenRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return Task.FromResult<ScreenRect?>(bounds.IsEmpty ? null : bounds);
    }

    public Task<ScreenRect?> ElementBoundsAsync(VBufferNode node) => documents.ElementBoundsAsync(node);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
}
