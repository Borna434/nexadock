using NexaDock.Core.Models;

namespace NexaDock.Core.Widgets;

public enum WidgetCategory
{
    Apps,
    Clocks,
    System,
    Media,
    Weather,
    Productivity,
    AI,
    Smart,
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

    // Phase 3
    public const string Pulse = "pulse";
    public const string Focus = "focus";
    public const string Timer = "timer";
    public const string Stopwatch = "stopwatch";
    public const string Todos = "todos";
    public const string Notes = "notes";
    public const string Volume = "volume";
    public const string Microphone = "microphone";
    public const string Network = "network";
    public const string Storage = "storage";
    public const string RecycleBin = "recyclebin";
    public const string Keyboard = "keyboard";
    public const string QuickToggles = "quicktoggles";
    public const string Brightness = "brightness";

    // Phase 3.5
    public const string Claude = "claude";

    // Phase 4
    public const string Agenda = "agenda";
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
        new(WidgetTypes.Agenda, "Up Next", WidgetCategory.Clocks,
            "Your next calendar event (Google Calendar or any iCal link); the card shows the coming week.", Glyph(0xE8BF)),
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

        // Smart
        new(WidgetTypes.Pulse, "Pulse", WidgetCategory.Smart,
            "Appears only when something needs you: low battery, a full disk, a finished timer, a hot CPU.", Glyph(0xE95E)),

        // Productivity
        new(WidgetTypes.Focus, "Focus", WidgetCategory.Productivity,
            "Pomodoro focus rounds and breaks you start and skip from the bar.", Glyph(0xE7C1)),
        new(WidgetTypes.Timer, "Timer", WidgetCategory.Productivity,
            "A countdown that starts in one click.", Glyph(0xE916)),
        new(WidgetTypes.Stopwatch, "Stopwatch", WidgetCategory.Productivity,
            "Start, pause and lap from the bar; the card keeps every lap.", Glyph(0xE916)),
        new(WidgetTypes.Todos, "To-dos", WidgetCategory.Productivity,
            "Your next open task with a progress ring; tick it off from the bar.", Glyph(0xE9D5)),
        new(WidgetTypes.Notes, "Notes", WidgetCategory.Productivity,
            "A quick note, one click from editing.", Glyph(0xE70B)),

        // Media
        new(WidgetTypes.Volume, "Volume", WidgetCategory.Media,
            "Speaker volume; scroll on the tile to change it.", Glyph(0xE767)),
        new(WidgetTypes.Microphone, "Microphone", WidgetCategory.Media,
            "Mute or unmute your microphone for every app at once.", Glyph(0xE720)),

        // System
        new(WidgetTypes.Network, "Network Speed", WidgetCategory.System,
            "Download and upload speed with a live chart.", Glyph(0xE774)),
        new(WidgetTypes.Storage, "Storage", WidgetCategory.System,
            "Free space on your drives, with usage bars.", Glyph(0xEDA2)),
        new(WidgetTypes.Keyboard, "Keyboard Layout", WidgetCategory.System,
            "The current keyboard layout; click to switch.", Glyph(0xE765)),
        new(WidgetTypes.QuickToggles, "Quick Toggles", WidgetCategory.System,
            "Wi-Fi and Bluetooth on or off in one click.", Glyph(0xE702)),
        new(WidgetTypes.Brightness, "Brightness", WidgetCategory.System,
            "Display brightness slider (laptop screens and DDC/CI monitors).", Glyph(0xE706)),
        new(WidgetTypes.RecycleBin, "Recycle Bin", WidgetCategory.Apps,
            "What the bin holds; open it or empty it.", Glyph(0xE74D)),

        // AI
        new(WidgetTypes.Claude, "Claude", WidgetCategory.AI,
            "Claude Code usage in the current 5-hour session, today and this week, read from local logs.", Glyph(0xE945)),
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
        WidgetPlacement.Create(WidgetTypes.Pulse),
        WidgetPlacement.Create(WidgetTypes.NowPlaying),
        WidgetPlacement.Create(WidgetTypes.System),
        WidgetPlacement.Create(WidgetTypes.Weather),
        WidgetPlacement.Create(WidgetTypes.ControlCenter),
        WidgetPlacement.Create(WidgetTypes.Calendar),
        WidgetPlacement.Create(WidgetTypes.Clock),
    ];
}
