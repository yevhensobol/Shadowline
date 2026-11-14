using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class HistoryReaderTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    private const string Event = """
        {"id":"apollo11","site":"apollo11-lm","landingUtc":"1969-07-20T20:17:40Z","gridStartUtc":"1969-07-17T20:00:00Z","gridStepMinutes":30,"gridCount":1344,"title":{"en":"A","uk":"А"}}
        """;

    [Fact]
    public async Task Reads_an_event_and_its_grid()
    {
        var document = await HistoryReader.ReadAsync(Json("{\"formatVersion\":1,\"events\":[" + Event + "]}"));

        var item = Assert.Single(document.Events!);
        Assert.Equal(1344, item.Grid.Count);
        Assert.Equal(144, item.Grid.IndexOf(item.LandingUtc));
    }

    [Fact]
    public async Task Landing_outside_the_grid_and_unsafe_ids_are_rejected()
    {
        var late = Event.Replace("1969-07-20T20:17:40Z", "1969-09-20T20:17:40Z");
        var unsafeId = Event.Replace("\"id\":\"apollo11\"", "\"id\":\"../x\"");

        await Assert.ThrowsAsync<DataFormatException>(() => HistoryReader.ReadAsync(Json("{\"formatVersion\":1,\"events\":[" + late + "]}")).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => HistoryReader.ReadAsync(Json("{\"formatVersion\":1,\"events\":[" + unsafeId + "]}")).AsTask());
    }
}
