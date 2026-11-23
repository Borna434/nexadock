using NexaDock.Core.Models;

namespace NexaDock.Core.Widgets;

public enum WidgetCategory
{
    Apps,
    Clocks,
    System,
    Media,
    Weather,
    Layout,
}

/// <summary>Static description of a widget type, used by the picker and for validation.</summary>
/// <param name="Glyph">Segoe Fluent Icons code point shown in the picker.</param>
/// <param name="AllowMultiple">Whether more than one instance may be on the bar.</param>
public sealed record WidgetDescriptor(
    string TypeId,
    string Name,
    WidgetCategory Category,
    string Description,
    string Glyph,
    bool AllowMultiple = false);

public static class WidgetTypes
{
    public const string Apps = "apps";
    public const string Clock = "clock";
    public const string Calendar = "calendar";
    public const string System = "system";
    public const string Weather = "weather";
    public const string NowPlaying = "nowplaying";
    public const string ControlCenter = "controlcenter";
    public const string ShowDesktop = "showdesktop";
    public const string DayProgress = "dayprogress";
    public const string Spacer = "spacer";
}

public static class WidgetCatalog
{
    public static IReadOnlyList<WidgetDescriptor> All { get; } =
    [
        new(WidgetTypes.Apps, "Apps", WidgetCategory.Apps,
            "Start, your pinned apps and every open window. Hover a running app for live previews.", Glyph(0xE71D)),
        new(WidgetTypes.ShowDesktop, "Show Desktop", WidgetCategory.Apps,
            "Hide every window, and bring them back.", Glyph(0xE7F4)),
        new(WidgetTypes.Clock, "Clock", WidgetCategory.Clocks,
            "Large, readable time and the date, in 12- or 24-hour format.", Glyph(0xE823)),
        new(WidgetTypes.Calendar, "Day Number", WidgetCategory.Clocks,
            "Today as a calendar page; opens the month.", Glyph(0xE787)),
        new(WidgetTypes.DayProgress, "Day Progress", WidgetCategory.Clocks,
            "How much of today, the week and the year is behind you.", Glyph(0xE916)),
        new(WidgetTypes.System, "CPU & Memory", WidgetCategory.System,
            "Processor and memory load as rings; opens the busiest apps.", Glyph(0xE9D9)),
        new(WidgetTypes.ControlCenter, "Control Center", WidgetCategory.System,
            "Network, sound and battery at a glance; opens Quick Settings and notifications.", Glyph(0xE713)),
        new(WidgetTypes.NowPlaying, "Now Playing", WidgetCategory.Media,
            "Cover art, title and controls for whatever is playing.", Glyph(0xE8D6)),
        new(WidgetTypes.Weather, "Weather", WidgetCategory.Weather,
            "Temperature now, with today's range and the next days.", Glyph(0xE706)),
        new(WidgetTypes.Spacer, "Flexible Space", WidgetCategory.Layout,
            "Pushes the widgets after it to the far end of the bar.", Glyph(0xE740), AllowMultiple: true),
    ];

    /// <summary>Segoe Fluent Icons glyph from its code point.</summary>
    public static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);

    public static WidgetDescriptor? Get(string? typeId) =>
        All.FirstOrDefault(d => string.Equals(d.TypeId, typeId, StringComparison.OrdinalIgnoreCase));

    /// <summary>First-run layout: apps on the left, live widgets on the right.</summary>
    public static IReadOnlyList<WidgetPlacement> DefaultLayout() =>
    [
        WidgetPlacement.Create(WidgetTypes.Apps),
        WidgetPlacement.Create(WidgetTypes.Spacer),
        WidgetPlacement.Create(WidgetTypes.NowPlaying),
        WidgetPlacement.Create(WidgetTypes.System),
        WidgetPlacement.Create(WidgetTypes.Weather),
        WidgetPlacement.Create(WidgetTypes.ControlCenter),
        WidgetPlacement.Create(WidgetTypes.Calendar),
        WidgetPlacement.Create(WidgetTypes.Clock),
    ];
}
