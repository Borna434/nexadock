using NexaDock.Core.Models;

namespace NexaDock.Core.Layout;

/// <summary>Dimensions (DIPs) derived from a size preset.</summary>
public sealed record DockMetrics(
    double Height,
    double TileSize,
    double IconSize,
    double Padding,
    double ClockFontSize,
    double DateFontSize,
    double ControlButtonSize)
{
    /// <summary>Approximate space used by clock, separator and Settings/Exit buttons.</summary>
    public double TrailingWidth => ClockFontSize * 5.2 + 26 + ControlButtonSize * 2 + 10;

    public static DockMetrics For(DockSizePreset preset) => preset switch
    {
        DockSizePreset.Small => new DockMetrics(56, 44, 28, 6, 13, 10.5, 30),
        DockSizePreset.Large => new DockMetrics(84, 68, 46, 8, 17, 12.5, 40),
        _ => new DockMetrics(68, 54, 36, 7, 15, 11.5, 34),
    };

    /// <summary>Minimum width needed to show every tile and the trailing controls.</summary>
    public double RequiredWidth(int shortcutCount, double spacing)
    {
        var count = Math.Max(0, shortcutCount);
        var tiles = count * TileSize + Math.Max(0, count - 1) * Math.Max(0, spacing);
        return Padding * 2 + 16 + tiles + 20 + TrailingWidth;
    }
}
