using System.IO.Compression;
using System.Text.Json;
using Shadowline.Core.Radio;
using Shadowline.Core.Terrain;
using Xunit.Abstractions;

namespace Shadowline.Tests;

public class MastViewshedTests(ITestOutputHelper output)
{
    private sealed record Reference(int Id, double X, double Y, double Los2, double Los10, double AreaKm2, double LunarrfLos2, double LunarrfLos10, double LunarrfAreaKm2);

    private static byte[] Gunzip(string path)
    {
        using var input = File.OpenRead(path);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var memory = new MemoryStream();
        gzip.CopyTo(memory);
        return memory.ToArray();
    }

    [Fact]
    public void Flat_ground_is_visible_to_the_radio_horizon()
    {
        var viewshed = MastViewshed.Compute(0, -100000, (_, _) => 0.0)!;

        Assert.Equal(1.0, viewshed.EvaFraction, 6);
        Assert.False(viewshed.Visible(0, MastViewshed.Steps - 1));
        var horizonM = Math.Sqrt(2 * LocalTerrain.MoonRadiusM * MastViewshed.MastM) + Math.Sqrt(2 * LocalTerrain.MoonRadiusM * MastViewshed.ReceiverM);
        var lastVisible = Enumerable.Range(0, MastViewshed.Steps).Last(step => viewshed.Visible(0, step));
        Assert.InRange(MastViewshed.DistanceM(lastVisible), horizonM - MastViewshed.StepM, horizonM + MastViewshed.StepM);
    }

    [Fact]
    public void A_wall_hides_the_ground_behind_it()
    {
        var viewshed = MastViewshed.Compute(0, -100000, (x, y) => Math.Abs(y + 100000 + 1000) < 50 ? 100.0 : 0.0)!;
        var north = 0;

        Assert.True(viewshed.Visible(north, 5));
        Assert.False(viewshed.Visible(north, 20));
    }

    [Fact]
    public void Tile_viewshed_matches_the_offline_reference()
    {
        var references = JsonSerializer.Deserialize<Reference[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "radio_reference.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var grid = PolarHorizonTests.Grid();
        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            var tiles = grid.TilesAround(reference.X, reference.Y, MastViewshed.RangeM + 1000).ToDictionary(key => key, key => PoleGrid.Decode(Gunzip(Path.Combine(AppContext.BaseDirectory, "data", "pole", "80", key.FileName)), grid.TilePx, grid.TilePx));
            var window = grid.Window(reference.X, reference.Y, MastViewshed.RangeM + 1000, key => tiles.GetValueOrDefault(key));

            var viewshed = MastViewshed.Compute(reference.X, reference.Y, window.Sample)!;

            output.WriteLine($"{reference.Id}: 2 km {viewshed.EvaFraction:F3} / {reference.Los2:F3} (LunarRF {reference.LunarrfLos2:F3}), 10 km {viewshed.NearFraction:F3} / {reference.Los10:F3}, area {viewshed.AreaKm2:F1} / {reference.AreaKm2:F1} km2");
            Assert.InRange(viewshed.EvaFraction - reference.Los2, -0.03, 0.03);
            Assert.InRange(viewshed.NearFraction - reference.Los10, -0.02, 0.02);
            Assert.InRange(viewshed.AreaKm2 - reference.AreaKm2, -0.05 * reference.AreaKm2 - 1, 0.05 * reference.AreaKm2 + 1);
        }
    }

    [Fact]
    public void Raster_is_north_up()
    {
        var viewshed = MastViewshed.Compute(0, -100000, (x, y) => y < -100000 - 1000 && Math.Abs(x) < 3000 ? 500.0 : 0.0)!;
        var raster = viewshed.Raster(110);

        Assert.Equal(1, raster[10 * 110 + 55]);
        Assert.Equal(2, raster[60 * 110 + 55]);
    }

    [Fact]
    public void Shaded_map_marks_visible_hidden_and_outside()
    {
        double? Sample(double x, double y) => y < -100000 - 1000 && Math.Abs(x) < 3000 ? 500.0 : 0.0;
        var viewshed = MastViewshed.Compute(0, -100000, Sample)!;
        var map = viewshed.ShadedMap(Sample, 110);

        Assert.Equal(MastViewshed.Outside, map[0]);
        Assert.True(map[60 * 110 + 55] >= MastViewshed.ShadeLevels);
        Assert.True(map[10 * 110 + 55] < MastViewshed.ShadeLevels);
    }
}
