using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using Windows.Devices.Radios;
using static NexaDock.Windows.Interop.NativeMethods;
using CoreRadioKind = NexaDock.Core.Abstractions.RadioKind;

namespace NexaDock.Windows.Services;

/// <summary>Keyboard layout of the window in front, and switching to the next installed layout.</summary>
public sealed class KeyboardLayoutService : IKeyboardLayoutService
{
    private const int WM_INPUTLANGCHANGEREQUEST = 0x0050;

    public KeyboardLayoutInfo GetCurrent()
    {
        var hkl = CurrentLayout();
        var langId = (int)(hkl.ToInt64() & 0xFFFF);
        try
        {
            var culture = CultureInfo.GetCultureInfo(langId);
            return new KeyboardLayoutInfo(culture.TwoLetterISOLanguageName.ToUpperInvariant(), culture.DisplayName);
        }
        catch (CultureNotFoundException)
        {
            return new KeyboardLayoutInfo("??", $"0x{langId:X4}");
        }
    }

    public void SwitchNext()
    {
        var count = GetKeyboardLayoutList(0, null);
        if (count <= 1)
        {
            return;
        }

        var layouts = new IntPtr[count];
        GetKeyboardLayoutList(count, layouts);
        var current = CurrentLayout();
        var index = Array.IndexOf(layouts, current);
        var next = layouts[(index + 1) % layouts.Length];

        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            PostMessage(fg, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, next);
        }
    }

    private static IntPtr CurrentLayout()
    {
        var fg = GetForegroundWindow();
        var thread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out _);
        return GetKeyboardLayout(thread);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);
}

/// <summary>Wi-Fi and Bluetooth radios via the documented Windows.Devices.Radios API.</summary>
public sealed class WinRtRadioService : IRadioService
{
    private readonly ILogger<WinRtRadioService> _logger;
    private bool? _access;

    public WinRtRadioService(ILogger<WinRtRadioService> logger) => _logger = logger;

    public async Task<IReadOnlyList<RadioInfo>> GetRadiosAsync()
    {
        try
        {
            if (!await EnsureAccessAsync())
            {
                return [];
            }

            var radios = await Radio.GetRadiosAsync();
            return radios
                .Where(r => r.Kind is global::Windows.Devices.Radios.RadioKind.WiFi or global::Windows.Devices.Radios.RadioKind.Bluetooth)
                .GroupBy(r => r.Kind)
                .Select(g => g.First())
                .Select(r => new RadioInfo(
                    r.Kind == global::Windows.Devices.Radios.RadioKind.WiFi ? CoreRadioKind.WiFi : CoreRadioKind.Bluetooth,
                    r.Name,
                    r.State == RadioState.On))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Radio query failed.");
            return [];
        }
    }

    public async Task<bool> SetAsync(CoreRadioKind kind, bool on)
    {
        try
        {
            if (!await EnsureAccessAsync())
            {
                return false;
            }

            var wanted = kind == CoreRadioKind.WiFi ? global::Windows.Devices.Radios.RadioKind.WiFi : global::Windows.Devices.Radios.RadioKind.Bluetooth;
            var radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == wanted);
            if (radio is null)
            {
                return false;
            }

            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return result == RadioAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Switching {Kind} failed.", kind);
            return false;
        }
    }

    private async Task<bool> EnsureAccessAsync()
    {
        _access ??= await Radio.RequestAccessAsync() == RadioAccessStatus.Allowed;
        return _access.Value;
    }
}

/// <summary>
/// Brightness for laptop panels (WMI WmiMonitorBrightness) and external monitors that support
/// DDC/CI (dxva2 Get/SetMonitorBrightness). Calls are slow; never run them on the UI thread.
/// </summary>
public sealed class BrightnessService : IBrightnessService
{
    private readonly ILogger<BrightnessService> _logger;
    private readonly object _gate = new();

    public BrightnessService(ILogger<BrightnessService> logger) => _logger = logger;

    public IReadOnlyList<DisplayBrightness> GetDisplays()
    {
        lock (_gate)
        {
            var list = new List<DisplayBrightness>();
            list.AddRange(ReadWmi());
            ForEachPhysicalMonitor((id, name, handle) =>
            {
                if (GetMonitorBrightness(handle, out var min, out var cur, out var max) && max > min)
                {
                    list.Add(new DisplayBrightness(id, name, (int)Math.Round(100.0 * (cur - min) / (max - min))));
                }

                return false;
            });
            return list;
        }
    }

    public void Set(string id, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            if (id.StartsWith("wmi:", StringComparison.Ordinal))
            {
                SetWmi(id[4..], percent);
                return;
            }

            ForEachPhysicalMonitor((monitorId, monitorName, handle) =>
            {
                if (monitorId != id || !GetMonitorBrightness(handle, out var min, out var current, out var max))
                {
                    return false;
                }

                SetMonitorBrightness(handle, (uint)Math.Round(min + (max - min) * percent / 100.0));
                return true;
            });
        }
    }

    private IEnumerable<DisplayBrightness> ReadWmi()
    {
        var result = new List<DisplayBrightness>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var o in searcher.Get().OfType<ManagementObject>())
            {
                using (o)
                {
                    result.Add(new DisplayBrightness("wmi:" + o["InstanceName"], "Built-in display", Convert.ToInt32(o["CurrentBrightness"], CultureInfo.InvariantCulture)));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            // Desktop PCs have no WMI brightness class; that is expected.
        }

        return result;
    }

    private void SetWmi(string instance, int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (var o in searcher.Get().OfType<ManagementObject>())
            {
                using (o)
                {
                    if (Equals(o["InstanceName"], instance))
                    {
                        o.InvokeMethod("WmiSetBrightness", [(uint)1, (byte)percent]);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            _logger.LogWarning(ex, "Setting panel brightness failed.");
        }
    }

    /// <summary>Visits every DDC/CI physical monitor; the callback returns true to stop.</summary>
    private void ForEachPhysicalMonitor(Func<string, string, IntPtr, bool> visit)
    {
        var monitors = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            monitors.Add(h);
            return true;
        }, IntPtr.Zero);

        var stop = false;
        for (var m = 0; m < monitors.Count && !stop; m++)
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitors[m], out var count) || count == 0)
            {
                continue;
            }

            var physical = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitors[m], count, physical))
            {
                continue;
            }

            try
            {
                for (var i = 0; i < physical.Length && !stop; i++)
                {
                    var name = string.IsNullOrWhiteSpace(physical[i].szPhysicalMonitorDescription) ? $"Display {m + 1}" : physical[i].szPhysicalMonitorDescription;
                    stop = visit($"ddc:{m}:{i}", name, physical[i].hPhysicalMonitor);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DDC/CI monitor call failed.");
            }
            finally
            {
                DestroyPhysicalMonitors(count, physical);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint min, out uint current, out uint max);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint brightness);
}
