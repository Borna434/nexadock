namespace NexaDock.Core.Models;

/// <summary>One widget instance on the bar, in display order.</summary>
public sealed record WidgetPlacement(string Id, string TypeId)
{
    public static WidgetPlacement Create(string typeId) => new(Guid.NewGuid().ToString("N"), typeId);
}

/// <summary>User interface language.</summary>
public enum AppLanguage
{
    /// <summary>Follow the Windows display language (Croatian if Windows is Croatian, otherwise English).</summary>
    System = 0,
    English = 1,
    Croatian = 2,
}

/// <summary>When a profile switches itself on.</summary>
/// <param name="ScheduleEnabled">Use the time window below.</param>
/// <param name="FromMinutes">Start, minutes after midnight (0–1439).</param>
/// <param name="ToMinutes">End, minutes after midnight; may be earlier than the start (overnight).</param>
/// <param name="Days">Days the schedule applies to; empty means every day.</param>
/// <param name="Apps">Executable names (e.g. "steam.exe"); when one is in front the profile wins.</param>
public sealed record ProfileTrigger(
    bool ScheduleEnabled,
    int FromMinutes,
    int ToMinutes,
    IReadOnlyList<DayOfWeek> Days,
    IReadOnlyList<string> Apps)
{
    public static ProfileTrigger None { get; } = new(false, 9 * 60, 17 * 60, [], []);

    public bool IsAutomatic => ScheduleEnabled || Apps.Count > 0;

    public bool Equals(ProfileTrigger? other) =>
        other is not null
        && ScheduleEnabled == other.ScheduleEnabled
        && FromMinutes == other.FromMinutes
        && ToMinutes == other.ToMinutes
        && Days.SequenceEqual(other.Days)
        && Apps.SequenceEqual(other.Apps, StringComparer.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(ScheduleEnabled, FromMinutes, ToMinutes, Days.Count, Apps.Count);
}

/// <summary>A named widget layout (Work, Gaming, Study, …) with optional automatic triggers.</summary>
public sealed record DockProfile(string Id, string Name, IReadOnlyList<WidgetPlacement> Widgets, ProfileTrigger Trigger)
{
    public bool Equals(DockProfile? other) =>
        other is not null
        && Id == other.Id
        && Name == other.Name
        && Widgets.SequenceEqual(other.Widgets)
        && Trigger.Equals(other.Trigger);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Widgets.Count);
}

/// <summary>The widget layout of the extra bar on one secondary display.</summary>
public sealed record DisplayLayout(string DeviceName, IReadOnlyList<WidgetPlacement> Widgets)
{
    public bool Equals(DisplayLayout? other) =>
        other is not null
        && string.Equals(DeviceName, other.DeviceName, StringComparison.OrdinalIgnoreCase)
        && Widgets.SequenceEqual(other.Widgets);

    public override int GetHashCode() => HashCode.Combine(DeviceName.ToUpperInvariant(), Widgets.Count);
}

