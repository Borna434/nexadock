namespace NexaDock.Core.Models;

/// <summary>A display as reported by the OS. Bounds and work area are in physical pixels.</summary>
/// <param name="DeviceName">GDI device name, e.g. <c>\\.\DISPLAY1</c>.</param>
/// <param name="DpiScale">Effective scale factor (1.0 = 96 DPI).</param>
public sealed record MonitorInfo(
    string DeviceName,
    PixelRect Bounds,
    PixelRect WorkArea,
    double DpiScale,
    bool IsPrimary)
{
    private const string GdiPrefix = @"\\.\DISPLAY";

    public string DisplayLabel
    {
        get
        {
            var number = DeviceName.StartsWith(GdiPrefix, StringComparison.OrdinalIgnoreCase)
                ? DeviceName[GdiPrefix.Length..]
                : DeviceName;
            var primary = IsPrimary ? " (primary)" : string.Empty;
            return $"Display {number}{primary} — {Bounds.Width} × {Bounds.Height} @ {DpiScale * 100:0}%";
        }
    }
}
