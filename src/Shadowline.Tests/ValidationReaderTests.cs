using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class ValidationReaderTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Reads_a_valid_document()
    {
        var document = await ValidationReader.ReadAsync(Json("""
            {"formatVersion":1,"source":"JPL Horizons","fixtures":[
              {"siteId":"a","body":"sun","timeUtc":"2026-11-01T00:00:00Z","azimuthDeg":10.5,"elevationDeg":-2.25}]}
            """));

        var fixture = Assert.Single(document.Fixtures!);
        Assert.Equal("a", fixture.SiteId);
        Assert.Equal(10.5, fixture.AzimuthDeg);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), fixture.TimeUtc);
    }

    [Fact]
    public async Task Unsupported_version_is_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => ValidationReader.ReadAsync(Json("""{"formatVersion":9,"source":"x","fixtures":[]}""")).AsTask());
    }

    [Fact]
    public async Task Missing_fixtures_are_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => ValidationReader.ReadAsync(Json("""{"formatVersion":1,"source":"x"}""")).AsTask());
    }

    [Fact]
    public async Task Empty_site_is_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => ValidationReader.ReadAsync(Json("""
            {"formatVersion":1,"source":"x","fixtures":[{"siteId":"","body":"sun","timeUtc":"2026-11-01T00:00:00Z","azimuthDeg":0,"elevationDeg":0}]}
            """)).AsTask());
    }

    [Fact]
    public async Task Malformed_json_is_wrapped()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => ValidationReader.ReadAsync(Json("{not json")).AsTask());
    }
}
