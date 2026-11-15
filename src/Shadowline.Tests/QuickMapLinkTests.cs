using Shadowline.Core.Sites;

namespace Shadowline.Tests;

public class QuickMapLinkTests
{
    [Fact]
    public void Polar_sites_use_polar_stereographic()
    {
        Assert.Equal(27, QuickMapLink.Projection(-84.79));
        Assert.Equal(26, QuickMapLink.Projection(80));
        Assert.Equal(16, QuickMapLink.Projection(-57.9));
    }

    [Fact]
    public void Link_is_centred_on_the_site()
    {
        var link = QuickMapLink.For(-57.865, 61.36);
        var extent = link.Split("extent=")[1].Split('&')[0].Split(',').Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        Assert.StartsWith(QuickMapLink.Base, link);
        Assert.EndsWith("&proj=16", link);
        Assert.Equal(61.36, (extent[0] + extent[2]) / 2, 4);
        Assert.Equal(-57.865, (extent[1] + extent[3]) / 2, 4);
    }

    [Fact]
    public void Longitude_is_wrapped_to_plus_minus_180()
    {
        var link = QuickMapLink.For(60.4445, 355.412);

        Assert.Contains("extent=-4.", link);
    }
}
