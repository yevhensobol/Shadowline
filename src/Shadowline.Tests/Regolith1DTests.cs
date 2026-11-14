using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Sites;
using Shadowline.Core.Thermal;
using Shadowline.Core.Time;

namespace Shadowline.Tests;

public class Regolith1DTests
{
    private const double AuKm = 149_597_870.7;

    private static BodyEphemeris Rotating(int count, double stepMinutes)
    {
        var grid = new TimeGrid(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(stepMinutes), count);
        var values = new float[count * 6];
        for (var i = 0; i < count; i++)
        {
            var a = i * stepMinutes * 60 / Regolith1D.SynodicSeconds * 2 * Math.PI;
            values[i * 6] = (float)(AuKm * Math.Cos(a));
            values[i * 6 + 1] = (float)(AuKm * Math.Sin(a));
            values[i * 6 + 3] = 384_400f;
        }

        return new BodyEphemeris(grid, values);
    }

    [Fact]
    public void Heat_capacity_matches_hayne_table()
    {
        Assert.True(Math.Abs(Regolith1D.HeatCapacity(250) - 671.8) < 1.0);
        Assert.True(Regolith1D.HeatCapacity(100) is > 250 and < 350);
    }

    [Fact]
    public void Equator_reaches_diviner_like_noon_and_predawn_temperatures()
    {
        var ephemeris = Rotating(1418 * 2, 30);
        var site = new Site("eq", "eq", null, 0, 0, 0);
        var result = Regolith1D.Run(new SiteFrame(site), ephemeris, HorizonProfile.Flat, 0, 1418);
        Assert.True(result.MaxK is > 370 and < 400);
        Assert.True(result.MinK is > 85 and < 110);
        Assert.True(result.SubsurfaceK is > 230 and < 270);
    }

    [Fact]
    public void A_point_without_sun_settles_at_the_geothermal_equilibrium()
    {
        var count = 17520;
        var grid = new TimeGrid(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), count);
        var values = new float[count * 6];
        for (var i = 0; i < count; i++)
        {
            values[i * 6] = (float)-AuKm;
            values[i * 6 + 4] = 384_400f;
        }

        var frame = new SiteFrame(new Site("dark", "dark", null, 0, 0, 0));
        var result = Regolith1D.Run(frame, new BodyEphemeris(grid, values), HorizonProfile.Flat, 0, count);
        var equilibrium = Math.Pow(RegolithParameters.Hayne2017.GeothermalFlux / (RegolithParameters.Hayne2017.Emissivity * 5.670374419e-8), 0.25);
        Assert.True(Math.Abs(result.MaxK - equilibrium) < 1.5);
    }

    [Fact]
    public void Short_spin_up_is_close_to_a_long_one()
    {
        var ephemeris = Rotating(1418 * 2, 30);
        var frame = new SiteFrame(new Site("eq", "eq", null, 0, 0, 0));
        var quick = Regolith1D.Run(frame, ephemeris, HorizonProfile.Flat, 0, 1418);
        var slow = Regolith1D.Run(frame, ephemeris, HorizonProfile.Flat, 0, 1418, null, 60);
        Assert.True(Math.Abs(quick.MinK - slow.MinK) < 1.0);
        Assert.True(Math.Abs(quick.MaxK - slow.MaxK) < 0.5);
        Assert.True(Math.Abs(quick.SubsurfaceK - slow.SubsurfaceK) < 5);
    }
}
