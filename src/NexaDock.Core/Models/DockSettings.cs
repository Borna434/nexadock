namespace NexaDock.Core.Models;

/// <summary>Complete persisted user configuration. Immutable; use <c>with</c> to change.</summary>
public sealed record DockSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DockEdge Edge { get; init; } = DockEdge.Bottom;

    /// <summary>Preferred monitor device name; <c>null</c> means primary.</summary>
    public string? MonitorDeviceName { get; init; }

    public DockSizePreset Size { get; init; } = DockSizePreset.Medium;

    public DockThemeId Theme { get; init; } = DockThemeId.Midnight;

    /// <summary>Preferred dock width in DIPs. Expanded to fit content and clamped to the monitor.</summary>
    public double Width { get; init; } = 720;

    /// <summary>Gap between shortcut tiles in DIPs.</summary>
    public double Spacing { get; init; } = 6;

    /// <summary>Opacity of the tinted glass layer (validated to a usable range).</summary>
    public double Opacity { get; init; } = 0.55;

    /// <summary>Distance from the work-area edge in DIPs.</summary>
    public double EdgeOffset { get; init; } = 10;

    public bool ReducedMotion { get; init; }

    /// <summary>True once default shortcuts were seeded, so a deliberately empty list is respected.</summary>
    public bool ShortcutsInitialized { get; init; }

    public IReadOnlyList<DockShortcut> Shortcuts { get; init; } = Array.Empty<DockShortcut>();

    public static DockSettings Default { get; } = new();

    public bool Equals(DockSettings? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Edge == other.Edge
        && string.Equals(MonitorDeviceName, other.MonitorDeviceName, StringComparison.OrdinalIgnoreCase)
        && Size == other.Size
        && Theme == other.Theme
        && Width.Equals(other.Width)
        && Spacing.Equals(other.Spacing)
        && Opacity.Equals(other.Opacity)
        && EdgeOffset.Equals(other.EdgeOffset)
        && ReducedMotion == other.ReducedMotion
        && ShortcutsInitialized == other.ShortcutsInitialized
        && Shortcuts.SequenceEqual(other.Shortcuts);

    public override int GetHashCode() =>
        HashCode.Combine(Edge, Size, Theme, Width, Spacing, Opacity, EdgeOffset, Shortcuts.Count);
}
