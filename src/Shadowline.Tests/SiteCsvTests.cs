using Shadowline.Core.Ephemeris;
using Shadowline.Core.Horizon;
using Shadowline.Core.Reports;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class SiteCsvTests
{
    [Fact]
    public void Writes_one_row_per_step_in_the_window()
    {
        var grid = new TimeGrid(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), 3);
        var values = new float[]
        {
            149_597_870f, 0, 0, 384_400f, 0, 0,
            -149_597_870f, 0, 0, -384_400f, 0, 0,
            149_597_870f, 0, 0, 384_400f, 0, 0,
        };
        var ephemeris = new BodyEphemeris(grid, values);
        var site = new Site("eq", "eq", null, 0, 0, 0);
        var tracks = SkyTracks.Build(site, ephemeris, HorizonProfile.Flat);

        var lines = SiteCsv.Build(site, ephemeris, HorizonProfile.Flat, tracks, grid, 1, 5).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal(SiteCsv.Header, lines[0]);
        Assert.True(lines[1].StartsWith("2026-11-01T00:30:00Z,", StringComparison.Ordinal));
        Assert.Equal("0", lines[1].Split(',')[4]);
        Assert.Equal("0", lines[1].Split(',')[8]);
        Assert.True(lines[2].StartsWith("2026-11-01T01:00:00Z,", StringComparison.Ordinal));
        Assert.Equal("1", lines[2].Split(',')[4]);
        Assert.Equal("90.000", lines[2].Split(',')[2]);
    }
}
