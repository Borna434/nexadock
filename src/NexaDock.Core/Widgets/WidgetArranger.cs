using NexaDock.Core.Models;

namespace NexaDock.Core.Widgets;

/// <summary>
/// A sensible order for the bar around the centred Apps widget:
/// <list type="bullet">
/// <item>Left of the apps: what you glance at or work with (weather, calendar, Claude,
/// productivity). Now Playing changes width as tracks change, so it sits nearest the apps.</item>
/// <item>Right of the apps: system status ending in the clock, like the Windows taskbar. Pulse
/// comes and goes, so it sits nearest the apps where it moves nothing else.</item>
/// </list>
/// The user can still drag tiles anywhere; this is only used for new layouts, for
/// "Arrange widgets" and to place a newly added widget.
/// </summary>
public static class WidgetArranger
{
    private enum Side
    {
        Left,
        Center,
        Right,
    }

    private static readonly string[] LeftOrder =
    [
        WidgetTypes.Weather,
        WidgetTypes.Agenda,
        WidgetTypes.Claude,
        WidgetTypes.Focus,
        WidgetTypes.Timer,
        WidgetTypes.Stopwatch,
        WidgetTypes.Todos,
        WidgetTypes.Notes,
        WidgetTypes.Clipboard,
        WidgetTypes.DayProgress,
        WidgetTypes.NowPlaying,
    ];

    private static readonly string[] RightOrder =
    [
        WidgetTypes.Pulse,
        WidgetTypes.System,
        WidgetTypes.Network,
        WidgetTypes.Storage,
        WidgetTypes.Tray,
        WidgetTypes.Keyboard,
        WidgetTypes.QuickToggles,
        WidgetTypes.Brightness,
        WidgetTypes.Volume,
        WidgetTypes.Microphone,
        WidgetTypes.ControlCenter,
        WidgetTypes.RecycleBin,
        WidgetTypes.Calendar,
        WidgetTypes.Clock,
        WidgetTypes.ShowDesktop,
    ];

    /// <summary>Unknown (future) types go on the right, before Control Center.</summary>
    private const double UnknownRank = 8.5;

    private static (Side Side, double Rank) Slot(string typeId)
    {
        if (string.Equals(typeId, WidgetTypes.Apps, StringComparison.OrdinalIgnoreCase))
        {
            return (Side.Center, 0);
        }

        var left = Array.FindIndex(LeftOrder, t => string.Equals(t, typeId, StringComparison.OrdinalIgnoreCase));
        if (left >= 0)
        {
            return (Side.Left, left);
        }

        var right = Array.FindIndex(RightOrder, t => string.Equals(t, typeId, StringComparison.OrdinalIgnoreCase));
        return (Side.Right, right >= 0 ? right : UnknownRank);
    }

    /// <summary>
    /// Reorders a layout: left widgets, Apps, right widgets. Flexible spaces are dropped (the
    /// centred layout doesn't need them), and Day Number is dropped when the Clock already shows
    /// the date. Without Apps, one flexible space separates the two sides instead.
    /// </summary>
    public static IReadOnlyList<WidgetPlacement> Arrange(IReadOnlyList<WidgetPlacement> widgets)
    {
        var hasClock = widgets.Any(w => Is(w, WidgetTypes.Clock));
        var kept = widgets
            .Where(w => !Is(w, WidgetTypes.Spacer))
            .Where(w => !(hasClock && Is(w, WidgetTypes.Calendar)))
            .Select((w, i) => (w, i, slot: Slot(w.TypeId)))
            .ToList();

        var left = kept.Where(x => x.slot.Side == Side.Left).OrderBy(x => x.slot.Rank).ThenBy(x => x.i).Select(x => x.w);
        var center = kept.Where(x => x.slot.Side == Side.Center).OrderBy(x => x.i).Select(x => x.w).ToList();
        var right = kept.Where(x => x.slot.Side == Side.Right).OrderBy(x => x.slot.Rank).ThenBy(x => x.i).Select(x => x.w).ToList();

        var result = left.ToList();
        if (center.Count > 0)
        {
            result.AddRange(center);
        }
        else if (right.Count > 0 && result.Count > 0)
        {
            result.Add(WidgetPlacement.Create(WidgetTypes.Spacer));
        }

        result.AddRange(right);
        return result;
    }

    /// <summary>Where a newly added widget of this type fits into an existing layout.</summary>
    public static int InsertIndex(IReadOnlyList<WidgetPlacement> widgets, string typeId)
    {
        var (side, rank) = Slot(typeId);
        var appsIndex = IndexOf(widgets, WidgetTypes.Apps);

        if (side == Side.Left)
        {
            // Before the first left-side widget that ranks after it, else just before the apps.
            var end = appsIndex >= 0 ? appsIndex : widgets.Count;
            for (var i = 0; i < end; i++)
            {
                var s = Slot(widgets[i].TypeId);
                if (s.Side == Side.Left && s.Rank > rank)
                {
                    return i;
                }
            }

            return end;
        }

        if (side == Side.Center)
        {
            for (var i = 0; i < widgets.Count; i++)
            {
                if (Slot(widgets[i].TypeId).Side == Side.Right)
                {
                    return i;
                }
            }

            return widgets.Count;
        }

        // Right side: before the first right-side widget (after the apps) that ranks after it.
        for (var i = appsIndex + 1; i < widgets.Count; i++)
        {
            var s = Slot(widgets[i].TypeId);
            if (s.Side == Side.Right && s.Rank > rank)
            {
                return i;
            }
        }

        return widgets.Count;
    }

    private static int IndexOf(IReadOnlyList<WidgetPlacement> widgets, string typeId)
    {
        for (var i = 0; i < widgets.Count; i++)
        {
            if (Is(widgets[i], typeId))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Is(WidgetPlacement w, string typeId) => string.Equals(w.TypeId, typeId, StringComparison.OrdinalIgnoreCase);
}
