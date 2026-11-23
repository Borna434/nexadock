using NexaDock.Core.Layout;

namespace NexaDock.Tests;

public class BarLayoutTests
{
    private static BarSlot F(double w) => new(w, BarSlotKind.Fixed);

    private static BarSlot Apps(double w) => new(w, BarSlotKind.Center);

    private static readonly BarSlot Space = new(BarLayout.MinFlexibleWidth, BarSlotKind.Flexible);

    [Theory]
    [InlineData(200)]
    [InlineData(420)]
    [InlineData(90)]
    public void Apps_stay_in_the_middle_whatever_their_width(double appsWidth)
    {
        var slots = new[] { F(100), Apps(appsWidth), F(80), F(60) };

        var p = BarLayout.Arrange(slots, 1000, 6);

        Assert.Equal((1000 - appsWidth) / 2, p[1].X, 3);
    }

    [Fact]
    public void Tiles_before_apps_start_at_the_left_and_tiles_after_end_at_the_right()
    {
        var slots = new[] { F(100), F(50), Apps(200), F(80), F(60) };

        var p = BarLayout.Arrange(slots, 1000, 6);

        Assert.Equal(0, p[0].X);
        Assert.Equal(106, p[1].X);
        Assert.Equal(1000, p[4].X + p[4].Width, 3);
        Assert.Equal(p[4].X - 6 - 80, p[3].X, 3);
        Assert.DoesNotContain(p, x => x.Hidden);
    }

    [Fact]
    public void A_flexible_space_on_the_right_side_pushes_the_tiles_after_it_to_the_edge()
    {
        // The old default layout (Apps, Space, widgets) still reads well: widgets end at the right edge.
        var slots = new[] { Apps(300), Space, F(80), F(60) };

        var p = BarLayout.Arrange(slots, 1000, 6);

        Assert.Equal(350, p[0].X, 3);
        Assert.Equal(1000, p[3].X + p[3].Width, 3);
    }

    [Fact]
    public void A_long_side_moves_the_apps_over_just_enough()
    {
        var slots = new[] { Apps(200), F(300), F(300), F(100) };

        var p = BarLayout.Arrange(slots, 1000, 6);

        // Right side needs 700 + 3 gaps = 718, so the apps end at 1000 - 718.
        Assert.Equal(1000 - 718 - 200, p[0].X, 3);
        Assert.True(p[1].X >= p[0].X + p[0].Width + 6 - 0.001);
        Assert.DoesNotContain(p, x => x.Hidden);
    }

    [Fact]
    public void When_nothing_fits_the_outermost_tiles_overflow_first_and_apps_never()
    {
        var slots = new[] { F(200), Apps(300), F(200), F(200), F(200) };

        var p = BarLayout.Arrange(slots, 800, 6, endReserve: 36);

        Assert.False(p[1].Hidden);
        Assert.True(p[4].Hidden);
        Assert.True(p[3].Hidden);
        Assert.False(p[2].Hidden);
        Assert.False(p[0].Hidden);
        Assert.True(p[2].X + p[2].Width <= 800 - 36 + 0.001);
    }

    [Fact]
    public void Without_apps_tiles_start_at_the_left_as_before()
    {
        var slots = new[] { F(100), Space, F(50) };

        var p = BarLayout.Arrange(slots, 500, 6);

        Assert.Equal(0, p[0].X);
        Assert.Equal(500, p[2].X + p[2].Width, 3);
    }

    [Fact]
    public void Overflow_without_apps_hides_everything_after_the_first_misfit()
    {
        var slots = new[] { F(100), F(100), F(100) };

        var p = BarLayout.Arrange(slots, 250, 6);

        Assert.False(p[0].Hidden);
        Assert.False(p[1].Hidden);
        Assert.True(p[2].Hidden);
    }
}
