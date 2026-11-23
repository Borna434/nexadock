using NexaDock.Core.Abstractions;
using NexaDock.Core.Widgets;

namespace NexaDock.Core.Pulse;

public enum PulseSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

/// <summary>
/// Something that needs the user's attention. Text is given as a localization key plus arguments.
/// </summary>
/// <param name="Id">Stable identity: the same condition keeps the same ID while it lasts.</param>
/// <param name="WidgetTypeId">Widget whose tile should glow (and whose card opens on click).</param>
public sealed record PulseAlert(
    string Id,
    string? WidgetTypeId,
    PulseSeverity Severity,
    string MessageKey,
    IReadOnlyList<string> Args,
    int GlyphCode)
{
    public bool Equals(PulseAlert? other) =>
        other is not null && Id == other.Id && Severity == other.Severity && MessageKey == other.MessageKey && Args.SequenceEqual(other.Args);

    public override int GetHashCode() => HashCode.Combine(Id, Severity, MessageKey);
}

public sealed record DriveSpace(string Name, ulong FreeBytes, ulong TotalBytes, bool IsSystem);

/// <summary>Pure rules that turn system readings into alerts (thresholds are unit-tested).</summary>
public static class PulseRules
{
    public const int BatteryWarnPercent = 15;
    public const int BatteryCriticalPercent = 7;
    public const double DiskWarnFraction = 0.10;
    public const ulong DiskWarnBytes = 10UL * 1024 * 1024 * 1024;
    public const double CpuHotPercent = 90;
    public const int CpuHotSamples = 15; // with a 2 s sample interval: half a minute

    public static PulseAlert? Battery(PowerStatus power)
    {
        if (!power.HasBattery || power.IsCharging || power.Percent > BatteryWarnPercent)
        {
            return null;
        }

        var critical = power.Percent <= BatteryCriticalPercent;
        return new PulseAlert("battery", WidgetTypes.ControlCenter,
            critical ? PulseSeverity.Critical : PulseSeverity.Warning,
            "pulse.battery", [power.Percent.ToString(System.Globalization.CultureInfo.InvariantCulture)], 0xE850);
    }

    public static PulseAlert? Storage(IEnumerable<DriveSpace> drives)
    {
        var system = drives.FirstOrDefault(d => d.IsSystem);
        if (system is null || system.TotalBytes == 0)
        {
            return null;
        }

        var fraction = (double)system.FreeBytes / system.TotalBytes;
        if (fraction >= DiskWarnFraction && system.FreeBytes >= DiskWarnBytes)
        {
            return null;
        }

        var freeGb = system.FreeBytes / 1073741824.0;
        return new PulseAlert("storage", WidgetTypes.Storage,
            fraction < 0.03 ? PulseSeverity.Critical : PulseSeverity.Warning,
            "pulse.storage", [system.Name, freeGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)], 0xEDA2);
    }

    /// <summary>True when every one of the most recent <see cref="CpuHotSamples"/> samples is high.</summary>
    public static PulseAlert? Cpu(IReadOnlyList<double> recentSamples)
    {
        if (recentSamples.Count < CpuHotSamples || recentSamples.TakeLast(CpuHotSamples).Any(v => v < CpuHotPercent))
        {
            return null;
        }

        return new PulseAlert("cpu", WidgetTypes.System, PulseSeverity.Warning, "pulse.cpu",
            [recentSamples[^1].ToString("0", System.Globalization.CultureInfo.InvariantCulture)], 0xE9D9);
    }
}
