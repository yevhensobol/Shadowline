using System.Text;
using Shadowline.Core.Ephemeris;
using Shadowline.Core.Geometry;
using Shadowline.Core.Horizon;
using Shadowline.Core.Incidents;
using Shadowline.Data;

namespace Shadowline.Tests;

public class IncidentsReaderTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    private const string Doc = """
        {"formatVersion":1,"incidents":[{"id":"x","site":"slim","kind":"tipped","landingUtc":"2024-01-19T15:20:00Z","gridStartUtc":"2024-01-15T00:00:00Z","gridStepMinutes":30,"gridCount":480,
        "observations":[{"type":"noPower","fromUtc":"2024-01-19T15:20:00Z","toUtc":"2024-01-19T17:57:00Z","use":"fit","note":"n","sources":["a"]}]}],
        "sources":{"a":{"title":"A","url":"https://example.org"}}}
        """;

    [Fact]
    public async Task Reads_incidents_and_maps_observations()
    {
        var document = await IncidentsReader.ReadAsync(Json(Doc));

        var item = Assert.Single(document.Incidents!);
        var observation = item.Observations![0].ToCore();
        Assert.Equal(ObservationKind.NoPower, observation.Kind);
        Assert.True(observation.Fit);
        Assert.False(observation.Check);
    }

    [Fact]
    public async Task Unknown_sources_and_uses_are_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => IncidentsReader.ReadAsync(Json(Doc.Replace("[\"a\"]", "[\"b\"]"))).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => IncidentsReader.ReadAsync(Json(Doc.Replace("\"use\":\"fit\"", "\"use\":\"maybe\""))).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => IncidentsReader.ReadAsync(Json(Doc.Replace("2024-01-19T15:20:00Z\",\"gridStartUtc", "2024-03-19T15:20:00Z\",\"gridStartUtc"))).AsTask());
    }

    [Fact]
    public async Task Shipped_slim_case_points_the_panel_west_and_predicts_held_out_contacts()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        await using var stream = File.OpenRead(Path.Combine(data, "incidents.json"));
        var document = await IncidentsReader.ReadAsync(stream);
        await using var catalogStream = File.OpenRead(Path.Combine(data, "catalog.json"));
        var catalog = await SiteCatalogReader.ReadAsync(catalogStream);
        var slim = document.Incidents!.Single(i => i.Id == "slim");
        foreach (var item in document.Incidents!)
        {
            Assert.True(catalog.Find(item.Site) is not null);
            Assert.Equal(item.GridCount * 24, new FileInfo(Path.Combine(data, "incidents", item.Id + ".bin")).Length);
        }

        var ephemeris = EphemerisReader.Read(await File.ReadAllBytesAsync(Path.Combine(data, "incidents", "slim.bin")), slim.Grid);
        var horizon = HorizonReader.Read(await File.ReadAllBytesAsync(Path.Combine(data, "horizon", "slim.bin")));
        var track = SunTrack.Build(new SiteFrame(catalog.Find("slim")!), ephemeris, horizon);
        var result = AttitudeInference.Run(track, slim.Observations!.Select(o => o.ToCore()).ToList(), InferencePrior.Sphere, 6000);

        Assert.True(result.Azimuth!.Median >= 250 && result.Azimuth!.Median <= 300);
        var march = result.Scores.Single(s => s.Observation.Kind == ObservationKind.Power && s.Observation.FromUtc.Month == 3);
        Assert.True(march.Share > 0.9);
        Assert.True(slim.Observations!.All(o => o.Use != "check" || o.Type != "operations"));
        Assert.True(slim.Observations!.Count(o => o.Use == "fit") == 2);
    }
}
