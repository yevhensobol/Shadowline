using Shadowline.Core.Thermal;

namespace Shadowline.Tests;

public class DivinerPolarMapTests
{
    [Fact]
    public void Shipped_map_shows_a_cold_trap_in_shackleton_and_a_warm_ridge()
    {
        var map = DivinerPolarMap.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "data", "pole_diviner.bin")));
        var floor = map.At(-89.66, 129.2);
        Assert.True(floor is not null);
        Assert.True(floor!.AnnualMaxK < 130);
        Assert.True(floor.IceDepthM is not null);
        var lowLatitude = map.At(-80.5, 0);
        Assert.True(lowLatitude is not null);
        Assert.True(lowLatitude!.AnnualMaxK > 200);
        Assert.True(map.At(-50, 0) is null);
    }
}
