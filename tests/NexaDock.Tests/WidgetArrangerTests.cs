using NexaDock.Core.Models;
using NexaDock.Core.Widgets;

namespace NexaDock.Tests;

public class WidgetArrangerTests
{
    private static List<WidgetPlacement> Layout(params string[] types) => types.Select(WidgetPlacement.Create).ToList();

    private static string[] Types(IEnumerable<WidgetPlacement> widgets) => widgets.Select(w => w.TypeId).ToArray();

    [Fact]
    public void Old_left_aligned_layout_becomes_left_apps_right()
    {
        // The layout from before Phase 4 (and the one on the user's bar).
        var old = Layout("apps", "spacer", "nowplaying", "system", "weather", "controlcenter", "calendar", "clock",
                         "claude", "notes", "pulse", "recyclebin");

        var arranged = WidgetArranger.Arrange(old);

        Assert.Equal(
            ["weather", "claude", "notes", "nowplaying", "apps", "pulse", "system", "controlcenter", "recyclebin", "clock"],
            Types(arranged));
    }

    [Fact]
    public void Day_number_is_kept_without_a_clock_and_ids_survive()
    {
        var old = Layout("calendar", "apps", "weather");
        var arranged = WidgetArranger.Arrange(old);

        Assert.Equal(["weather", "apps", "calendar"], Types(arranged));
        Assert.Equal(old.Select(w => w.Id).Order(), arranged.Select(w => w.Id).Order());
    }

    [Fact]
    public void Arranging_twice_changes_nothing()
    {
        var once = WidgetArranger.Arrange(Layout("clock", "focus", "apps", "volume", "weather", "pulse"));
        Assert.Equal(Types(once), Types(WidgetArranger.Arrange(once)));
    }

    [Fact]
    public void Without_apps_a_space_separates_the_sides()
    {
        Assert.Equal(["weather", "spacer", "clock"], Types(WidgetArranger.Arrange(Layout("clock", "weather"))));
    }

    [Theory]
    [InlineData("todos", 2)]     // left side, after Claude, before Now Playing
    [InlineData("volume", 5)]    // right side, before Control Center
    [InlineData("showdesktop", 7)] // after the clock, at the far right
    public void New_widgets_land_in_their_logical_place(string type, int expected)
    {
        var layout = Layout("weather", "claude", "nowplaying", "apps", "system", "controlcenter", "clock");
        Assert.Equal(expected, WidgetArranger.InsertIndex(layout, type));
    }

    [Fact]
    public void Default_layout_is_centred_with_widgets_on_both_sides()
    {
        var types = Types(WidgetCatalog.DefaultLayout());
        var apps = Array.IndexOf(types, "apps");

        Assert.True(apps > 0 && apps < types.Length - 1);
        Assert.DoesNotContain("spacer", types);
        Assert.Equal("clock", types[^1]);
    }
}
