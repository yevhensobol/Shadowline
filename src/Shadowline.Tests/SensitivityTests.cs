using Shadowline.Core.Ephemeris;
using Shadowline.Core.Horizon;
using Shadowline.Core.Metrics;
using Shadowline.Core.Sensitivity;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class SensitivityTests
{
    private static BodyEphemeris Ephemeris(int count)
    {
        var grid = new TimeGrid(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(30), count);
        var values = new float[count * BodyEphemeris.FloatsPerRow];
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / 480;
            values[i * 6 + 0] = (float)(1.5e8 * Math.Cos(angle));
            values[i * 6 + 1] = (float)(1.5e8 * Math.Sin(angle));
            values[i * 6 + 2] = 0;
            values[i * 6 + 3] = 384400;
            values[i * 6 + 4] = 0;
            values[i * 6 + 5] = 0;
        }

        return new BodyEphemeris(grid, values);
    }

    [Fact]
    public void Zero_shift_matches_direct_evaluation()
    {
        var ephemeris = Ephemeris(48 * 20);
        var site = new Site("s", "s", null, 0, 0, 0);
        var tracks = SkyTracks.Build(site, ephemeris, HorizonProfile.Flat);

        var points = SensitivityAnalyzer.ByStartShift(tracks, ephemeris.Grid, 48 * 5, 48, [0]);

        Assert.True(points.Count == 1);
        Assert.Equal(WindowEvaluator.Evaluate(tracks, ephemeris.Grid, 48 * 5, 48).SunFraction, points[0].Metrics.SunFraction);
    }

    [Fact]
    public void Shifts_outside_the_grid_are_skipped()
    {
        var ephemeris = Ephemeris(48 * 10);
        var site = new Site("s", "s", null, 0, 0, 0);
        var tracks = SkyTracks.Build(site, ephemeris, HorizonProfile.Flat);

        var points = SensitivityAnalyzer.ByStartShift(tracks, ephemeris.Grid, 48, 48 * 2, [-3, -1, 0, 1, 20]);

        Assert.True(points.Select(point => point.Offset).SequenceEqual(new double[] { -1, 0, 1 }));
    }

    [Fact]
    public void Zero_position_offset_matches_the_base_site()
    {
        var ephemeris = Ephemeris(48 * 10);
        var site = new Site("s", "s", null, -80, 20, 0);
        var window = 48 * 5;
        var baseTracks = SkyTracks.Build(site, ephemeris, HorizonProfile.Flat);

        var points = SensitivityAnalyzer.ByPosition(site, ephemeris, HorizonProfile.Flat, 0, window, [0], true);

        Assert.True(points.Count == 1);
        Assert.Equal(WindowEvaluator.Evaluate(baseTracks, ephemeris.Grid, 0, window).SunFraction, points[0].Metrics.SunFraction);
    }

    [Fact]
    public void Offsets_past_the_pole_cross_to_the_far_side_on_a_great_circle()
    {
        var ephemeris = Ephemeris(48 * 3);
        var site = new Site("s", "s", null, -89.9, 0, 0);

        var points = SensitivityAnalyzer.ByPosition(site, ephemeris, HorizonProfile.Flat, 0, 48, [-10, 0, 10], true);
        var across = SensitivityAnalyzer.Shifted(site, -10, true);
        var east = SensitivityAnalyzer.Shifted(site, 10, false);

        Assert.Equal(3, points.Count);
        Assert.True(Math.Abs(across.LatitudeDeg + 89.77) < 0.02 && Math.Abs(Math.Abs(across.LongitudeEastDeg) - 180) < 0.01);
        Assert.True(Math.Abs(east.LongitudeEastDeg - 90) < 30 && east.LatitudeDeg > -89.9);
    }
}
