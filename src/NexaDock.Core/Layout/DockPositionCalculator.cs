using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;

namespace NexaDock.Core.Layout;

/// <summary>Pure placement math: monitor + settings → window bounds in physical pixels.</summary>
public sealed class DockPositionCalculator : IDockPositioner
{
    public PixelRect Calculate(MonitorInfo monitor, DockSettings settings, double contentWidth, PlacementOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(settings);

        var scale = monitor.DpiScale is > 0 and < 10 ? monitor.DpiScale : 1.0;
        var area = options.UseMonitorBounds ? monitor.Bounds : monitor.WorkArea;
        var metrics = DockMetrics.For(settings.Size);
        var padding = Math.Max(0, options.WindowPadding);
        var offsetDip = Math.Max(0, settings.EdgeOffset);

        // Width: full span (minus the same margin used at the edge) or just the content.
        var availableDip = Math.Max(1, area.Width / scale - offsetDip * 2);
        var widthDip = settings.FullWidth ? availableDip : Math.Min(Math.Max(contentWidth, 1), availableDip);

        var glassW = (int)Math.Round(widthDip * scale);
        var glassH = (int)Math.Round(metrics.Height * scale);
        var padPx = (int)Math.Round(padding * scale);
        var offsetPx = (int)Math.Round(offsetDip * scale);

        // Never let the offset push the bar outside the area.
        offsetPx = Math.Min(offsetPx, Math.Max(0, area.Height - glassH));

        // The glass position is independent of the padding; the transparent padding (shadow area)
        // simply surrounds it and may overlap the edge.
        var glassX = area.X + (area.Width - glassW) / 2;
        var glassY = settings.Edge == DockEdge.Top
            ? area.Y + offsetPx
            : area.Bottom - offsetPx - glassH;

        return new PixelRect(glassX - padPx, glassY - padPx, glassW + padPx * 2, glassH + padPx * 2);
    }

    /// <summary>
    /// The strip an app bar should reserve so maximised windows stop short of the bar
    /// (bar height plus the gap on both sides of it).
    /// </summary>
    public static PixelRect ReservedStrip(MonitorInfo monitor, DockSettings settings)
    {
        var scale = monitor.DpiScale is > 0 and < 10 ? monitor.DpiScale : 1.0;
        var metrics = DockMetrics.For(settings.Size);
        var thickness = (int)Math.Round((metrics.Height + Math.Max(0, settings.EdgeOffset) * 2) * scale);
        var b = monitor.Bounds;
        return settings.Edge == DockEdge.Top
            ? new PixelRect(b.X, b.Y, b.Width, thickness)
            : new PixelRect(b.X, b.Bottom - thickness, b.Width, thickness);
    }
}
