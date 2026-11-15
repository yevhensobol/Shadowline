using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Thermal;

namespace Shadowline.Tests;

public class ThermalModelTests
{
    private const double AuKm = 149_597_870.7;

    private static SkySnapshot Sky(double elevationDeg) =>
        new(new SkyPosition(180, elevationDeg, AuKm), new SkyPosition(0, 10, 384_400), 0.2666, 0.95);

    [Fact]
    public void Overhead_sun_gives_about_386_kelvin()
    {
        var t = ThermalModel.SurfaceK(Sky(90), HorizonProfile.Flat, 0);

        Assert.True(Math.Abs(t - 386) < 3);
    }

    [Fact]
    public void Low_polar_sun_is_cold_and_night_uses_the_floor()
    {
        var day = ThermalModel.SurfaceK(Sky(2), HorizonProfile.Flat, -85);
        var night = ThermalModel.SurfaceK(Sky(-5), HorizonProfile.Flat, 0);

        Assert.True(day is > 155 and < 175);
        Assert.Equal(ThermalModel.EquatorNightK, night);
        Assert.True(ThermalModel.NightK(-89.9) >= ThermalModel.MinNightK);
    }
}
