using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Seneschal.Core;

/// <summary>
/// A picture of the window the person is using, taken by the app itself. Seneschal runs on the person's own machine, and when they press Snip the window
/// in front is the browser showing Seneschal, so "the window in front" is Seneschal's own window: nothing has to be chosen and the browser asks for no
/// permission. Windows only; anywhere else the page falls back to the browser's own screen capture.
/// </summary>
public static class WindowCapture
{
    private const int SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000, ExtendedFrameBounds = 9, BiRgb = 0, DibRgbColors = 0;
    private const int MaxSide = 16384;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorUsed, ColorImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Rect rect, int size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int op);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[]? bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr gdiObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    /// <summary>The window in front, as a PNG. Null when there is none, it is minimized, or this is not Windows.</summary>
    public static byte[]? CaptureForeground()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return Capture(GetForegroundWindow());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A window as it looks on the screen right now (what is drawn over it is in the picture too), as a PNG.</summary>
    public static byte[]? Capture(IntPtr window)
    {
        if (window == IntPtr.Zero || IsIconic(window)) return null;

        // Pixels, not the scaled units a window that is not DPI aware is told about
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            // The visible edge of the window, without the invisible border Windows 10 and 11 add around it
            if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out var rect, Marshal.SizeOf<Rect>()) != 0 && !GetWindowRect(window, out rect)) return null;
            int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
            if (width < 1 || height < 1 || width > MaxSide || height > MaxSide) return null;

            screen = GetDC(IntPtr.Zero);
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, width, height);
            old = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, SrcCopy | CaptureBlt)) return null;

            var info = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32, Compression = BiRgb };
            var bgra = new byte[width * height * 4];
            SelectObject(memory, old);
            old = IntPtr.Zero;
            if (GetDIBits(memory, bitmap, 0, (uint)height, bgra, ref info, DibRgbColors) == 0) return null;
            return EncodePng(bgra, width, height);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memory, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>Raw 32 bit pixels (blue, green, red, unused) as a PNG of 24 bit color.</summary>
    internal static byte[] EncodePng(byte[] bgra, int width, int height)
    {
        var scanlines = new byte[height * (1 + width * 3)];
        var o = 0;
        for (var y = 0; y < height; y++)
        {
            scanlines[o++] = 0; // no filter
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                scanlines[o++] = bgra[row + x * 4 + 2];
                scanlines[o++] = bgra[row + x * 4 + 1];
                scanlines[o++] = bgra[row + x * 4];
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteInt(header, 0, width);
        WriteInt(header, 4, height);
        header[8] = 8; // bits per channel
        header[9] = 2; // color: red, green, blue
        Chunk(output, "IHDR", header);
        using (var packed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(packed, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(scanlines);
            Chunk(output, "IDAT", packed.ToArray());
        }
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        output.Write(length);
        var body = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        output.Write(body);
        var crc = new byte[4];
        WriteInt(crc, 0, unchecked((int)Crc32(body)));
        output.Write(crc);
    }

    private static void WriteInt(byte[] target, int at, int value)
    {
        target[at] = (byte)(value >> 24);
        target[at + 1] = (byte)(value >> 16);
        target[at + 2] = (byte)(value >> 8);
        target[at + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
