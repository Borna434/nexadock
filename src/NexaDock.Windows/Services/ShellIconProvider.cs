using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>
/// Extracts the real shell icon (the same image Explorer shows) for files, shortcuts and packaged
/// apps via IShellItemImageFactory. Must be called on an STA thread.
/// </summary>
public sealed class ShellIconProvider : IIconProvider
{
    private static Guid _imageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private readonly ConcurrentDictionary<(string, int), IconImage?> _cache = new();
    private readonly ILogger<ShellIconProvider> _logger;

    public ShellIconProvider(ILogger<ShellIconProvider> logger) => _logger = logger;

    public IconImage? GetIcon(string target, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        var size = Math.Clamp(pixelSize, 16, 256);
        return _cache.GetOrAdd((target.ToUpperInvariant(), size), _ => Extract(target, size));
    }

    private IconImage? Extract(string target, int size)
    {
        var parsingName = Environment.ExpandEnvironmentVariables(target);
        try
        {
            var hr = SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref _imageFactoryIid, out var factory);
            if (hr < 0 || factory is null)
            {
                _logger.LogDebug("No shell item for {Target} (0x{Hr:X8}).", target, hr);
                return null;
            }

            try
            {
                // Exact size (no BIGGERSIZEOK): the shell picks the icon frame designed for this
                // size, or scales a larger one with its own filter. A bigger bitmap downscaled by
                // WPF at a non-integer ratio looks soft and blurry.
                hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY, out var hbitmap);
                if (hr < 0 || hbitmap == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return ReadPixels(hbitmap);
                }
                finally
                {
                    DeleteObject(hbitmap);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(factory);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FileNotFoundException or ArgumentException)
        {
            _logger.LogDebug(ex, "Icon extraction failed for {Target}.", target);
            return null;
        }
    }

    /// <summary>Copies a 32bpp HBITMAP into top-down premultiplied BGRA bytes.</summary>
    private static IconImage? ReadPixels(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, System.Runtime.InteropServices.Marshal.SizeOf<BITMAP>(), out var bmp) == 0
            || bmp.bmWidth <= 0 || bmp.bmHeight == 0)
        {
            return null;
        }

        var width = bmp.bmWidth;
        var height = Math.Abs(bmp.bmHeight);
        var header = new BITMAPINFOHEADER
        {
            biSize = 40,
            biWidth = width,
            biHeight = -height, // request top-down rows
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };

        var pixels = new byte[width * height * 4];
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hbitmap, 0, (uint)height, pixels, ref header, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        // Some icons (old 24-bit ones) come back with an all-zero alpha channel: treat as opaque.
        var anyAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                anyAlpha = true;
                break;
            }
        }

        if (!anyAlpha)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }
        }
        else if (!IsPremultiplied(pixels))
        {
            // Icon bitmaps usually come back with straight (unpremultiplied) alpha. Drawn as
            // premultiplied, their soft edge pixels are far too bright: a white halo.
            Premultiply(pixels);
        }

        return new IconImage(width, height, pixels);
    }

    /// <summary>In premultiplied data no colour channel can exceed its pixel's alpha.</summary>
    internal static bool IsPremultiplied(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a)
            {
                return false;
            }
        }

        return true;
    }

    internal static void Premultiply(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255)
            {
                continue;
            }

            bgra[i] = (byte)((bgra[i] * a + 127) / 255);
            bgra[i + 1] = (byte)((bgra[i + 1] * a + 127) / 255);
            bgra[i + 2] = (byte)((bgra[i + 2] * a + 127) / 255);
        }
    }
}
