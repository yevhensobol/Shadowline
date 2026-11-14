using Shadowline.Core.Ephemeris;
using Shadowline.Core.Horizon;
using Shadowline.Core.Reports;
using Shadowline.Core.Sites;
using Shadowline.Core.Terrain;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class MissionAnalysisTests
{
    private static byte[] PatchBytes(int size, float spacing, Func<double, double, double> height)
    {
        var bytes = new byte[12 + size * size * 2];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 4), size);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), spacing);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 4), 0.5f);
        var half = (size - 1) / 2.0;
        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                var units = (short)Math.Round(height((col - half) * spacing, (half - row) * spacing) / 0.5);
                BitConverter.TryWriteBytes(bytes.AsSpan(12 + 2 * (row * size + col), 2), units);
            }
        }

        return bytes;
    }

    [Fact]
    public void Plane_has_its_slope_everywhere()
    {
        var patch = TerrainPatch.Parse(PatchBytes(41, 100, (east, north) => east * Math.Tan(10 * Math.PI / 180)));
        var stats = patch.Slopes(1000, 15);
        Assert.True(Math.Abs(stats.AtSiteDeg - 10) < 0.2);
        Assert.True(Math.Abs(stats.MeanDeg - 10) < 0.2);
        Assert.Equal(0.0, stats.SteepShare);
    }

    [Fact]
    public void Wall_to_the_east_raises_the_east_horizon()
    {
        var patch = TerrainPatch.Parse(PatchBytes(81, 100, (east, north) => east > 1000 ? 500 : 0));
        var horizon = patch.Horizon(0, 0, 0);
        Assert.True(horizon.ElevationAt(90) > 20);
        Assert.True(horizon.ElevationAt(270) < 1);
    }

    [Fact]
    public void Battery_runs_empty_when_load_exceeds_supply()
    {
        var panel = Enumerable.Repeat(0.0, 48).ToArray();
        var battery = MissionAnalysis.Battery(1, 100, panel, 0.5);
        Assert.Equal(0.0, battery.MinStateOfCharge);
        Assert.True(battery.HoursToEmpty is > 9.9 and < 10.6);
        var sunny = MissionAnalysis.Battery(1, 100, Enumerable.Repeat(200.0, 48).ToArray(), 0.5);
        Assert.Equal(1.0, sunny.MinStateOfCharge);
        Assert.True(sunny.HoursToEmpty is null);
        var real = MissionAnalysis.Battery(1, 100, panel, 0.5, Shadowline.Core.Power.PowerOptions.Default);
        Assert.True(real.HoursToEmpty is > 7.4 and < 8.1);
        Assert.True(Math.Abs(real.MinStateOfCharge - 0.2) < 1e-6);
    }

    [Fact]
    public void Analysis_reports_touchdown_and_changes()
    {
        var grid = new TimeGrid(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), 96);
        var values = new float[96 * 6];
        for (var i = 0; i < 96; i++)
        {
            var angle = i / 96.0 * 2 * Math.PI;
            values[i * 6] = (float)(149_597_870 * Math.Cos(angle));
            values[i * 6 + 1] = (float)(149_597_870 * Math.Sin(angle));
            values[i * 6 + 3] = 384_400f;
        }

        var ephemeris = new BodyEphemeris(grid, values);
        var site = new Site("eq", "Equator", null, 0, 0, 0);
        var tracks = SkyTracks.Build(site, ephemeris, HorizonProfile.Flat);
        var mission = MissionAnalysis.Analyze(site, HorizonProfile.Flat, ephemeris, tracks, new BitTrack(96).Not(), 0, 96, 100, null, null);
        Assert.Equal(3, mission.Changes.Count);
        Assert.True(mission.Changes[0].HoursToChange is > 0);
        Assert.Equal(MissionAnalysis.BatteryKWh.Length, mission.Batteries.Count);
        Assert.Equal(3, mission.Landing.Shadows.Count);
        Assert.True(mission.Slope is null);
    }

    [Fact]
    public void Battery_series_drains_to_the_floor_and_recharges()
    {
        var panel = Enumerable.Repeat(0.0, 100).Concat(Enumerable.Repeat(300.0, 100)).ToArray();
        var soc = MissionAnalysis.BatterySeries(5, 100, panel, 0.5, Shadowline.Core.Power.PowerOptions.Default);
        Assert.Equal(200, soc.Length);
        Assert.Equal(1.0, soc[0], 6);
        Assert.Equal(1 - 100 * 0.5 / 0.95 / 5000, soc[1], 6);
        Assert.Equal(0.2, soc[99], 6);
        Assert.Equal(0.2 + 200 * 0.95 * 0.5 / 5000, soc[101], 6);
        Assert.Equal(1.0, soc[199], 6);
    }
}
