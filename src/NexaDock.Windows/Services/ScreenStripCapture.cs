using System;
using System.Runtime.InteropServices;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;

namespace NexaDock.Windows.Services;

/// <summary>
/// BitBlt of a thin screen strip into a reusable DIB. A 1920 × 16 strip is about 120 KB and copies
/// in well under a millisecond, so ten times a second stays far below one percent of a core.
/// </summary>
public sealed class ScreenStripCapture : IScreenStripCapture, IDisposable
{
    private IntPtr _memDc;
    private IntPtr _bitmap;
    private IntPtr _old;
    private int _width;
    private int _height;

    public bool Capture(PixelRect region, byte[] bgra)
    {
        if (region.Width <= 0 || region.Height <= 0 || bgra.Length < region.Width * region.Height * 4)
        {
            return false;
        }

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            Ensure(screen, region.Width, region.Height);
            if (!BitBlt(_memDc, 0, 0, region.Width, region.Height, screen, region.X, region.Y, SRCCOPY))
            {
                return false;
            }

            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = region.Width,
                biHeight = -region.Height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            return GetDIBits(_memDc, _bitmap, 0, (uint)region.Height, bgra, ref header, 0) != 0;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void Ensure(IntPtr screen, int width, int height)
    {
        if (_memDc != IntPtr.Zero && width == _width && height == _height)
        {
            return;
        }

        Release();
        _memDc = CreateCompatibleDC(screen);
        _bitmap = CreateCompatibleBitmap(screen, width, height);
        _old = SelectObject(_memDc, _bitmap);
        _width = width;
        _height = height;
    }

    private void Release()
    {
        if (_memDc != IntPtr.Zero)
        {
            SelectObject(_memDc, _old);
            DeleteObject(_bitmap);
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
    }

    public void Dispose() => Release();

    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
}
