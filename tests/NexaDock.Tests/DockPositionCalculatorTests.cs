using NexaDock.Core.Abstractions;
using NexaDock.Core.Layout;
using NexaDock.Core.Models;

namespace NexaDock.Tests;

public class DockPositionCalculatorTests
{
    private static readonly DockPositionCalculator Calc = new();

    private static MonitorInfo Monitor(int x = 0, int y = 0, int w = 2560, int h = 1440, int taskbar = 48, double scale = 1.0, bool primary = true, string name = @"\\.\DISPLAY1") =>
        new(name, new PixelRect(x, y, w, h), new PixelRect(x, y, w, h - taskbar), scale, primary);

    [Fact]
    public void Full_width_bar_spans_the_work_area_minus_margins()
    {
        var settings = new DockSettings { FullWidth = true, EdgeOffset = 8, Size = DockSizePreset.Medium };
        var r = Calc.Calculate(Monitor(), settings, contentWidth: 300);

        Assert.Equal(2560 - 16, r.Width);
        Assert.Equal(8, r.X);
        Assert.Equal(56, r.Height);
        Assert.Equal(1392 - 8 - 56, r.Y);
    }

    [Fact]
    public void Flush_bar_with_zero_offset_touches_the_edges()
    {
        var settings = new DockSettings { FullWidth = true, EdgeOffset = 0 };
        var r = Calc.Calculate(Monitor(), settings, 0);

        Assert.Equal(new PixelRect(0, 1392 - 56, 2560, 56), r);
    }

    [Fact]
    public void Compact_bar_fits_content_and_is_centered()
    {
        var settings = new DockSettings { FullWidth = false, EdgeOffset = 10 };
        var r = Calc.Calculate(Monitor(), settings, contentWidth: 900);

        Assert.Equal(900, r.Width);
        Assert.Equal((2560 - 900) / 2, r.X);
    }

    [Fact]
    public void Compact_bar_is_clamped_to_the_area()
    {
        var settings = new DockSettings { FullWidth = false, EdgeOffset = 10 };
        var r = Calc.Calculate(Monitor(w: 1280, h: 720), settings, contentWidth: 5000);

        Assert.Equal(1280 - 20, r.Width);
        Assert.Equal(10, r.X);
    }

    [Fact]
    public void Top_edge_sits_below_the_area_top()
    {
        var settings = new DockSettings { EdgeOffset = 12, Edge = DockEdge.Top };
        var r = Calc.Calculate(Monitor(), settings, 0);

        Assert.Equal(12, r.Y);
    }

    [Fact]
    public void Dimensions_scale_with_monitor_dpi()
    {
        var settings = new DockSettings { FullWidth = false, EdgeOffset = 10, Size = DockSizePreset.Medium };
        var r = Calc.Calculate(Monitor(w: 3840, h: 2160, taskbar: 72, scale: 1.5), settings, contentWidth: 800);

        Assert.Equal(1200, r.Width);
        Assert.Equal(84, r.Height);
        Assert.Equal((3840 - 1200) / 2, r.X);
        Assert.Equal(2088 - 15 - 84, r.Y);
    }

    [Fact]
    public void Monitor_bounds_are_used_when_the_taskbar_is_replaced()
    {
        var settings = new DockSettings { EdgeOffset = 0 };
        var r = Calc.Calculate(Monitor(), settings, 0, new PlacementOptions(UseMonitorBounds: true));

        Assert.Equal(1440 - 56, r.Y);
    }

    [Fact]
    public void Secondary_monitor_with_negative_origin_is_respected()
    {
        var settings = new DockSettings { FullWidth = false, EdgeOffset = 0 };
        var r = Calc.Calculate(Monitor(x: -1920, w: 1920, h: 1080, primary: false), settings, contentWidth: 600);

        Assert.Equal(-1920 + (1920 - 600) / 2, r.X);
        Assert.Equal(1032 - 56, r.Y);
    }

    [Fact]
    public void Window_padding_inflates_bounds_without_moving_the_glass()
    {
        var settings = new DockSettings { EdgeOffset = 10 };
        var plain = Calc.Calculate(Monitor(), settings, 0);
        var padded = Calc.Calculate(Monitor(), settings, 0, new PlacementOptions(WindowPadding: 16));

        Assert.Equal(plain.X - 16, padded.X);
        Assert.Equal(plain.Y - 16, padded.Y);
        Assert.Equal(plain.Width + 32, padded.Width);
        Assert.Equal(plain.Height + 32, padded.Height);
    }

