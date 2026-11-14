using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class SiteCatalogReaderTests
{
    [Fact]
    public async Task Sites_are_read_and_found_by_id()
    {
        var catalog = await SiteCatalogReader.ReadAsync(Json(Catalog(Site("a", -85.0, 10.0), Site("b", -88.0, 200.0))));

        Assert.Equal(2, catalog.Sites.Count);
        Assert.Equal("b", catalog.Find("b")?.Id);
        Assert.Null(catalog.Find("missing"));
    }

    [Fact]
    public async Task Synthetic_flag_defaults_to_false_and_is_read_when_present()
    {
        var plain = await SiteCatalogReader.ReadAsync(Json(Catalog(Site("a", -85.0, 10.0))));
        var flagged = await SiteCatalogReader.ReadAsync(Json("""{"synthetic":true,"sites":[]}"""));

        Assert.False(plain.IsSynthetic);
        Assert.True(flagged.IsSynthetic);
    }

    [Fact]
    public async Task Duplicate_ids_are_rejected()
    {
        var json = Catalog(Site("a", -85.0, 10.0), Site("a", -88.0, 20.0));

        await Assert.ThrowsAsync<DataFormatException>(() => SiteCatalogReader.ReadAsync(Json(json)).AsTask());
    }

    [Fact]
    public async Task Latitude_out_of_range_is_rejected()
    {
        var json = Catalog(Site("a", -91.0, 10.0));

        await Assert.ThrowsAsync<DataFormatException>(() => SiteCatalogReader.ReadAsync(Json(json)).AsTask());
    }

    private static string Site(string id, double latitude, double longitude) => $$"""
        { "id": "{{id}}", "name": "{{id}}", "mission": null, "latitudeDeg": {{latitude}}, "longitudeEastDeg": {{longitude}}, "elevationM": 0 }
        """;

    private static string Catalog(params string[] sites) => "{ \"sites\": [" + string.Join(",", sites) + "] }";

    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));
}
