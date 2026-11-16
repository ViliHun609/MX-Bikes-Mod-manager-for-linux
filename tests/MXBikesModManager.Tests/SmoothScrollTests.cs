using MXBikesModManager.Behaviors;
using MXBikesModManager.Services;
using Xunit;

namespace MXBikesModManager.Tests;

public class SmoothScrollTests
{
    [Theory]
    [InlineData(-40, 100, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(42, 100, 42)]
    [InlineData(100, 100, 100)]
    [InlineData(180, 100, 100)]
    [InlineData(50, -10, 0)]
    public void Target_is_clamped_to_the_scrollable_range(double value, double maximum, double expected)
    {
        Assert.Equal(expected, SmoothScrollMath.Clamp(value, maximum));
    }

    [Fact]
    public void A_step_moves_toward_the_target_without_overshooting()
    {
        var first = SmoothScrollMath.Step(0, 100, 0.25);
        var second = SmoothScrollMath.Step(first, 100, 0.25);

        Assert.Equal(25, first, 3);
        Assert.Equal(43.75, second, 3);
        Assert.True(second > first && second < 100);
    }

    [Fact]
    public void Close_enough_snaps_the_last_fraction()
    {
        Assert.True(SmoothScrollMath.IsClose(100, 100.4));
        Assert.True(SmoothScrollMath.IsClose(100.6, 100));
        Assert.False(SmoothScrollMath.IsClose(100, 101));
    }

    [Fact]
    public void Wheel_step_scales_with_the_line_height_but_has_a_minimum()
    {
        Assert.Equal(64, SmoothScrollMath.WheelStep(0));
        Assert.Equal(64, SmoothScrollMath.WheelStep(16));
        Assert.Equal(120, SmoothScrollMath.WheelStep(40));
    }

    [Fact]
    public void Smooth_scrolling_is_enabled_by_default()
    {
        var config = ConfigService.CreateDefault();

        Assert.True(config.Ui.SmoothScrolling);
    }
}
