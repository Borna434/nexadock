namespace NexaDock.Core.Models;

/// <summary>One widget instance on the bar, in display order.</summary>
public sealed record WidgetPlacement(string Id, string TypeId)
{
    public static WidgetPlacement Create(string typeId) => new(Guid.NewGuid().ToString("N"), typeId);
}

/// <summary>Complete persisted user configuration. Immutable; use <c>with</c> to change.</summary>
public sealed record DockSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    // ---- Placement ---------------------------------------------------------------------------
    public DockEdge Edge { get; init; } = DockEdge.Bottom;

    /// <summary>Preferred monitor device name; <c>null</c> means primary.</summary>
    public string? MonitorDeviceName { get; init; }

    /// <summary>Span the whole work area (true) or shrink to fit the widgets (false).</summary>
    public bool FullWidth { get; init; } = true;

    /// <summary>Gap between the bar and the screen edges, in DIPs. 0 = flush like a taskbar.</summary>
    public double EdgeOffset { get; init; } = 8;

    // ---- Appearance --------------------------------------------------------------------------
    public DockSizePreset Size { get; init; } = DockSizePreset.Medium;

    public DockThemeId Theme { get; init; } = DockThemeId.Glass;

    /// <summary>Gap between widget tiles in DIPs.</summary>
    public double Spacing { get; init; } = 6;

    /// <summary>Opacity of the tinted glass layer (validated to a usable range).</summary>
    public double Opacity { get; init; } = 0.55;

    public bool ReducedMotion { get; init; }

    public bool Use24HourClock { get; init; } = true;

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

    // ---- Widgets -----------------------------------------------------------------------------
    public string WeatherCity { get; init; } = string.Empty;

    public bool WidgetsInitialized { get; init; }

    public IReadOnlyList<WidgetPlacement> Widgets { get; init; } = Array.Empty<WidgetPlacement>();

    /// <summary>True once default pinned apps were seeded, so a deliberately empty list is respected.</summary>
    public bool ShortcutsInitialized { get; init; }

    /// <summary>Pinned apps shown in the Apps widget.</summary>
    public IReadOnlyList<DockShortcut> Shortcuts { get; init; } = Array.Empty<DockShortcut>();

    public static DockSettings Default { get; } = new();

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
        && Use24HourClock == other.Use24HourClock
        && ReplaceTaskbar == other.ReplaceTaskbar
        && HideInFullscreen == other.HideInFullscreen
        && LaunchAtStartup == other.LaunchAtStartup
        && TaskbarStateBeforeReplace == other.TaskbarStateBeforeReplace
        && string.Equals(WeatherCity, other.WeatherCity, StringComparison.Ordinal)
        && WidgetsInitialized == other.WidgetsInitialized
        && Widgets.SequenceEqual(other.Widgets)
        && ShortcutsInitialized == other.ShortcutsInitialized
        && Shortcuts.SequenceEqual(other.Shortcuts);

    public override int GetHashCode() =>
        HashCode.Combine(Edge, Size, Theme, Spacing, Opacity, EdgeOffset, Widgets.Count, Shortcuts.Count);
}
