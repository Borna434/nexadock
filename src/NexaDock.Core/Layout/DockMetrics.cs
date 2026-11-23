using NexaDock.Core.Models;

namespace NexaDock.Core.Layout;

/// <summary>Dimensions (DIPs) derived from a size preset.</summary>
public sealed record DockMetrics(
    double Height,
    double TileHeight,
    double IconSize,
    double Padding,
    double PrimaryFontSize,
    double SecondaryFontSize)
{
    /// <summary>Square app button inside the Apps widget.</summary>
    public double AppButtonSize => TileHeight;

    public static DockMetrics For(DockSizePreset preset) => preset switch
    {
        DockSizePreset.Small => new DockMetrics(48, 38, 24, 5, 12.5, 10),
        DockSizePreset.Large => new DockMetrics(66, 54, 34, 6, 15, 11.5),
        _ => new DockMetrics(56, 46, 28, 5, 13.5, 10.5),
    };
}
