using System.Runtime.InteropServices;
using Vox.Core.Lifecycle;

namespace Vox.Core.Ocr;

/// <summary>
/// <see cref="IScreenCapture"/> through GDI (BitBlt from the screen, layered windows included).
/// Never in secure mode.
/// </summary>
public sealed class GdiScreenCapture(RunPolicy policy) : IScreenCapture
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint DIB_RGB_COLORS = 0;

    public bool IsAvailable => !policy.IsSecure;

    public CapturedImage? Capture(ScreenRect bounds)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("The screen can't be captured on a secure screen");

        var screen = new ScreenRect(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
        var area = bounds.Intersect(screen);
        if (area.IsEmpty)
            return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            var info = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = area.Width,
                Height = -area.Height, // top row first
                Planes = 1,
                BitCount = 32,
            };
            bitmap = CreateDIBSection(memoryDc, ref info, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("Could not create a bitmap for the screen capture");
            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, area.Width, area.Height, screenDc, area.Left, area.Top, SRCCOPY | CAPTUREBLT))
                throw new InvalidOperationException("Could not copy the screen");

            var pixels = new byte[area.Width * area.Height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // GDI leaves alpha undefined
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return new CapturedImage(area.Width, area.Height, pixels, area);
        }
        finally
        {
            if (previous != IntPtr.Zero)
                SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    // BITMAPINFO with no colour table: just its header, for 32-bit pixels
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits,
        IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source,
        int sourceX, int sourceY, uint rop);
}
