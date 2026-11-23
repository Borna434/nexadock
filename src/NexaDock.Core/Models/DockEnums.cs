namespace NexaDock.Core.Models;

public enum DockEdge
{
    Bottom = 0,
    Top = 1,
}

public enum DockSizePreset
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

public enum DockThemeId
{
    Glass = 0,
    Midnight = 1,
    Aurora = 2,
    Onyx = 3,
    Pearl = 4,
}

/// <summary>How the bar surface is rendered.</summary>
public enum BackdropKind
{
    /// <summary>DWM system backdrop (acrylic blur) behind a translucent tint.</summary>
    NativeAcrylic = 0,

    /// <summary>Per-pixel transparent window with a painted translucent surface (no blur).</summary>
    TranslucentFallback = 1,
}
