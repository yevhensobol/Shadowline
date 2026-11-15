using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Power;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;

namespace Shadowline.Tests;

public class PowerModelTests
{
    private const float AuKm = 149_597_870.7f;

    private static readonly Site Equator = new("eq", "eq", null, 0, 0, 0);

    private static (BodyEphemeris Ephemeris, TimeGrid Grid) Constant(float sx, float sy, float sz, int count)
    {
        var grid = new TimeGrid(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), count);
        var values = new float[count * 6];
        for (var i = 0; i < count; i++)
        {
            values[i * 6] = sx;
            values[i * 6 + 1] = sy;
            values[i * 6 + 2] = sz;
            values[i * 6 + 3] = -384_400f;
        }

        return (new BodyEphemeris(grid, values), grid);
    }

    [Fact]
    public void Overhead_sun_gives_full_flux_on_a_flat_panel()
    {
        var (ephemeris, grid) = Constant(AuKm, 0, 0, 48);

        var estimate = PowerModel.Evaluate(new SiteFrame(Equator), ephemeris, HorizonProfile.Flat, grid, 0, 48, PowerOptions.Ideal);

        Assert.True(Math.Abs(estimate.HorizontalKWhPerDay - 1361 * 0.3 * 24 / 1000) < 0.02);
        Assert.True(estimate.VerticalKWhPerDay < 0.01);
        Assert.Equal(0, estimate.LongestDarkHours);
    }

    [Fact]
    public void Sun_on_the_horizon_is_half_visible_and_lights_a_vertical_panel()
    {
        var (ephemeris, grid) = Constant(0, AuKm, 0, 4);

        var estimate = PowerModel.Evaluate(new SiteFrame(Equator), ephemeris, HorizonProfile.Flat, grid, 0, 4, PowerOptions.Ideal);

        Assert.True(Math.Abs(estimate.PeakVerticalW - 1361 * 0.3 * 0.5) < 2);
        Assert.True(estimate.HorizontalKWhPerDay < 0.01);
    }

    [Fact]
    public void Sun_below_zero_but_above_a_low_ridge_horizon_still_gives_power()
    {
        var el = -2.0 * Math.PI / 180;
        var (ephemeris, grid) = Constant((float)(Math.Sin(el) * AuKm), (float)(Math.Cos(el) * AuKm), 0, 4);
        var ridge = new HorizonProfile(Enumerable.Repeat(-3f, HorizonProfile.SampleCount).ToArray());

        var estimate = PowerModel.Evaluate(new SiteFrame(Equator), ephemeris, ridge, grid, 0, 4, PowerOptions.Ideal);

        Assert.True(estimate.PeakVerticalW > 1361 * 0.3 * 0.99);
        Assert.True(estimate.HorizontalKWhPerDay < 0.01);
    }

    [Fact]
    public void Visible_sun_fraction_is_the_area_of_a_circular_segment()
    {
        Assert.Equal(0.5, PowerModel.VisibleFraction(0, 0, 0.25), 9);
        Assert.Equal(0.1955, PowerModel.VisibleFraction(-0.125, 0, 0.25), 3);
        Assert.Equal(1, PowerModel.VisibleFraction(1, 0, 0.25), 9);
        Assert.Equal(0, PowerModel.VisibleFraction(-1, 0, 0.25), 9);
        Assert.Equal(0.907, PowerModel.SystemFactorExplicit, 3);
    }

    [Fact]
    public void Earth_covers_the_right_share_of_the_sun_disc()
    {
        Assert.Equal(0, PowerModel.CoveredFraction(0.27, 0.95, 1.3), 9);
        Assert.Equal(1, PowerModel.CoveredFraction(0.27, 0.95, 0.5), 9);
        Assert.Equal(0.5, PowerModel.CoveredFraction(1, 1000, 1000), 2);
        var half = PowerModel.CoveredFraction(0.27, 0.95, 0.95);
        Assert.True(half > 0.45 && half < 0.55);
    }

    [Fact]
    public void Night_sets_the_battery_for_the_load()
    {
        var (ephemeris, grid) = Constant(-AuKm, 0, 0, 10);

        var estimate = PowerModel.Evaluate(new SiteFrame(Equator), ephemeris, HorizonProfile.Flat, grid, 0, 10, PowerOptions.Ideal with { LoadW = 200 });

        Assert.Equal(5, estimate.LongestDarkHours);
        Assert.True(Math.Abs(estimate.BatteryKWh - 1.0) < 1e-9);
        Assert.Equal(0, estimate.VerticalKWhPerDay);
    }

    [Fact]
    public void Hot_cells_lose_efficiency()
    {
        var options = PowerOptions.Default;
        var cell = PowerModel.CellTemperatureK(1361, 250, options);
        Assert.True(cell is > 290 and < 345);
        Assert.True(PowerModel.CellEfficiency(cell, options) < options.Efficiency);
        Assert.True(Math.Abs(PowerModel.CellEfficiency(273.15 + 50, options) - 0.281) < 0.002);
        Assert.True(Math.Abs(PowerModel.CellEfficiency(273.15 + 120, options) - 0.221) < 0.002);
    }

    [Fact]
    public void Fixed_panel_sees_less_than_a_tracking_one_when_the_sun_circles()
    {
        var pole = new Site("p", "p", null, -89.9, 0, 0);
        var grid = new TimeGrid(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), 1440);
        var values = new float[1440 * 6];
        for (var i = 0; i < 1440; i++)
        {
            var a = i / 1440.0 * 2 * Math.PI;
            values[i * 6] = (float)(AuKm * Math.Cos(a));
            values[i * 6 + 1] = (float)(AuKm * Math.Sin(a));
            values[i * 6 + 2] = (float)(AuKm * Math.Sin(-0.02));
            values[i * 6 + 3] = -384_400f;
        }

        var mounts = PowerModel.CompareMounts(new SiteFrame(pole), new BodyEphemeris(grid, values), HorizonProfile.Flat, 0, 1440);
        var tracking = mounts[0].KWhPerDay;
        var bestFixed = mounts[1].KWhPerDay;
        Assert.True(tracking > 0);
        Assert.True(bestFixed < tracking * 0.45);
        Assert.True(mounts[3].KWhPerDay < tracking * 0.45);
    }
}
