using NexaDock.Core.Layout;
using NexaDock.Core.Models;

namespace NexaDock.Tests;

public class DockPositionCalculatorTests
{
    private static readonly DockPositionCalculator Calc = new();

    private static MonitorInfo Monitor(int x = 0, int y = 0, int w = 2560, int h = 1440, int taskbar = 48, double scale = 1.0, bool primary = true, string name = @"\\.\DISPLAY1") =>
        new(name, new PixelRect(x, y, w, h), new PixelRect(x, y, w, h - taskbar), scale, primary);

    [Fact]
    public void Bottom_edge_is_centered_above_the_taskbar()
    {
        var settings = new DockSettings { Width = 800, EdgeOffset = 10, Size = DockSizePreset.Medium, Edge = DockEdge.Bottom };
        var r = Calc.Calculate(Monitor(), settings, requiredContentWidth: 0);

        Assert.Equal(800, r.Width);
        Assert.Equal(68, r.Height);
        Assert.Equal((2560 - 800) / 2, r.X);
        Assert.Equal(1392 - 10 - 68, r.Y); // work area bottom − offset − height
    }

    [Fact]
    public void Top_edge_sits_below_the_work_area_top()
    {
        var settings = new DockSettings { Width = 800, EdgeOffset = 12, Edge = DockEdge.Top };
        var r = Calc.Calculate(Monitor(), settings, 0);

        Assert.Equal(12, r.Y);
    }

    [Fact]
    public void Dimensions_scale_with_monitor_dpi()
    {
        var settings = new DockSettings { Width = 800, EdgeOffset = 10, Size = DockSizePreset.Medium };
        var r = Calc.Calculate(Monitor(w: 3840, h: 2160, taskbar: 72, scale: 1.5), settings, 0);

        Assert.Equal(1200, r.Width);
        Assert.Equal(102, r.Height);
        Assert.Equal((3840 - 1200) / 2, r.X);
        Assert.Equal(2088 - 15 - 102, r.Y);
    }

    [Fact]
    public void Width_grows_to_fit_required_content()
    {
        var settings = new DockSettings { Width = 400 };
        var r = Calc.Calculate(Monitor(), settings, requiredContentWidth: 950);

        Assert.Equal(950, r.Width);
    }

    [Fact]
    public void Width_is_clamped_to_the_work_area()
    {
        var settings = new DockSettings { Width = 2400 };
        var r = Calc.Calculate(Monitor(w: 1280, h: 720), settings, 0);

        Assert.Equal(1280 - 2 * (int)DockPositionCalculator.SideMargin, r.Width);
        Assert.True(r.X >= 0);
        Assert.True(r.Right <= 1280);
    }

    [Fact]
    public void Secondary_monitor_with_negative_origin_is_respected()
    {
        var settings = new DockSettings { Width = 600, EdgeOffset = 0 };
        var r = Calc.Calculate(Monitor(x: -1920, w: 1920, h: 1080, primary: false), settings, 0);

        Assert.Equal(-1920 + (1920 - 600) / 2, r.X);
        Assert.Equal(1032 - 68, r.Y);
    }

    [Fact]
    public void Window_padding_inflates_bounds_without_moving_the_glass()
    {
        var settings = new DockSettings { Width = 800, EdgeOffset = 10 };
        var plain = Calc.Calculate(Monitor(), settings, 0);
        var padded = Calc.Calculate(Monitor(), settings, 0, windowPadding: 16);

        Assert.Equal(plain.X - 16, padded.X);
        Assert.Equal(plain.Y - 16, padded.Y);
        Assert.Equal(plain.Width + 32, padded.Width);
        Assert.Equal(plain.Height + 32, padded.Height);
    }

    [Fact]
    public void Huge_offset_never_pushes_dock_off_screen()
    {
        var settings = new DockSettings { EdgeOffset = 64 };
        var r = Calc.Calculate(Monitor(w: 800, h: 100, taskbar: 0), settings, 0);

        Assert.True(r.Y >= 0);
        Assert.True(r.Bottom <= 100);
    }

    [Fact]
    public void Invalid_scale_falls_back_to_100_percent()
    {
        var settings = new DockSettings { Width = 800 };
        var r = Calc.Calculate(Monitor(scale: 0), settings, 0);

        Assert.Equal(800, r.Width);
    }

    [Theory]
    [InlineData(DockSizePreset.Small, 56)]
    [InlineData(DockSizePreset.Medium, 68)]
    [InlineData(DockSizePreset.Large, 84)]
    public void Size_presets_set_height(DockSizePreset preset, int expectedHeight)
    {
        var r = Calc.Calculate(Monitor(), new DockSettings { Size = preset }, 0);
        Assert.Equal(expectedHeight, r.Height);
    }

    [Fact]
    public void Required_width_increases_with_shortcuts_and_spacing()
    {
        var m = DockMetrics.For(DockSizePreset.Medium);
        Assert.True(m.RequiredWidth(8, 6) > m.RequiredWidth(4, 6));
        Assert.True(m.RequiredWidth(8, 12) > m.RequiredWidth(8, 6));
        Assert.Equal(m.RequiredWidth(0, 6), m.RequiredWidth(-3, -1));
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
}
