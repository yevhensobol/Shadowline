using System.IO.Compression;
using System.Text.Json;
using Shadowline.Core.Horizon;
using Shadowline.Core.Terrain;
using Shadowline.Data;
using Xunit.Abstractions;

namespace Shadowline.Tests;

public class PolarHorizonTests(ITestOutputHelper output)
{
    private static string DataPath(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "data", .. parts]);

    private static byte[] Gunzip(string path)
    {
        using var input = File.OpenRead(path);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var memory = new MemoryStream();
        gzip.CopyTo(memory);
        return memory.ToArray();
    }

    internal static PoleGrid Grid()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DataPath("pole", "pole.json")));
        var root = document.RootElement;
        var present = root.GetProperty("present").EnumerateArray().Select(item => new PoleTileKey(item[0].GetInt32(), item[1].GetInt32()));
        return new PoleGrid(root.GetProperty("sizePx").GetInt32(), root.GetProperty("spacingM").GetDouble(), root.GetProperty("halfExtentM").GetDouble(), root.GetProperty("tilePx").GetInt32(), root.GetProperty("unitM").GetDouble(), present);
    }

    [Fact]
    public void Delta_decoding_restores_heights()
    {
        short[] heights = [100, 102, 105, -32000, 32000, 7, 0, -1, 30000];
        var deltas = new byte[heights.Length * 2];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                var i = r * 3 + c;
                var value = heights[i] - (c > 0 ? heights[i - 1] : 0) - (r > 0 ? heights[i - 3] : 0) + (r > 0 && c > 0 ? heights[i - 4] : 0);
                var d = unchecked((short)value);
                deltas[2 * i] = (byte)(d & 0xFF);
                deltas[2 * i + 1] = (byte)((d >> 8) & 0xFF);
            }
        }

        Assert.Equal(heights, PoleGrid.Decode(deltas, 3, 3));
    }

    [Fact]
    public void Projection_round_trips()
    {
        var (x, y) = PolarDem.Project(-84.7906, 29.1957);
        var (lat, lon) = PolarDem.Unproject(x, y);

        Assert.Equal(-84.7906, lat, 6);
        Assert.Equal(29.1957, lon, 6);
    }

    [Theory]
    [InlineData("im2-athena", -84.7906, 29.1957)]
    [InlineData("plan-bluemoon1", -89.45, -137.3)]
    [InlineData("plan-change7", -88.8, 123.4)]
    public void Tile_horizon_matches_the_offline_80_m_horizon(string siteId, double lat, double lon)
    {
        var grid = Grid();
        var (x, y) = PolarDem.Project(lat, lon);
        var tiles = grid.TilesAround(x, y, PolarHorizon.NearRangeM).ToDictionary(key => key, key => PoleGrid.Decode(Gunzip(DataPath("pole", "80", key.FileName)), grid.TilePx, grid.TilePx));
        var window = grid.Window(x, y, PolarHorizon.NearRangeM, key => tiles.GetValueOrDefault(key));
        var coarse = PolarDem.ParseDelta(Gunzip(DataPath("pole", "coarse.bin.gz")))!;
        var cache = new Dictionary<DemTileKey, short[]?>();
        var global = new GlobalDem(key => cache.TryGetValue(key, out var hit) ? hit : cache[key] = LoadGlobal(key));
        var reference = HorizonReader.Read(File.ReadAllBytes(DataPath("horizon", siteId + ".bin")));

        var computed = PolarHorizon.Compute(lat, lon, window, coarse, global.Sample);

        var diffs = Enumerable.Range(0, HorizonProfile.SampleCount).Select(i => Math.Abs(computed.ElevationAt(i * HorizonProfile.StepDeg) - reference.ElevationAt(i * HorizonProfile.StepDeg))).ToArray();
        output.WriteLine($"{siteId}: mean {diffs.Average():F3} max {diffs.Max():F3}");
        Assert.True(diffs.Average() < 0.05);
        Assert.True(diffs.Max() < 0.2);
    }

    private static short[]? LoadGlobal(DemTileKey key)
    {
        var path = DataPath("dem", key.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        var values = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
