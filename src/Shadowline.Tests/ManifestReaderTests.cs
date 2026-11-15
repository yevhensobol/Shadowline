using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class ManifestReaderTests
{
    [Fact]
    public async Task Valid_manifest_is_read()
    {
        var manifest = await ManifestReader.ReadAsync(Json(Valid(1, "synthetic")));

        Assert.Equal("synthetic", manifest.Kind);
        Assert.Equal(48, manifest.Grid.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), manifest.Grid.Step);
    }

    [Fact]
    public async Task Unsupported_format_version_is_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => ManifestReader.ReadAsync(Json(Valid(2, "synthetic"))).AsTask());
    }

    [Fact]
    public async Task Unknown_kind_is_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => ManifestReader.ReadAsync(Json(Valid(1, "other"))).AsTask());
    }

    [Fact]
    public async Task Malformed_json_is_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => ManifestReader.ReadAsync(Json("{")).AsTask());
    }

    private static string Valid(int version, string kind) => $$"""
        {
          "formatVersion": {{version}},
          "kind": "{{kind}}",
          "builtAtUtc": "2026-11-02T00:00:00Z",
          "gridStartUtc": "2026-11-01T00:00:00Z",
          "gridStepMinutes": 30,
          "gridCount": 48,
          "sources": []
        }
        """;

    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));
}
