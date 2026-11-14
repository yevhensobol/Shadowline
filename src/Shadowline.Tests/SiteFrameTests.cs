using Shadowline.Core.Geometry;
using Shadowline.Core.Sites;

namespace Shadowline.Tests;

public class SiteFrameTests
{
    private static SiteFrame Frame(double lat, double lon) => new(new Site("s", "s", null, lat, lon, 0));

    [Fact]
    public void Body_along_up_is_at_zenith()
    {
        var sky = Frame(0, 0).Observe(new Vec3(1e6, 0, 0));

        Assert.True(sky.ElevationDeg > 89.99);
    }

    [Fact]
    public void Body_along_north_at_equator_has_zero_azimuth_and_near_zero_elevation()
    {
        var sky = Frame(0, 0).Observe(new Vec3(Bodies.MoonRadiusKm, 0, 1e6));

        Assert.True(Math.Abs(sky.ElevationDeg) < 1e-6);
        Assert.True(Math.Abs(sky.AzimuthDeg) < 1e-6 || Math.Abs(sky.AzimuthDeg - 360.0) < 1e-6);
    }

    [Fact]
    public void Body_along_east_has_azimuth_ninety()
    {
        var sky = Frame(0, 0).Observe(new Vec3(Bodies.MoonRadiusKm, 1e6, 0));

        Assert.True(Math.Abs(sky.AzimuthDeg - 90.0) < 1e-6);
    }

    [Fact]
    public void Body_below_the_surface_has_negative_elevation()
    {
        var sky = Frame(0, 0).Observe(new Vec3(-1e6, 0, 0));

        Assert.True(sky.ElevationDeg < -89.99);
    }

    [Fact]
    public void Basis_vectors_are_orthonormal()
    {
        var frame = Frame(-85, 31);

        Assert.True(Math.Abs(frame.Up.Length - 1) < 1e-12);
        Assert.True(Math.Abs(frame.East.Length - 1) < 1e-12);
        Assert.True(Math.Abs(frame.North.Length - 1) < 1e-12);
        Assert.True(Math.Abs(Vec3.Dot(frame.Up, frame.East)) < 1e-12);
        Assert.True(Math.Abs(Vec3.Dot(frame.Up, frame.North)) < 1e-12);
        Assert.True(Math.Abs(Vec3.Dot(frame.East, frame.North)) < 1e-12);
    }
}
