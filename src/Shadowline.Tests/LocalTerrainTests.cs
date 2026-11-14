using Shadowline.Core.Terrain;

namespace Shadowline.Tests;

public class LocalTerrainTests
{
    [Fact]
    public void Flat_ground_gives_a_horizon_just_below_zero()
    {
        var horizon = LocalTerrain.Horizon((_, _) => 0.0, 10, 20);

        Assert.True(horizon.ElevationAt(0) < 0 && horizon.ElevationAt(0) > -0.1);
        Assert.True(Math.Abs(horizon.ElevationAt(90) - horizon.ElevationAt(270)) < 1e-6);
    }

    [Fact]
    public void A_wall_to_the_east_raises_the_eastern_horizon()
    {
        double Sample(double lat, double lon) => lon > 20.2 ? 2000.0 : 0.0;

        var horizon = LocalTerrain.Horizon(Sample, 0, 20);

        Assert.True(horizon.ElevationAt(90) > 10);
        Assert.True(horizon.ElevationAt(270) < 0.1);
    }

    [Fact]
    public void Tile_has_the_terrain_header_and_a_zero_centre()
    {
        var bytes = LocalTerrain.Tile((lat, lon) => 100.0 * lat, 5, 5, 11, 1000);

        Assert.Equal(11, BitConverter.ToInt32(bytes, 0));
        Assert.Equal(1000f, BitConverter.ToSingle(bytes, 4));
        Assert.Equal(12 + 2 * 11 * 11, bytes.Length);
        Assert.Equal(0, BitConverter.ToInt16(bytes, 12 + 2 * (5 * 11 + 5)));
        Assert.True(BitConverter.ToInt16(bytes, 12 + 2 * (0 * 11 + 5)) > 0);
    }

    [Fact]
    public void Dem_samples_tiles_and_finds_the_tiles_it_needs()
    {
        var tile = new short[GlobalDem.TileSize * GlobalDem.TileSize];
        Array.Fill(tile, (short)1234);
        var dem = new GlobalDem(key => key.Row == 5 && key.Col == 13 ? tile : null);

        Assert.True(Math.Abs(dem.Sample(10, 20) - 1234) < 1e-6);
        Assert.True(Math.Abs(dem.Sample(-40, -100)) < 1e-6);
        var keys = GlobalDem.TilesAround(10, 20, 300000);
        Assert.True(keys.Contains(new DemTileKey(5, 13)));
        Assert.True(GlobalDem.TilesAround(-85, 0, 300000).Count >= GlobalDem.Cols);
    }
}
