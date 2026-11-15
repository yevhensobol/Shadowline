using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class TourReaderTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    private const string ValidStep = """
        {"id":"a","site":"s","time":"2026-11-01T00:00:00Z","windowHours":24,"focus":"sky",
         "title":{"en":"T","uk":"Т"},"text":{"en":"x","uk":"ікс"}}
        """;

    [Fact]
    public async Task Reads_a_valid_tour()
    {
        var document = await TourReader.ReadAsync(Json("{\"formatVersion\":1,\"steps\":[" + ValidStep + "]}"));

        var step = Assert.Single(document.Steps!);
        Assert.Equal("a", step.Id);
        Assert.Equal(24, step.WindowHours);
        Assert.Equal("ікс", step.Text!["uk"]);
    }

    [Fact]
    public async Task Missing_language_is_rejected()
    {
        var step = ValidStep.Replace("\"uk\":\"ікс\"", "\"de\":\"x\"");

        await Assert.ThrowsAsync<DataFormatException>(() => TourReader.ReadAsync(Json("{\"formatVersion\":1,\"steps\":[" + step + "]}")).AsTask());
    }

    [Fact]
    public async Task Duplicate_ids_and_empty_tours_are_rejected()
    {
        await Assert.ThrowsAsync<DataFormatException>(() => TourReader.ReadAsync(Json("{\"formatVersion\":1,\"steps\":[" + ValidStep + "," + ValidStep + "]}")).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => TourReader.ReadAsync(Json("{\"formatVersion\":1,\"steps\":[]}")).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => TourReader.ReadAsync(Json("{\"formatVersion\":2,\"steps\":[" + ValidStep + "]}")).AsTask());
    }

    [Fact]
    public async Task Unknown_layer_is_rejected()
    {
        var step = ValidStep.Replace("\"focus\":\"sky\"", "\"focus\":\"sky\",\"layer\":\"heat\"");

        await Assert.ThrowsAsync<DataFormatException>(() => TourReader.ReadAsync(Json("{\"formatVersion\":1,\"steps\":[" + step + "]}")).AsTask());
    }

    [Fact]
    public async Task Unknown_scene_is_rejected()
    {
        var step = ValidStep.Replace("\"focus\":\"sky\"", "\"focus\":\"sky\",\"scene\":\"moonwalk\"");

        await Assert.ThrowsAsync<DataFormatException>(() => TourReader.ReadAsync(Json("{\"formatVersion\":1,\"steps\":[" + step + "]}")).AsTask());
    }
}
