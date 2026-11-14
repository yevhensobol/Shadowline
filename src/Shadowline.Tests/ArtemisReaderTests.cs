using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class ArtemisReaderTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    private const string Region = """
        {"code":"MMO","name":"Mons Mouton","artemis3":true,"lat":-85.4,"lon":31.2,"sites":[{"name":"MMO01","lat":-85.416,"lon":31.85}]}
        """;

    [Fact]
    public async Task Reads_regions()
    {
        var document = await ArtemisReader.ReadAsync(Json("{\"formatVersion\":1,\"regions\":[" + Region + "]}"));

        var region = Assert.Single(document.Regions!);
        Assert.True(region.Artemis3);
        Assert.Equal("MMO01", Assert.Single(region.Sites!).Name);
    }

    [Fact]
    public async Task Non_polar_or_empty_regions_are_rejected()
    {
        var north = Region.Replace("\"lat\":-85.4", "\"lat\":10");
        var empty = Region.Replace("[{\"name\":\"MMO01\",\"lat\":-85.416,\"lon\":31.85}]", "[]");

        await Assert.ThrowsAsync<DataFormatException>(() => ArtemisReader.ReadAsync(Json("{\"formatVersion\":1,\"regions\":[" + north + "]}")).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => ArtemisReader.ReadAsync(Json("{\"formatVersion\":1,\"regions\":[" + empty + "]}")).AsTask());
        await Assert.ThrowsAsync<DataFormatException>(() => ArtemisReader.ReadAsync(Json("{\"formatVersion\":1,\"regions\":[" + Region + "," + Region + "]}")).AsTask());
    }
}
