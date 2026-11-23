using NexaDock.Core.Layout;
using NexaDock.Core.Models;
using NexaDock.Core.Settings;
using NexaDock.Core.Widgets;

namespace NexaDock.Tests;

public class DisplayLayoutTests
{
    private const string Second = @"\\.\DISPLAY2";

    [Fact]
    public void A_display_without_its_own_layout_gets_apps_and_clock()
    {
        var types = BarLayouts.Get(new DockSettings(), Second).Select(w => w.TypeId);

        Assert.Equal([WidgetTypes.Apps, WidgetTypes.Clock], types);
    }

    [Fact]
    public void Default_secondary_layout_has_stable_ids()
    {
        Assert.Equal(WidgetCatalog.SecondaryLayout(Second), WidgetCatalog.SecondaryLayout(Second));
    }

    [Fact]
    public void Editing_an_extra_bar_leaves_the_main_bar_alone()
    {
        var s = new DockSettings { Widgets = [WidgetPlacement.Create(WidgetTypes.Weather)] };
        var layout = new[] { WidgetPlacement.Create(WidgetTypes.Apps), WidgetPlacement.Create(WidgetTypes.Volume) };

        var changed = BarLayouts.With(s, Second, layout);

        Assert.Equal(s.Widgets, changed.Widgets);
        Assert.Equal(layout, BarLayouts.Get(changed, Second));
        Assert.Equal(layout, BarLayouts.Get(changed, @"\\.\display2")); // device names ignore case
        Assert.Single(BarLayouts.With(changed, Second, layout).DisplayLayouts);
    }

    [Fact]
    public void Display_layouts_and_the_switch_survive_saving()
    {
        var s = BarLayouts.With(new DockSettings { ShowOnAllDisplays = false }, Second,
            [WidgetPlacement.Create(WidgetTypes.Apps), WidgetPlacement.Create(WidgetTypes.Clock)]);

        var json = SettingsSerializer.Serialize(s);
        var back = SettingsSerializer.Deserialize(json)!;

        Assert.False(back.ShowOnAllDisplays);
        Assert.Equal(BarLayouts.Get(SettingsNormalizer.Normalize(s), Second), BarLayouts.Get(back, Second));
    }

    [Fact]
    public void Old_settings_show_bars_on_every_display()
    {
        Assert.True(SettingsSerializer.Deserialize("{\"SchemaVersion\":3}")!.ShowOnAllDisplays);
    }
}
