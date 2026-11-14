using Shadowline.Core.Geometry;

namespace Shadowline.Tests;

public class MoonOrientationTests
{
    [Fact]
    public void Earth_direction_matches_the_shipped_MOON_ME_ephemeris()
    {
        var earthJ2000 = new Vec3(182430.898, -284569.096, -137319.784);
        var expected = new Vec3(363372, 28692, -16715).Normalized();

        var actual = MoonOrientation.FromJ2000(earthJ2000, new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero)).Normalized();

        var angle = Math.Acos(Math.Clamp(Vec3.Dot(actual, expected), -1, 1)) * 180 / Math.PI;
        Assert.True(angle < 0.006, angle.ToString());
    }

    [Fact]
    public void Ra_dec_gives_unit_vectors()
    {
        var pole = MoonOrientation.RaDec(0, 90);

        Assert.True(Math.Abs(pole.Z - 1) < 1e-12);
        Assert.True(Math.Abs(MoonOrientation.RaDec(123, -45).Length - 1) < 1e-12);
    }
}
