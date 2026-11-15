using Shadowline.Core.Geometry;

namespace Shadowline.Tests;

public class MapProjectionTests
{
    [Fact]
    public void Equirectangular_places_origin_in_the_center_and_round_trips()
    {
        var projection = new EquirectangularProjection(2048, 1024);

        var center = projection.Project(0, 0);
        Assert.Equal(1024, center.X, 6);
        Assert.Equal(512, center.Y, 6);

        var point = projection.Project(-43.3, -11.4);
        var back = projection.Unproject(point.X, point.Y);
        Assert.Equal(-43.3, back.LatitudeDeg, 6);
        Assert.Equal(-11.4, back.LongitudeEastDeg, 6);
    }

    [Fact]
    public void Equirectangular_wraps_longitude()
    {
        var projection = new EquirectangularProjection(360, 180);

        Assert.Equal(projection.Project(10, -170).X, projection.Project(10, 190).X, 6);
    }

    [Fact]
    public void South_polar_puts_the_pole_in_the_center_and_the_limit_on_the_rim()
    {
        var projection = new SouthPolarProjection(500, -60);

        var pole = projection.Project(-90, 0);
        Assert.Equal(500, pole.X, 6);
        Assert.Equal(500, pole.Y, 6);

        var rim = projection.Project(-60, 0);
        Assert.Equal(500, rim.X, 6);
        Assert.Equal(0, rim.Y, 6);
    }

    [Fact]
    public void South_polar_puts_east_longitude_to_the_right_and_round_trips()
    {
        var projection = new SouthPolarProjection(500, -60);

        var east = projection.Project(-75, 90);
        Assert.True(east.X > 500);
        Assert.Equal(500, east.Y, 6);

        var point = projection.Project(-83.2, 123.4);
        var back = projection.Unproject(point.X, point.Y);
        Assert.Equal(-83.2, back.LatitudeDeg, 6);
        Assert.Equal(123.4, back.LongitudeEastDeg, 6);
    }

    [Fact]
    public void Sub_point_of_a_direction_and_terminator_are_consistent()
    {
        var sun = SubPoint.Of(new Vec3(0, 1, 0));
        Assert.Equal(0, sun.LatitudeDeg, 6);
        Assert.Equal(90, sun.LongitudeEastDeg, 6);

        var axis = sun.ToUnit();
        foreach (var point in sun.Terminator(36))
        {
            Assert.Equal(0.0, Vec3.Dot(axis, point.ToUnit()), 6);
        }
    }
}
