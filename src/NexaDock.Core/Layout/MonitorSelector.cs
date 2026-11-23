using NexaDock.Core.Models;

namespace NexaDock.Core.Layout;

public static class MonitorSelector
{
    /// <summary>
    /// Picks the preferred monitor, falling back to the primary (then the first) display when the
    /// preferred one is missing. Callers keep the stored preference so the dock moves back when the
    /// display is reconnected.
    /// </summary>
    public static MonitorInfo? Select(IReadOnlyList<MonitorInfo>? monitors, string? preferredDeviceName)
    {
        if (monitors is null || monitors.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            var match = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, preferredDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>The monitor whose bounds contain the point (physical pixels), else the primary.</summary>
    public static MonitorInfo? FromPoint(IReadOnlyList<MonitorInfo>? monitors, int x, int y)
    {
        if (monitors is null || monitors.Count == 0)
        {
            return null;
        }

        return monitors.FirstOrDefault(m => x >= m.Bounds.X && x < m.Bounds.Right && y >= m.Bounds.Y && y < m.Bounds.Bottom)
            ?? Select(monitors, null);
    }
}
