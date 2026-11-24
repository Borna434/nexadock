using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexaDock.Core.Abstractions;
using static NexaDock.Windows.Interop.NativeMethods;

namespace NexaDock.Windows.Services;

/// <summary>
/// Battery details and the Windows power mode. The power mode is the "overlay" on the active plan
/// (powrprof.dll), the same switch as Settings › Power and the Quick Settings battery flyout.
/// </summary>
public sealed class PowerService : IPowerService
{
    private static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid Balanced = Guid.Empty;
    private static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    private readonly ILogger<PowerService> _logger;

    public PowerService(ILogger<PowerService> logger) => _logger = logger;

    public BatteryDetails GetBattery()
    {
        if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryFlag == 255)
        {
            return new BatteryDetails(false, 100, false, true, null, false);
        }

        var percent = s.BatteryLifePercent == 255 ? 100 : s.BatteryLifePercent;
        var plugged = s.ACLineStatus == 1;
        var charging = (s.BatteryFlag & 8) != 0;
        TimeSpan? remaining = !plugged && s.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(s.BatteryLifeTime) : null;
        return new BatteryDetails(true, percent, charging, plugged, remaining, (s.SystemStatusFlag & 1) != 0);
    }

    public PowerMode? GetMode()
    {
        try
        {
            if (PowerGetEffectiveOverlayScheme(out var overlay) != 0)
            {
                return null;
            }

            return overlay == Efficiency ? PowerMode.Efficiency
                : overlay == Performance ? PowerMode.Performance
                : overlay == Balanced ? PowerMode.Balanced
                : null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    public bool SetMode(PowerMode mode)
    {
        var guid = mode switch
        {
            PowerMode.Efficiency => Efficiency,
            PowerMode.Performance => Performance,
            _ => Balanced,
        };

        try
        {
            var code = PowerSetActiveOverlayScheme(guid);
            if (code != 0)
            {
                _logger.LogInformation("Setting the power mode failed ({Code}).", code);
            }

            return code == 0;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetEffectiveOverlayScheme(out Guid overlaySchemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);
}
