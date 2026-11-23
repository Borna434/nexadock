using NexaDock.Core.Models;
using NexaDock.Core.Settings;
using NexaDock.Core.Widgets;

namespace NexaDock.Tests;

public class WidgetLayoutTests
{
    [Fact]
    public void Catalog_type_ids_are_unique_and_lookup_is_case_insensitive()
    {
        Assert.Equal(WidgetCatalog.All.Count, WidgetCatalog.All.Select(d => d.TypeId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(WidgetTypes.Clock, WidgetCatalog.Get("CLOCK")!.TypeId);
        Assert.Null(WidgetCatalog.Get("does-not-exist"));
    }

    [Fact]
    public void Default_layout_is_valid_and_contains_apps()
    {
        var layout = WidgetCatalog.DefaultLayout();
        var normalized = SettingsNormalizer.Normalize(new DockSettings { Widgets = layout }).Widgets;

        Assert.Equal(layout, normalized);
        Assert.Contains(layout, w => w.TypeId == WidgetTypes.Apps);
        Assert.Equal(layout.Count, layout.Select(w => w.Id).Distinct().Count());
    }

    [Fact]
    public void Unknown_widget_types_are_dropped()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings
        {
            Widgets = [new WidgetPlacement("a", "clock"), new WidgetPlacement("b", "teleporter")],
        });

        Assert.Single(s.Widgets);
    }

    [Fact]
    public void Single_instance_widgets_are_deduplicated_but_spacers_may_repeat()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings
        {
            Widgets =
            [
                new WidgetPlacement("1", "clock"),
                new WidgetPlacement("2", "Clock"),
                new WidgetPlacement("3", "spacer"),
                new WidgetPlacement("4", "spacer"),
            ],
        });

        Assert.Equal(["clock", "spacer", "spacer"], s.Widgets.Select(w => w.TypeId));
    }

    [Fact]
    public void Duplicate_or_missing_widget_ids_are_regenerated()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings
        {
            Widgets = [new WidgetPlacement("x", "spacer"), new WidgetPlacement("x", "spacer"), new WidgetPlacement("", "clock")],
        });

        Assert.Equal(3, s.Widgets.Select(w => w.Id).Distinct().Count());
        Assert.All(s.Widgets, w => Assert.False(string.IsNullOrWhiteSpace(w.Id)));
    }

    [Fact]
    public void Widget_count_is_capped()
    {
        var many = Enumerable.Range(0, 100).Select(i => new WidgetPlacement($"s{i}", "spacer")).ToList();
        Assert.Equal(SettingsLimits.MaxWidgets, SettingsNormalizer.Normalize(new DockSettings { Widgets = many }).Widgets.Count);
    }

    [Fact]
    public void Weather_city_is_trimmed_and_limited()
    {
        var s = SettingsNormalizer.Normalize(new DockSettings { WeatherCity = "  Split  " });
        Assert.Equal("Split", s.WeatherCity);

        var longCity = SettingsNormalizer.Normalize(new DockSettings { WeatherCity = new string('a', 500) });
        Assert.Equal(SettingsLimits.MaxCityLength, longCity.WeatherCity.Length);
    }

    [Fact]
    public void Old_theme_names_from_phase_1_fall_back_to_default()
    {
        var s = SettingsSerializer.Deserialize("""{ "Theme": "Frost", "Size": "Large" }""")!;
        Assert.Equal(DockSettings.Default.Theme, s.Theme);
        Assert.Equal(DockSizePreset.Large, s.Size);
    }

    [Theory]
    [InlineData(0, true, WeatherKind.Sun)]
    [InlineData(0, false, WeatherKind.Moon)]
    [InlineData(2, true, WeatherKind.SunCloud)]
    [InlineData(63, true, WeatherKind.Rain)]
    [InlineData(73, true, WeatherKind.Snow)]
    [InlineData(95, true, WeatherKind.Storm)]
    [InlineData(45, true, WeatherKind.Fog)]
    [InlineData(12345, true, WeatherKind.Cloud)]
    public void Weather_codes_map_to_icon_kinds(int code, bool isDay, WeatherKind expected) =>
        Assert.Equal(expected, WeatherCodes.Kind(code, isDay));
}
