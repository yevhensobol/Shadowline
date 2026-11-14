using System.Text;
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
}
