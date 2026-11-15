using Shadowline.Core.Ml;

namespace Shadowline.Tests;

public class PolarScoreMapTests
{
    [Fact]
    public void Reads_the_cell_under_a_point_and_skips_outside_the_domain()
    {
        var size = 4;
        var bytes = new byte[12 + size * size];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), size);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), 200000f);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), -84f);
        for (var i = 0; i < size * size; i++)
        {
            bytes[12 + i] = (byte)(10 + i);
        }

        var map = PolarScoreMap.Parse(bytes);
        Assert.True(map.Percentile(-80, 0) is null);
        var top = map.Percentile(-87, 0);
        Assert.True(top is not null);
        Assert.True(Math.Abs(top!.Value - 16 / 255.0) < 1e-9);
    }

    [Fact]
    public void Shipped_map_scores_artemis_sites_high()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "pole_artemis.bin");
        var map = PolarScoreMap.Parse(File.ReadAllBytes(path));
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "artemis.json")));
        var scores = new List<double>();
        foreach (var region in json.RootElement.GetProperty("regions").EnumerateArray())
        {
            foreach (var site in region.GetProperty("sites").EnumerateArray())
            {
                if (map.Percentile(site.GetProperty("lat").GetDouble(), site.GetProperty("lon").GetDouble()) is { } p)
                {
                    scores.Add(p);
                }
            }
        }

        Assert.True(scores.Count > 100);
        scores.Sort();
        Assert.True(scores[scores.Count / 2] > 0.9);
    }
}
