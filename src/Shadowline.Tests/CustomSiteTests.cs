using Shadowline.Core.Sites;

namespace Shadowline.Tests;

public class CustomSiteTests
{
    [Fact]
    public void Created_site_round_trips_through_its_id()
    {
        var site = CustomSite.Create(-89.9, 12.34567);

        Assert.True(CustomSite.TryParse(site.Id, out var parsed));
        Assert.Equal(site.LatitudeDeg, parsed.LatitudeDeg);
        Assert.Equal(site.LongitudeEastDeg, parsed.LongitudeEastDeg);
        Assert.Equal(site.Id, parsed.Id);
    }

    [Fact]
    public void Out_of_range_coordinates_are_rejected()
    {
        Assert.False(CustomSite.TryParse("geo:95,10", out _));
        Assert.False(CustomSite.TryParse("geo:10", out _));
        Assert.False(CustomSite.TryParse("geo:a,b", out _));
        Assert.False(CustomSite.TryParse("apollo11-lm", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => CustomSite.Create(91, 0));
    }
}
