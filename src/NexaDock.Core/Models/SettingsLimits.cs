namespace NexaDock.Core.Models;

/// <summary>Valid ranges for numeric settings. Shared by validation and the settings UI.</summary>
public static class SettingsLimits
{
    public const double MinWidth = 360;
    public const double MaxWidth = 2400;

    public const double MinSpacing = 0;
    public const double MaxSpacing = 24;

    public const double MinOpacity = 0.15;
    public const double MaxOpacity = 0.95;

    public const double MinEdgeOffset = 0;
    public const double MaxEdgeOffset = 64;

    public const int MaxShortcuts = 32;
}
