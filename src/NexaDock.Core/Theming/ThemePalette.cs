using NexaDock.Core.Models;

namespace NexaDock.Core.Theming;

/// <summary>Framework-neutral colour palette (#RRGGBB / #AARRGGBB strings).</summary>
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
    string Glow);

public static class ThemePalettes
{
    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new(DockThemeId.Frost, "Frost", "Cool, bright glass", false,
            TintTop: "#FFFDFEFF", TintBottom: "#FFD9E4F2",
            Foreground: "#FF141B28", SecondaryForeground: "#FF4A5669",
            Accent: "#FF2F7CF6", Highlight: "#FFFFFFFF", Glow: "#FF8FB8FF"),

        new(DockThemeId.Midnight, "Midnight", "Deep neutral dark glass", true,
            TintTop: "#FF2C3446", TintBottom: "#FF0B0F1A",
            Foreground: "#FFF4F6FB", SecondaryForeground: "#FFA7B0C4",
            Accent: "#FF7FA9FF", Highlight: "#FFFFFFFF", Glow: "#FF5B7FD6"),

        new(DockThemeId.Aurora, "Aurora", "Teal to violet shimmer", true,
            TintTop: "#FF16606F", TintBottom: "#FF3A1C6B",
            Foreground: "#FFF1FCFF", SecondaryForeground: "#FFB5DCE8",
            Accent: "#FF5EF2D6", Highlight: "#FFD9FFF8", Glow: "#FF6CE6FF"),

        new(DockThemeId.Ember, "Ember", "Warm sunset glow", true,
            TintTop: "#FFE9875F", TintBottom: "#FF8E2550",
            Foreground: "#FFFFF8F3", SecondaryForeground: "#FFFFDCCB",
            Accent: "#FFFFD166", Highlight: "#FFFFF1E6", Glow: "#FFFF9F6B"),

        new(DockThemeId.Sage, "Sage", "Soft botanical light glass", false,
            TintTop: "#FFF4F8F1", TintBottom: "#FFC3D8BF",
            Foreground: "#FF18261B", SecondaryForeground: "#FF4B5E4E",
            Accent: "#FF2F8A55", Highlight: "#FFFFFFFF", Glow: "#FF9ED6A8"),
    ];

    public static ThemePalette Get(DockThemeId id) =>
        All.FirstOrDefault(p => p.Id == id) ?? All[1];
}
