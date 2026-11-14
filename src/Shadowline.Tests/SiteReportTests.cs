using Shadowline.Core.Comms;
using Shadowline.Core.Ephemeris;
using Shadowline.Core.Horizon;
using Shadowline.Core.Reports;
using Shadowline.Core.Sites;
using Shadowline.Core.Time;
using Shadowline.Core.Tracks;

namespace Shadowline.Tests;

public class SiteReportTests
{
    [Fact]
    public void Builds_a_report_with_intervals_and_verdicts()
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
        var dsn = new BitTrack(96).Not();

        var report = SiteReportBuilder.Build(site, "flat", HorizonProfile.Flat, ephemeris, tracks, dsn, 0, 96, "2026", "test", grid.Start);

        Assert.Equal(3, report.Verdicts.Count);
        Assert.True(report.Sun.SunlitShare is > 0.45 and < 0.55);
        Assert.Equal(1.0, report.Earth.DsnLinkShare);
        Assert.True(report.SunIntervals.Count is >= 1 and <= 2);
        Assert.Equal(72, report.Horizon.ElevationEvery5Deg.Count);
        Assert.True(report.Path.Count > 0);
        Assert.True(report.Sun.Sunrises + report.Sun.Sunsets is >= 1 and <= 2);
        Assert.True(report.Sun.MinIrradianceWm2 is > 1300 and < 1420);
        Assert.Equal(3, report.Earth.Stations.Count);
        Assert.True(report.Provenance.Count > 0);
        var series = report.Series!;
        Assert.Equal(series.SunElevationDeg.Count, series.SurfaceK.Count);
        Assert.Equal(series.SunElevationDeg.Count, series.TrackingW.Count);
        Assert.Equal(3, series.Battery.Count);
        Assert.True(series.TrackingW.Max() is > 200 and < 400);
        Assert.True(series.Battery.All(b => b.Soc.All(v => v is >= 0.2 - 1e-9 and <= 1 + 1e-9)));
    }

    [Fact]
    public void Provenance_points_to_known_references_and_strings()
    {
        foreach (var source in new[] { "polar", "global", "flat" })
        {
            foreach (var item in SiteReportBuilder.Provenance(source, "2026", new Site("x", "X", null, 0, 0, 0)))
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Method));
                Assert.True(item.Accuracy != "report.acc." + item.Quantity);
                Assert.True(Shadowline.Core.Localization.Strings.Get("en", "report.quantity." + item.Quantity) != "report.quantity." + item.Quantity);
                foreach (var id in item.References)
                {
                    Assert.True(References.Find(id) is not null);
                }
            }
        }

        Assert.Equal(References.All.Count, References.All.Select(r => r.Id).Distinct().Count());
        Assert.True(References.All.All(r => References.Link(r) is not null || r.Id == "wgs84"));
    }
}
