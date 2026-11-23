namespace NexaDock.Core.Models;

/// <summary>Valid ranges for numeric settings. Shared by validation and the settings UI.</summary>
public static class SettingsLimits
{
    public const double MinSpacing = 0;
    public const double MaxSpacing = 24;

    public const double MinOpacity = 0.15;
    public const double MaxOpacity = 0.95;

    public const double MinEdgeOffset = 0;
    public const double MaxEdgeOffset = 48;

    public const int MaxShortcuts = 32;
    public const int MaxWidgets = 40;
    public const int MaxCityLength = 80;

    public const int MaxProfiles = 8;
    public const int MaxProfileNameLength = 32;
    public const int MaxTriggerApps = 20;

    public const int MinFocusMinutes = 5;
    public const int MaxFocusMinutes = 120;
    public const int MinBreakMinutes = 1;
    public const int MaxBreakMinutes = 60;
}
