using System.Text;
using Shadowline.Data;

namespace Shadowline.Tests;

public class PhotosReaderTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    private const string Valid = """
        {"site":"a","file":"img/sites/a.jpg","source":"https://example.org","credit":"C","caption":{"en":"x","uk":"ікс"}}
        """;

    [Fact]
    public async Task Reads_and_finds_photos()
    {
        var document = await PhotosReader.ReadAsync(Json("{\"formatVersion\":1,\"photos\":[" + Valid + "]}"));

        Assert.Equal("img/sites/a.jpg", PhotosReader.Find(document, "a")!.File);
        Assert.Null(PhotosReader.Find(document, "b"));
    }

    [Fact]
    public async Task Absolute_or_parent_paths_are_rejected()
    {
        foreach (var file in new[] { "/img/a.jpg", "../a.jpg", "https://x.org/a.jpg" })
        {
            var photo = Valid.Replace("img/sites/a.jpg", file);
            await Assert.ThrowsAsync<DataFormatException>(() => PhotosReader.ReadAsync(Json("{\"formatVersion\":1,\"photos\":[" + photo + "]}")).AsTask());
        }
    }
}
