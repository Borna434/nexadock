using NexaDock.Core.Models;

namespace NexaDock.Core.Theming;

/// <summary>Framework-neutral colour palette (#RRGGBB / #AARRGGBB strings).</summary>
/// <param name="TintStrength">Multiplier applied to the user's opacity for the base tint.</param>
/// <param name="TileStrength">How strongly widget tiles stand out from the bar (0..1).</param>
public sealed record ThemePalette(
    DockThemeId Id,
    string DisplayName,
    string Description,
    bool IsDark,
    string TintTop,
    string TintBottom,
    string Foreground,
    string SecondaryForeground,
    string Accent,
    string Highlight,
    string Glow,
    double TintStrength,
    double TileStrength);

public static class ThemePalettes
{
    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new(DockThemeId.Glass, "Liquid Glass", "Clear glass that lets the wallpaper through", true,
            TintTop: "#FF5A6478", TintBottom: "#FF1C2230",
            Foreground: "#FFFFFFFF", SecondaryForeground: "#FFD3DAE6",
            Accent: "#FF8AB4FF", Highlight: "#FFFFFFFF", Glow: "#FFB9D2FF",
            TintStrength: 0.55, TileStrength: 0.9),

        new(DockThemeId.Onyx, "Onyx", "Deep black, quiet and focused", true,
            TintTop: "#FF16171B", TintBottom: "#FF050506",
            Foreground: "#FFF5F5F7", SecondaryForeground: "#FF9A9CA5",
            Accent: "#FF4CD964", Highlight: "#FFFFFFFF", Glow: "#FF3A3D46",
            TintStrength: 1.35, TileStrength: 0.75),

        new(DockThemeId.Pearl, "Pearl", "Bright frosted white", false,
            TintTop: "#FFFFFFFF", TintBottom: "#FFE3E8F0",
            Foreground: "#FF12161F", SecondaryForeground: "#FF56606F",
            Accent: "#FF2F6FEB", Highlight: "#FFFFFFFF", Glow: "#FFB8CCF5",
            TintStrength: 1.25, TileStrength: 1.0),

        new(DockThemeId.Midnight, "Midnight", "Navy dusk with a cool rim", true,
            TintTop: "#FF243050", TintBottom: "#FF0A0F1E",
            Foreground: "#FFF2F5FC", SecondaryForeground: "#FFA2AECA",
            Accent: "#FF7FA9FF", Highlight: "#FFDCE6FF", Glow: "#FF4C6FD0",
            TintStrength: 1.2, TileStrength: 0.8),

        new(DockThemeId.Aurora, "Aurora", "Teal to violet shimmer", true,
            TintTop: "#FF125B6A", TintBottom: "#FF35196A",
            Foreground: "#FFF1FCFF", SecondaryForeground: "#FFB5DCE8",
            Accent: "#FF5EF2D6", Highlight: "#FFD9FFF8", Glow: "#FF6CE6FF",
            TintStrength: 1.15, TileStrength: 0.85),
    ];

    public static ThemePalette Get(DockThemeId id) =>
        All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
