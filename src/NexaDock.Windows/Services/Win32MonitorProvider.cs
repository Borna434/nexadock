using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>Enumerates displays with per-monitor effective DPI (requires a PerMonitorV2 process).</summary>
public sealed class Win32MonitorProvider : IMonitorProvider
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), szDevice = string.Empty };
            if (!GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            var scale = 1.0;
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                scale = dpiX / 96.0;
            }

            result.Add(new MonitorInfo(
                info.szDevice,
                ToPixelRect(info.rcMonitor),
                ToPixelRect(info.rcWork),
                scale,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        result.Sort((a, b) => string.CompareOrdinal(a.DeviceName, b.DeviceName));
        return result;
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