/// <summary>Complete persisted user configuration. Immutable; use <c>with</c> to change.</summary>
public sealed record DockSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    // ---- Placement ---------------------------------------------------------------------------
    public DockEdge Edge { get; init; } = DockEdge.Bottom;

    /// <summary>Preferred monitor device name; <c>null</c> means primary.</summary>
    public string? MonitorDeviceName { get; init; }

    /// <summary>Span the whole work area (true) or shrink to fit the widgets (false).</summary>
    public bool FullWidth { get; init; } = true;

    /// <summary>Gap between the bar and the screen edges, in DIPs. 0 = flush like a taskbar.</summary>
    public double EdgeOffset { get; init; } = 0;

    // ---- Appearance --------------------------------------------------------------------------
    public DockSizePreset Size { get; init; } = DockSizePreset.Medium;

    public DockThemeId Theme { get; init; } = DockThemeId.Glass;

    /// <summary>Gap between widget tiles in DIPs.</summary>
    public double Spacing { get; init; } = 6;

    /// <summary>Opacity of the tinted glass layer (validated to a usable range).</summary>
    public double Opacity { get; init; } = 0.55;

    public bool ReducedMotion { get; init; }

    /// <summary>
    /// Liquid refraction: the glass edge shows, bent and mirrored, what lies just beyond it. Reads
    /// a thin strip of the screen ten times a second while the bar is visible, so it is opt-in.
    /// </summary>
    public bool LiquidRefraction { get; init; }

    public bool Use24HourClock { get; init; } = true;

    public AppLanguage Language { get; init; } = AppLanguage.System;

    // ---- Behaviour ---------------------------------------------------------------------------
    /// <summary>Hide the Windows taskbar while NexaDock runs and reserve screen space for the bar.</summary>
    public bool ReplaceTaskbar { get; init; }

    /// <summary>Hide the bar while a full-screen app (game, video, presentation) is in front.</summary>
    public bool HideInFullscreen { get; init; } = true;

    public bool LaunchAtStartup { get; init; }

    /// <summary>
    /// Taskbar auto-hide state captured before NexaDock replaced the taskbar, so it can be restored
    /// even after a crash. <c>null</c> when NexaDock has not changed the taskbar.
    /// </summary>
    public int? TaskbarStateBeforeReplace { get; init; }

    /// <summary>Let the bar raise tiles that need attention (battery, disk, timers, …).</summary>
    public bool PulseEnabled { get; init; } = true;

    // ---- Widgets & profiles -----------------------------------------------------------------
    public string WeatherCity { get; init; } = string.Empty;

    public int FocusMinutes { get; init; } = 25;

    public int ShortBreakMinutes { get; init; } = 5;

    public int LongBreakMinutes { get; init; } = 15;

    public bool WidgetsInitialized { get; init; }

    /// <summary>The active layout. Always mirrored into the active profile by normalization.</summary>
    public IReadOnlyList<WidgetPlacement> Widgets { get; init; } = Array.Empty<WidgetPlacement>();

    public IReadOnlyList<DockProfile> Profiles { get; init; } = Array.Empty<DockProfile>();

    public string? ActiveProfileId { get; init; }

    /// <summary>Profile used when no automatic trigger matches (the one the user picked last).</summary>
    public string? DefaultProfileId { get; init; }

    public bool AutoSwitchProfiles { get; init; } = true;

    /// <summary>True once default pinned apps were seeded, so a deliberately empty list is respected.</summary>
    public bool ShortcutsInitialized { get; init; }

    /// <summary>Pinned apps shown in the Apps widget.</summary>
    public IReadOnlyList<DockShortcut> Shortcuts { get; init; } = Array.Empty<DockShortcut>();

    // ---- Multiple displays --------------------------------------------------------------------

    /// <summary>Show an extra bar on every other display (each with its own layout).</summary>
    public bool ShowOnAllDisplays { get; init; } = true;

    /// <summary>Layouts of the extra bars, by display; a display without one gets the default.</summary>
    public IReadOnlyList<DisplayLayout> DisplayLayouts { get; init; } = Array.Empty<DisplayLayout>();

    public static DockSettings Default { get; } = new();

    public DockProfile? ActiveProfile => Profiles.FirstOrDefault(p => p.Id == ActiveProfileId);

    public bool Equals(DockSettings? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Edge == other.Edge
        && string.Equals(MonitorDeviceName, other.MonitorDeviceName, StringComparison.OrdinalIgnoreCase)
        && FullWidth == other.FullWidth
        && EdgeOffset.Equals(other.EdgeOffset)
        && Size == other.Size
        && Theme == other.Theme
        && Spacing.Equals(other.Spacing)
        && Opacity.Equals(other.Opacity)
        && ReducedMotion == other.ReducedMotion
        && LiquidRefraction == other.LiquidRefraction
        && Use24HourClock == other.Use24HourClock
        && Language == other.Language
        && ReplaceTaskbar == other.ReplaceTaskbar
        && HideInFullscreen == other.HideInFullscreen
        && LaunchAtStartup == other.LaunchAtStartup
        && TaskbarStateBeforeReplace == other.TaskbarStateBeforeReplace
        && PulseEnabled == other.PulseEnabled
        && string.Equals(WeatherCity, other.WeatherCity, StringComparison.Ordinal)
        && FocusMinutes == other.FocusMinutes
        && ShortBreakMinutes == other.ShortBreakMinutes
        && LongBreakMinutes == other.LongBreakMinutes
        && WidgetsInitialized == other.WidgetsInitialized
        && Widgets.SequenceEqual(other.Widgets)
        && Profiles.SequenceEqual(other.Profiles)
        && ActiveProfileId == other.ActiveProfileId
        && DefaultProfileId == other.DefaultProfileId
        && AutoSwitchProfiles == other.AutoSwitchProfiles
        && ShortcutsInitialized == other.ShortcutsInitialized
        && Shortcuts.SequenceEqual(other.Shortcuts)
        && ShowOnAllDisplays == other.ShowOnAllDisplays
        && DisplayLayouts.SequenceEqual(other.DisplayLayouts);

    public override int GetHashCode() =>
        HashCode.Combine(Edge, Size, Theme, Spacing, Opacity, EdgeOffset, Widgets.Count, Profiles.Count);
}
