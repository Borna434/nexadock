using NexaDock.Core.Models;
using NexaDock.Core.Widgets;

namespace NexaDock.Core.Layout;

/// <summary>
/// The widget layout of a bar. The main bar (<c>display</c> null) uses
/// <see cref="DockSettings.Widgets"/>, which follows the active profile; the extra bar on a
/// secondary display has its own layout in <see cref="DockSettings.DisplayLayouts"/>.
/// </summary>
public static class BarLayouts
{
    public static IReadOnlyList<WidgetPlacement> Get(DockSettings s, string? display)
    {
        if (display is null)
        {
            return s.Widgets;
        }

        return s.DisplayLayouts.FirstOrDefault(l => string.Equals(l.DeviceName, display, StringComparison.OrdinalIgnoreCase))?.Widgets
            ?? WidgetCatalog.SecondaryLayout(display);
    }

    public static DockSettings With(DockSettings s, string? display, IReadOnlyList<WidgetPlacement> widgets)
    {
        if (display is null)
        {
            return s with { Widgets = widgets, WidgetsInitialized = true };
        }

        var layouts = s.DisplayLayouts
            .Where(l => !string.Equals(l.DeviceName, display, StringComparison.OrdinalIgnoreCase))
            .Append(new DisplayLayout(display, widgets))
            .ToList();
        return s with { DisplayLayouts = layouts };
    }
}
