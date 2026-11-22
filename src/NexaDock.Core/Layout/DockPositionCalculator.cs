using NexaDock.Core.Abstractions;
using NexaDock.Core.Models;

namespace NexaDock.Core.Layout;

/// <summary>Pure placement math: monitor work area + settings → window bounds in physical pixels.</summary>
public sealed class DockPositionCalculator : IDockPositioner
{
    /// <summary>Minimum horizontal room kept between the dock and the work-area edges (DIPs).</summary>
    public const double SideMargin = 8;

    public PixelRect Calculate(MonitorInfo monitor, DockSettings settings, double requiredContentWidth, double windowPadding = 0)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(settings);

        var scale = monitor.DpiScale is > 0 and < 10 ? monitor.DpiScale : 1.0;
        var work = monitor.WorkArea;
        var metrics = DockMetrics.For(settings.Size);
        var padding = Math.Max(0, windowPadding);

        // Width: honour the user's preference, grow to fit content, never exceed the work area.
        var availableDip = Math.Max(1, work.Width / scale - SideMargin * 2 - padding * 2);
        var desiredDip = Math.Max(settings.Width, requiredContentWidth);
        var widthDip = Math.Min(desiredDip, availableDip);

        var glassW = (int)Math.Round(widthDip * scale);
        var glassH = (int)Math.Round(metrics.Height * scale);
        var padPx = (int)Math.Round(padding * scale);
        var offsetPx = (int)Math.Round(Math.Max(0, settings.EdgeOffset) * scale);

        // Never let the offset push the dock outside the work area.
        offsetPx = Math.Min(offsetPx, Math.Max(0, work.Height - glassH));

        // The glass position is independent of the padding; the transparent padding (shadow area)
        // simply surrounds it and may overlap the edge.
        var glassX = work.X + (work.Width - glassW) / 2;
        var glassY = settings.Edge == DockEdge.Top
            ? work.Y + offsetPx
            : work.Bottom - offsetPx - glassH;

        return new PixelRect(glassX - padPx, glassY - padPx, glassW + padPx * 2, glassH + padPx * 2);
    }
}
