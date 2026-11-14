using Shadowline.Core.Sites;

namespace Shadowline.Tests;

public class SiteDistanceTests
{
    private static Site At(double lat, double lon) => new("s", "s", null, lat, lon, 0);

    [Fact]
    public void Same_point_is_zero()
    {
        Assert.True(SiteDistance.Km(At(-85, 31), At(-85, 31)) < 1e-6);
    }

    [Fact]
    public void Quarter_of_a_meridian_is_a_quarter_of_the_circumference()
    {
        var expected = Math.PI / 2 * 1737.4;

        Assert.True(Math.Abs(SiteDistance.Km(At(0, 0), At(90, 0)) - expected) < 1e-6);
    }

    [Fact]
    public void Longitudes_across_the_dateline_are_handled()
    {
        var near = SiteDistance.Km(At(0, 359), At(0, 1));

        Assert.True(near < 100);
    }
}