    [Fact]
    public void Huge_offset_never_pushes_bar_off_screen()
    {
        var settings = new DockSettings { EdgeOffset = 48 };
        var r = Calc.Calculate(Monitor(w: 800, h: 80, taskbar: 0), settings, 0);

        Assert.True(r.Y >= 0);
        Assert.True(r.Bottom <= 80);
    }

    [Fact]
    public void Invalid_scale_falls_back_to_100_percent()
    {
        var settings = new DockSettings { FullWidth = false };
        var r = Calc.Calculate(Monitor(scale: 0), settings, contentWidth: 800);

        Assert.Equal(800, r.Width);
    }

    [Theory]
    [InlineData(DockSizePreset.Small, 48)]
    [InlineData(DockSizePreset.Medium, 56)]
    [InlineData(DockSizePreset.Large, 66)]
    public void Size_presets_set_height(DockSizePreset preset, int expectedHeight)
    {
        var r = Calc.Calculate(Monitor(), new DockSettings { Size = preset }, 0);
        Assert.Equal(expectedHeight, r.Height);
    }

    [Fact]
    public void Reserved_strip_ends_at_the_top_of_the_glass()
    {
        var settings = new DockSettings { EdgeOffset = 8, Edge = DockEdge.Bottom };
        var monitor = Monitor(scale: 1.25);
        var strip = DockPositionCalculator.ReservedStrip(monitor, settings);

        Assert.Equal((int)Math.Round((56 + 8) * 1.25), strip.Height);

        // Maximised windows stop exactly where the glass starts (no gap between window and bar).
        var bar = new DockPositionCalculator().Calculate(monitor, settings, 400, new PlacementOptions(0, UseMonitorBounds: true));
        Assert.Equal(bar.Y, strip.Y);
        Assert.Equal(1440, strip.Bottom);
        Assert.Equal(2560, strip.Width);

        var top = DockPositionCalculator.ReservedStrip(Monitor(), settings with { Edge = DockEdge.Top });
        Assert.Equal(0, top.Y);
    }
}

public class MonitorSelectorTests
{
    private static MonitorInfo M(string name, bool primary) =>
        new(name, new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 90), 1, primary);

    [Fact]
    public void Returns_preferred_monitor_when_present()
    {
        var list = new[] { M(@"\\.\DISPLAY1", true), M(@"\\.\DISPLAY2", false) };
        Assert.Equal(@"\\.\DISPLAY2", MonitorSelector.Select(list, @"\\.\display2")!.DeviceName);
    }

    [Fact]
    public void Falls_back_to_primary_when_preferred_monitor_disappears()
    {
        var list = new[] { M(@"\\.\DISPLAY3", false), M(@"\\.\DISPLAY1", true) };
        Assert.Equal(@"\\.\DISPLAY1", MonitorSelector.Select(list, @"\\.\DISPLAY2")!.DeviceName);
    }

    [Fact]
    public void Null_preference_means_primary()
    {
        var list = new[] { M(@"\\.\DISPLAY2", false), M(@"\\.\DISPLAY1", true) };
        Assert.True(MonitorSelector.Select(list, null)!.IsPrimary);
    }

    [Fact]
    public void Uses_first_monitor_when_none_is_primary()
    {
        var list = new[] { M("A", false), M("B", false) };
        Assert.Equal("A", MonitorSelector.Select(list, "missing")!.DeviceName);
    }

    [Fact]
    public void Returns_null_for_no_monitors()
    {
        Assert.Null(MonitorSelector.Select([], "x"));
        Assert.Null(MonitorSelector.Select(null, null));
    }

    [Fact]
    public void Finds_monitor_containing_a_point()
    {
        var left = new MonitorInfo("L", new PixelRect(-1920, 0, 1920, 1080), new PixelRect(-1920, 0, 1920, 1040), 1, false);
        var main = new MonitorInfo("M", new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1, true);

        Assert.Equal("L", MonitorSelector.FromPoint([left, main], -100, 500)!.DeviceName);
        Assert.Equal("M", MonitorSelector.FromPoint([left, main], 100, 500)!.DeviceName);
        Assert.Equal("M", MonitorSelector.FromPoint([left, main], 99999, 99999)!.DeviceName); // nearest → primary
    }
}
